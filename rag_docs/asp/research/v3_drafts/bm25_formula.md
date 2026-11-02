Path-augmented BM25 scores `TreeNode` objects rather than fixed-width chunks: each node carries its content plus its heading path ("Article 5 > Termination > Notice"), and the retriever ranks whole nodes against the query. The score is built in four layers — a BM25 term over the concatenation of content tokens and path tokens, an empty-container child aggregation step that lets pure section headers borrow their immediate children's tokens so they can still score, an additive heading-path overlap boost that fires only when the query and the path share ≥2 tokens, and a multiplicative depth bias that nudges the retriever toward leaves on contract corpora and toward shallow nodes on multi-hop QA. The full implementation is `score_nodes_bm25` in [bm25.py](src/asp/benchmarks/retrieval/bm25.py) (lines 109–191).

```text
Tokenisation (same for query and node content):
  drop tokens of length ≤ 2 and standard English stopwords
  (a, an, the, is, are, of, to, at, …)

Per-node document representation:
  content_tokens = normalise(title + " " + content)
  path_tokens    = normalise(heading_path)

Empty-container child aggregation (lets a section header whose
only payload is e.g. a table still score):
  if content_tokens is empty and node has child_node_ids
                              and a nodes_dict lookup is available:
    for each child with token_count > 0:
        extend content_tokens with normalise(child.title + " " + child.content)

Per-node document representation (final):
  doc_tokens = content_tokens + path_tokens
  (the heading-path tokens are appended once — this is the
   "half-field-weight" effect: they contribute to TF without
   inflating doc length more than necessary)

IDF over the corpus of N nodes:
  df(t) = Σ_nodes [t ∈ content_set] + 0.5 · Σ_nodes [t ∈ path_set \ content_set]
  IDF(t) = log( (N + 1) / (df(t) + 1) )
  (path tokens count 0.5 to df — they help discrimination
   without inflating IDF for generic headings like "Article")

Standard Okapi BM25 with k₁ = 1.5, b = 0.75:
  TF(t, d) = f(t,d) · (k₁ + 1) / ( f(t,d) + k₁ · (1 − b + b · |d| / avg_dl) )
  BM25(q, d) = Σ_{t ∈ q} IDF(t) · TF(t, d)

Heading-path overlap boost (fires only when ≥2 query tokens overlap):
  overlap = |query_token_set ∩ path_token_set|
  heading_boost = 0.5 · overlap     if overlap ≥ 2
                = 0                  otherwise

Depth multiplier (prefer_leaf is set per-corpus):
  prefer_leaf = True   (contract corpora — CUAD, MAUD)
    depth_mult = 1.2   if depth ≥ 2
                 0.9   if depth < 2     ← penalises shallow section headers

  prefer_leaf = False  (HotpotQA, Travel Policy, JCP Credit)
    depth_mult = 1.1   if depth == 1
                 1.0   otherwise

Final node score:
  score(node, query) = ( BM25(q, doc_tokens) + heading_boost ) · depth_mult
```

### What the knobs do

The depth multiplier is the weakest of the three ranking signals (the child-aggregation step is a precondition for scoring rather than a ranking signal — it decides whether an empty-container node enters the comparison at all). Its full dynamic range on contract corpora is 1.2/0.9 ≈ 1.33×, so it can only flip rankings between two nodes whose underlying BM25 + heading-boost scores are already within ~25% of each other. We did not run a dedicated `prefer_leaf` ablation on the five corpora in this study, so we cannot give a head-to-head Δ-EM number; based on the 1.33× dynamic range relative to the typical BM25 score gaps we observed in the per-query traces on CUAD/MAUD/HotpotQA/Travel Policy/JCP Credit, our working assumption is that it acts as a tiebreaker on a minority of queries and that turning it off would shift accuracy within noise — but this remains an empirical gap to close, not a demonstrated result. On these corpora the dominant signal is plain BM25 over `doc_tokens`; `depth_mult` is configured to bias toward leaves (CUAD/MAUD) or toward H1 anchors (HotpotQA, where the article title is usually the right hop). The heading-path overlap boost is the more decisive structural signal: +0.5 per overlapping token, gated at a minimum of 2 overlaps, which corresponds to ~10–25% of a typical BM25 raw score on these corpora. It bites hardest on structured documents where the question text repeats the section heading verbatim ("What is the *governing law*?" against a node on the "Governing Law" path) — exactly the CUAD/MAUD/EW shape. The half-weight on path tokens in the IDF denominator is what keeps generic headings like "Article" or "Section" from dominating; without it, every contract chunk would look equally relevant to any query that mentioned an article number. The empty-container child aggregation, finally, is what keeps section-header nodes whose only payload is a child table or a child list from being silently dropped from the ranking: without it, those headers would tokenise to nothing and score 0, so any query whose answer lives under such a header would have to hit the child node directly.

### Where this sits in §3's taxonomy

This formula is **structure-aware retrieval**, not structure-aware chunking: the heading path enters the ranking function as both a field (via `doc_tokens`) and a separate boost term, and ranking is performed over whole tree nodes. The contrast is with approaches like HiQA, which prefix the heading path onto flat fixed-size chunks and then run flat retrieval over the resulting strings — there the heading affects retrieval only through the chunk's bag-of-words, never as an explicit structural signal, and the unit of ranking remains a chunk rather than a node. That distinction is exactly what the EE-Struct experiment in §6.3 isolates by pitting this path-augmented BM25 against the structure-aware-chunking baseline in [flat_structured_rag.py](src/asp/benchmarks/pipelines/flat_structured_rag.py).