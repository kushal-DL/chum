# ASP Implementation Comparison — example-workflow2 (JCP Credit demo) vs Current Repo

**Date:** 2026-06-19
**Purpose:** Side-by-side, file-by-file, function-by-function comparison
of the ASP document-tree implementation in example-workflow2 (JCP Credit demo) (the working
reference) against the current `src/asp/benchmarks/` implementation —
plus a forensic explanation of why ASP appears to use *fewer* total tokens
than flat RAG on HotpotQA.

---

## TL;DR — the token mystery, resolved

You were right to be suspicious. The headline number (`avg_tokens` 1,151
ASP vs 1,822 flat) is **not** the input context size. It is
`response.usage.total_tokens` — the LLM's *prompt + completion combined*.

Here is the actual decomposition for the HotpotQA-50 run:

| Component (per question) | ASP | Flat |
|---|---:|---:|
| Input context tokens (tiktoken) | ~1,037 | ~1,330 |
| Prompt template overhead | ~30 | ~30 |
| **Estimated input tokens** | **~1,067** | **~1,360** |
| **Estimated output tokens** | **~84 (median 27)** | **~434 (median 403)** |
| `total_tokens` from API | 1,151 | 1,822 |

So the truth is:

- **Input tokens:** ASP genuinely *does* send slightly fewer input tokens
  on HotpotQA (~1,067 vs ~1,360). This is **not** because ASP magically
  removes tokens — it's because ASP picks 6 of 10 paragraphs and runs
  paragraph-extraction inside large nodes, while flat sends all 10
  paragraphs verbatim. The metadata overhead (60 tokens × 6 nodes ≈ 360
  tokens of headers + authority note + citations) **is** in there — it's
  just outweighed by what paragraph extraction drops.

- **Output tokens — this is the dominant gap.** ASP outputs ~27 tokens
  median (a tight phrase: *"Catherine, Duchess of Cambridge"*). Flat
  outputs ~400 tokens median — when given 10 paragraphs of mixed
  relevance, the model rambles, restates the question, hedges, lists
  alternatives. The compact ASP context with explicit `[Source §Section]`
  headers anchors the model to one answer.

So the claim to make in the paper is **not** "ASP uses fewer tokens than
flat". The honest claim is two-part:

1. **ASP input ≈ flat input** on multi-paragraph datasets. The protocol
   metadata overhead is genuinely real (~360 tokens for 6 nodes), and is
   approximately offset by retrieval pruning. On structured datasets
   (CUAD/SQuAD), ASP input is *larger* than flat (370 vs 328 tokens).
2. **ASP output is much smaller** because the structured context controls
   model verbosity.

The "76% cost reduction" thesis from CLAUDE.md is a *separate* claim —
that's about routing different shapes to different *backends* (small fine-tuned
30B vs frontier API), which this benchmark does not test. This benchmark
tests the document-tree shape against flat retrieval at the same model.

---

## Architecture comparison

### example-workflow2 (JCP Credit demo)

```
example-workflow2/ (JCP Credit demo)src/
├── parsers/
│   ├── markdown_parser.py    (215 lines) — heading-aware → DocumentTree
│   └── tree_builder.py       ( 78 lines) — TreeNode schema (Pydantic)
├── asp/
│   ├── schema.py             ( 89 lines) — ASPBundle, RetrievedNode, SourceAuthority
│   ├── bundle_assembler.py   (131 lines) — assemble_bundle() — the brain
│   ├── paragraph_extractor.py(264 lines) — extract_relevant_paragraphs()
│   ├── citation_builder.py   ( 21 lines) — build_citation()
│   └── token_budget.py       ( 51 lines) — tiktoken-based count_tokens
├── retrievers/
│   ├── tree_retriever.py     (425 lines) — BM25 + boosts + multi-hop
│   └── flat_retriever.py     (268 lines) — vector+BM25 hybrid (Chroma)
└── agents/
    └── orchestrator.py       (588 lines) — 10-node SSE pipeline
```

### Current repo

```
src/asp/
├── retrieval/document_tree/
│   ├── schema.py             — TreeNode (richer than jcp_credit: NodeType enum,
│   │                           clause_id, parent_node_id, child_node_ids,
│   │                           cross_refs)
│   ├── tree_builder.py
│   ├── retriever.py          — full ASP-spec tree retriever (production)
│   ├── cross_ref_resolver.py
│   └── parsers/
│       ├── markdown_parser.py
│       ├── docx_parser.py
│       └── _parsing_utils.py
└── benchmarks/
    ├── retrieval/                   ← NEW, ports the working primitives
    │   ├── bm25.py                  — path-augmented BM25 (from jcp_credit)
    │   ├── tree_traversal.py        — phase 1/2/3 (from jcp_credit)
    │   ├── paragraph_extractor.py   — verbatim port of jcp_credit
    │   ├── cross_encoder_reranker.py — NEW (not in jcp_credit)
    │   └── bundle_assembler.py      — port of jcp_credit with same 60-tok header
    ├── adapters/document_tree_qa.py — composes the primitives
    └── baselines/flat_qa.py
```

---

## File-by-file diff

### 1. Tree node schema

| Field | jcp_credit `parsers/tree_builder.py` | Current `src/asp/retrieval/document_tree/schema.py` |
|---|---|---|
| `node_id` | ✅ | ✅ |
| `node_type` | ✅ str ("section", "table", …) | ✅ NodeType enum (richer) |
| `doc_slug` | ✅ | ✅ |
| `title` | ✅ | ✅ |
| `content` | ✅ | ✅ |
| `depth` | ✅ | ✅ |
| `heading_path` | ✅ | ✅ |
| `parent_node_id` | ✅ | ✅ |
| `child_node_ids` | ✅ | ✅ |
| `clause_id` | ❌ | ✅ |
| `cross_refs` | ❌ | ✅ |
| `effective_date` | ❌ | ✅ |
| `parse_method`/`parse_confidence` | ❌ | ✅ |
| `token_count` | ✅ | ✅ |

**Verdict:** Current schema is a proper superset. No regression.

---

### 2. BM25 retrieval scoring

#### jcp_credit `retrievers/tree_retriever.py` (lines 33-50, 100-122, 215-318)

```python
# Path-augmented BM25 with these layers:
#   - normalise_tokens: drop stopwords, drop ≤2-char tokens
#   - per-corpus IDF: content tokens at 1.0×, path tokens at 0.5× weight
#   - empty-content child aggregation (sections with table-only kids)
#   - heading_path overlap boost: +0.5/token, requires ≥2 token overlap
#   - depth multiplier: 1.2 for depth≥2 in leaf scopes, 1.1 for depth=1 in broad
#   - scope_boost: +5 to +8 for travel-domain hint matches
```

#### Current `src/asp/benchmarks/retrieval/bm25.py`

```python
# Path-augmented BM25 with these layers:
#   - normalise_tokens: drop stopwords, drop ≤2-char tokens          [PORTED]
#   - per-corpus IDF: content 1.0×, path 0.5× weight                  [PORTED]
#   - empty-content child aggregation                                  [PORTED]
#   - heading_path overlap boost: +0.5/token, ≥2 overlap               [PORTED]
#   - depth multiplier: prefer_leaf flag (1.2 for ≥2 / 0.9 for <2)    [PORTED, generalised]
#   - scope_boost                                                      [DROPPED]
```

| Feature | jcp_credit | Current | Note |
|---|---|---|---|
| Stopword filter | ✅ | ✅ | Identical wordlist |
| Path-augmented IDF | ✅ | ✅ | |
| Heading-path boost | ✅ +0.5/token, ≥2 overlap | ✅ same | |
| Depth multiplier | ✅ scope-driven (5 scopes) | ✅ `prefer_leaf` flag | Simpler, generic |
| Empty-section child aggregation | ✅ | ✅ | |
| Travel-domain `scope_boost` (+5 to +8) | ✅ | ❌ | Hardcoded to a domain — does not generalise. Dropped. |
| Country/region keyword routing | ✅ | ❌ | Hardcoded for travel demo. Dropped. |
| Configurable `scope_keywords` from YAML | ✅ | ❌ | Useful but not yet ported; deferred. |

**The dropped bits are domain-specific to jcp_credit's travel demo and would
*not* help on HotpotQA / CUAD.** No accuracy regression on the public
benchmarks; documented loss for future domain-specific use.

---

### 3. Tree traversal (multi-hop expansion)

#### jcp_credit `retrievers/tree_retriever.py` lines 318-418

Three phases, all inside `TreeRetriever.retrieve()`:

```python
# Phase 1 — parent-child injection
#   for each top node:
#     if has child_node_ids: inject all populated table children
#     if is table: inject parent prose section
#
# Phase 2 — ancestor walk
#   for each top node, walk parent_node_id upward
#     include any ancestor with non-zero BM25 score
#
# Phase 3 — sibling scan
#   for each top node, look at parent.child_node_ids
#     include siblings whose score >= max(0.8 * weakest_top_score, 1.5)
#
# Hard cap: phase 2+3 never adds more than top_k extra nodes.
```

#### Current `src/asp/benchmarks/retrieval/tree_traversal.py`

```python
# expand_with_tree_traversal(seed_nodes, score_map, nodes_dict, *,
#                            table_node_predicate=None,
#                            expansion_cap=None,
#                            sibling_threshold_floor=1.5)
```

| Phase | jcp_credit | Current | Status |
|---|---|---|---|
| 1: parent/child injection | ✅ | ✅ | Identical logic |
| 2: ancestor walk | ✅ | ✅ | Identical logic |
| 3: sibling scan | ✅ | ✅ | Identical (max(0.8×weakest, 1.5)) |
| Expansion cap | ✅ top_k | ✅ configurable, defaults to len(seeds) | Same default |

**Verdict:** Verbatim port lifted out of the retriever class so any
adapter (HotpotQA, CUAD, future) can use it without inheriting the
travel-domain scope routing.

---

### 4. Paragraph extraction

#### jcp_credit `asp/paragraph_extractor.py` (264 lines)

Public API:
```python
tokenize_query(query) -> list[str]
extract_relevant_paragraphs(node, query_tokens) -> str
```

Branches:
- Table node ≤300 tok → return verbatim
- Table node >300 tok → keep header + separator + only matching rows
- Prose node ≤150 tok → return verbatim
- Numbered/bulleted list → keep only items whose words overlap query
- Definition lines (`**Term:** explanation` or `**Term** — explanation`) →
  keep only matching defs
- Plain prose → split on `\n\n`, keep paragraphs that mention query tokens

Hard guarantee: **never returns empty when content is non-empty** — falls
back to full content when no sub-content matches.

#### Current `src/asp/benchmarks/retrieval/paragraph_extractor.py`

**Verbatim port** (264 lines, same regex constants, same fallback rules).

| Aspect | jcp_credit | Current | Match |
|---|---|---|---|
| `_PROSE_THRESHOLD = 150` | ✅ | ✅ | identical |
| `_TABLE_THRESHOLD = 300` | ✅ | ✅ | identical |
| `_LIST_ITEM` regex | ✅ | ✅ | identical |
| `_DEF_LINE` regex | ✅ | ✅ | identical |
| `_TABLE_SEP` regex | ✅ | ✅ | identical |
| Stopword set | ✅ | ✅ | identical |
| Hard-fallback to full content | ✅ | ✅ | identical |

**This is the single highest-value piece** — the reason your token bills
on multi-paragraph nodes don't blow up.

---

### 5. Bundle assembly

#### jcp_credit `asp/bundle_assembler.py` (131 lines)

```python
def assemble_bundle(
    nodes, retrieval_reasons, source_authorities, query, query_normalised,
    policy_scope="general", travel_type="general", token_budget=1500,
) -> ASPBundle:
    # 1. Sort: (authority_level, original_BM25_rank)
    # 2. Per node:
    #     extracted = extract_relevant_paragraphs(node, query_tokens)
    #     cost = count_tokens(extracted) + 60
    #     if running + cost <= token_budget: include
    #     else: compression_applied = True (drop)
    # 3. Build context_for_model:
    #     [SourceName §LeafSection | ClauseID]\n
    #     {extracted}
    # 4. If multiple doc_slugs: prepend authority resolution note
    # 5. Build full citations list (separately, not inline)
```

#### Current `src/asp/benchmarks/retrieval/bundle_assembler.py`

```python
def assemble_context_bundle(
    nodes, query, source_authorities, *, token_budget=1500,
) -> tuple[context_for_model, citations, total_tokens, compression, auth_res]:
    # Same 5 steps. Returns a tuple instead of an ASPBundle Pydantic model
    # (the schema lives in src/asp/core/context_bundle.py and isn't needed
    #  in the benchmark loop).
```

| Step | jcp_credit | Current | Match |
|---|---|---|---|
| Authority + rank sort | ✅ | ✅ | identical |
| Per-node `extract_relevant_paragraphs` | ✅ | ✅ | identical |
| 60-token header overhead | ✅ | ✅ | identical |
| Drop nodes that overflow budget | ✅ | ✅ | identical |
| Compact `[Src §Leaf \| ClauseID]` header | ✅ | ✅ | identical |
| Authority resolution note | ✅ | ✅ | identical |
| Default budget = 1500 | ✅ | ✅ | identical |
| Returns Pydantic ASPBundle | ✅ | ❌ tuple | Benchmark doesn't need the wire object; protocol code (`src/asp/core/`) does |

---

### 6. The pipeline orchestration

#### jcp_credit `agents/orchestrator.py` (588 lines)

10-node SSE pipeline:
```
query_normalise → shape_classify → scope_detect → doc_select →
section_retrieve → bundle_assemble → token_budget → answer_generate →
citation_validate → complete
```

This is heavyweight because it serves a streaming UI. For benchmarking,
most of these nodes are no-ops (no UI, no shape classification — the
benchmark already knows the shape).

#### Current `src/asp/benchmarks/adapters/document_tree_qa.py::_run_full_pipeline`

```python
1. score_nodes_bm25(query, nodes, prefer_leaf=...)
2. seeds = top-K by score
3. expand_with_tree_traversal(seeds, score_map, nodes_dict)
4. cross_encoder_rerank(query, expanded, top_k=6)        ← NEW (not in jcp_credit)
5. assemble_context_bundle(top_nodes, query, authorities, token_budget=1500)
6. LLM call → answer
```

**Notable difference:** the cross-encoder reranker (step 4) is **new** —
jcp_credit did not have it. jcp_credit trusted BM25 + heading boosts + multi-hop
expansion to surface the right nodes. The current benchmark adds a local
cross-encoder pass because:

- HotpotQA has bridge entities where surface-vocabulary BM25 misses
  (e.g. "Mark Steven Johnson nationality" needs the Johnson Wikipedia
  page, which doesn't share words with "Ghost Rider director").
- The cross-encoder is local (`ms-marco-MiniLM-L-6-v2`), zero API calls,
  ~240ms batched.
- Same model jcp_credit's `flat_retriever.py` used (line 169 of that file).

---

### 7. What jcp_credit had that the current repo does NOT (yet)

| Feature | jcp_credit location | Why not yet |
|---|---|---|
| `intent_classifier.py` (LLM call to classify shape) | retrievers/ | Phase 3 — orchestrator E8 not built yet |
| `shape_router.py` (route to vector / table / tree) | agents/ | Phase 3 — orchestrator E8 |
| `safety.py` (PII redaction, output filters) | retrievers/ | Not in scope for benchmark; protocol-spec story |
| `result_formatter.py` (table → markdown rendering) | retrievers/ | UI concern; benchmark doesn't render |
| YAML-driven `scope_keywords` | tree_retriever | Travel-demo specific; deferred |
| Streaming SSE token meter | orchestrator | UI concern |
| Citation validation step | orchestrator | Deferred — citations are recorded but not validated against output |

**None of these affect the document-tree retrieval primitives.** They are
orchestrator / UI / domain-routing concerns that live above the
retrieval layer.

---

### 8. What the current repo has that jcp_credit did NOT

| Feature | Current location | Reason |
|---|---|---|
| Cross-encoder reranker | benchmarks/retrieval/cross_encoder_reranker.py | Improves multi-hop bridge-entity recall on HotpotQA |
| Real `NodeType` enum (not string) | retrieval/document_tree/schema.py | Type safety; same logic |
| `clause_id` extraction | retrieval/document_tree/parsers/markdown_parser.py | Required by ASP spec for legal docs |
| `cross_refs` resolver | retrieval/document_tree/cross_ref_resolver.py | Required by ASP spec for cross-document references |
| DOCX parser | retrieval/document_tree/parsers/docx_parser.py | ASP spec requires multi-format support |
| Public-dataset benchmark harness (Spider, FIQA, CUAD, HotpotQA) | benchmarks/ | All of E10-S9 |
| REPO_STRUCTURE.md naming rules | repo root | Prevents file naming drift |

---

## Function-level summary

The seven items I identified earlier are now in place:

| Capability | jcp_credit | Last week | Now |
|---|---|---|---|
| Stopword-aware tokeniser | ✅ | ❌ used `query.lower().split()` | ✅ ported |
| Path-augmented BM25 | ✅ | ❌ concatenated tokens | ✅ ported |
| Heading-path overlap boost | ✅ | ❌ | ✅ ported |
| Empty-section child aggregation | ✅ | ❌ | ✅ ported |
| Depth multiplier | ✅ | ❌ | ✅ ported (generalised via `prefer_leaf`) |
| Three-phase tree traversal | ✅ | ✅ partial | ✅ verbatim |
| Paragraph extraction | ✅ | ❌ sent whole node.content | ✅ verbatim port |
| Compact `[Src §Leaf]` header | ✅ | ✅ | ✅ |
| Authority resolution note | ✅ | ✅ | ✅ |
| 60-token header overhead | ✅ | ✅ | ✅ |
| Token budget = 1500 | ✅ | ❌ was 12,000 | ✅ |
| Cross-encoder reranker | ❌ | ✅ added | ✅ kept |

---

## Why the input tokens are still close to flat

You instinctively expected:

> ASP_input ≈ flat_input + metadata_overhead

That's true for any *fixed* set of nodes. The difference is that ASP's
node set is **smaller**:

```
HotpotQA per question:
  flat:  10 paragraphs × ~95 tokens each = ~950 input tokens (no headers)
  ASP:    6 nodes after cross-encoder
          paragraph-extracted content per node (often ~60% of original)
          + 60 tok header per node = 360 tok overhead
          ≈ 6 × (95 × 0.6 + 60) = 696 token equivalent
```

So on HotpotQA, ASP's input is *roughly* the same as flat — the metadata
overhead (~360 tokens of headers + auth note) exists but is offset by
dropping 4 paragraphs and shrinking the surviving ones via paragraph
extraction.

On CUAD/SQuAD (single-passage QA), ASP's input is **larger** than flat
(370 vs 328 tokens) because there's only one passage so there's nothing
to drop, and the metadata cost is pure overhead. The accuracy gain
(+6 pp EM) comes from the model being anchored to the source.

The big total_tokens delta (~670) is dominated by **output tokens**: the
flat baseline rambles when given undifferentiated context. This is a
real ASP win, but it's an *output verbosity* effect, not an input
metadata-savings effect.

---

## Recommended language for the paper

Honest framing — the kind that survives peer review:

> The ASP document-tree shape on HotpotQA matches flat retrieval on EM
> (56.0% vs 56.0%) and trails by 1.3 pp on F1 while sending an input
> context of comparable size despite the protocol's metadata overhead
> (~360 tokens for source headers, citations, and authority notes). On
> structured single-document QA (CUAD/SQuAD), ASP outperforms flat by
> +6 pp EM and +2.4 pp F1 at +13% input token cost. The protocol's
> measured *total*-token efficiency on HotpotQA (37% reduction) is
> primarily an *output*-side effect: the structured context with
> explicit `[Source §Section]` headers anchors the model to a single
> answer and reduces verbosity from a median of ~400 output tokens
> (flat) to ~27 (ASP).

The 76% cost reduction claim from the project north-star is a separate,
*orthogonal* claim about model-tier routing (small fine-tuned 30B vs
frontier API), which this benchmark does not test.

---

## Reproduction

```bash
# HotpotQA (50 questions, ASP + flat)
python -m src.asp.benchmarks.runner --datasets hotpotqa --backends asp flat --limit 50

# CUAD/SQuAD (50 questions, ASP + flat)
python -m src.asp.benchmarks.runner --datasets cuad --backends asp flat --limit 50

# Per-question raw data
data/benchmarks/hotpotqa_results.json
data/benchmarks/cuad_results.json
```
