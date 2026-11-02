# Research Brief — Unstructured-Text (Vector-RAG) Retrieval Shape (ASP Epic 16 seed)

**Author context:** Kushal Sharma / ASP
**Date:** 2026-08-08
**Branch:** `experiment/unstructured-rag`
**Author (planner):** Athena — Architect + Requirements Owner hats, via multi-agent SOTA survey + adversarial invention workflow
**Mirrors:** [table_rag_research_brief.md](table_rag_research_brief.md) · [graph_rag_research_brief.md](graph_rag_research_brief.md)

**Hypothesis under test:** *The unstructured-text shape is the fourth and final ASP retrieval shape — flat ad-hoc passage retrieval over free-form text (FAQs, emails, claims, support tickets, policy prose) that has neither the heading hierarchy the document-tree shape exploits, the header/cell structure the table shape exploits, nor the entity-relation edges the graph shape exploits. Every current generation-free method scores a candidate by **one similarity scalar** (dense cosine, ColBERT sum-of-max, sparse dot, or fused rank), collapsing a query's multiple — sometimes negated, sometimes numeric — aspects into a single number. A NEW scoring operator that reads the **shape of the query→passage token-alignment evidence as a distribution/coupling, not a scalar**, can beat a same-condition dense+BM25 floor both where dense already wins (paraphrase/semantic sets) and where it fails (entity/faceted/jargon sets), while never reusing a named prior-art mechanism.*

**Grounding:** Three shapes are proven on the invent-to-beat trajectory —
- **Document-tree:** CUAD +14–19 pp EM, ObliQA +38 pp recall, GovReport-QS +55 pp token-recall.
- **Knowledge-graph:** `bison_grace_tars` 85.1% recall@20 STaRK / 90.0% recall@10 MuSiQue (+16.8 pp over SOTA MFAR*).
- **Semantic-table:** `asptable` beats the flat-dense floor on all 4 IBM-Research corpora, 3/4 ≥90% recall@10.

This brief ports that discipline to `src/asp/retrieval/vector/` (currently an empty `__init__.py` — greenfield). It closes the four-shape ASP architecture and supplies the flat-RAG contrast the orchestrator (E8) and the paper's comparative claim need.

---

## ⚖️ PATENTABILITY / NOVELTY MANDATE (hard constraint — set by Kushal)

The invented ASP unstructured-text method must be **designed for novelty** — novel AND non-obvious over
prior art — and must **NOT reuse, re-implement, or be an obvious variant of any named open-source /
published method.** Prior art to design **AROUND**, never build **on** (see §3 for the full design-around table).

**Honesty rule (non-negotiable):** We **cannot** certify patentability here — that requires the formal
prior-art scan + attorney review (story E16-S14 → E11-P2). The correct language throughout is
*"designed for novelty with an explicit distinction over &lt;named prior art&gt;, pending formal novelty
scan"* — **never "patentable."**

**Web/arxiv access is BLOCKED in-env** (confirmed 2026-08-08: both WebSearch and WebFetch fail for this
model, exactly as the table-shape session documented). Therefore **no published absolute recall@k /
nDCG@10 is verifiable**, and **every claimed win is stated vs a SAME-CONDITION FLOOR** — flat dense
`bge-small` cosine and BM25, run in the same process — never "beats SOTA &lt;number&gt;."

---

## Standing rules (carried from E14/E15, non-negotiable)

1. **Recall@k / nDCG@10 is the primary metric**, generation-independent, $0/query (local embedding + numpy/scipy).
2. **n ≤ 50 per query-slice** until a method clears its pre-registered per-slice pp bar on the primary
   dataset (the 85%-analogue gate) before scaling to n=500.
3. **Always `--checkpoint-dir`** — checkpoint after every query, resume-on-restart.
4. **No dataset-specific crutches** — one parameter set across datasets; all slice gates use a
   **dataset-agnostic query-property classifier**, never `if dataset == ...`.
5. **No test-split tuning** — all knobs (N, ε, τ, r*, β0) tuned on a **held-out validation slice** only.
6. **Same-condition comparison** — ASP numbers alone are not a result; the same-run flat-dense floor
   and BM25 floor are always the comparators.
7. **Honest negatives are first-class** — write up falsified methods in `docs/research/`.
8. **No silent truncation** — any token-cloud / candidate cap is logged in `dropped_items` on the
   `ContextBundle`.

---

## 1. SOTA survey (honest, cited-by-name, number caveats front and centre)

The unstructured-text shape is **ad-hoc passage/document retrieval over free text**, measured by
recall@k / nDCG@10 (BEIR/MS-MARCO). Every standing below is from established literature knowledge —
web is blocked, so treat every absolute number as approximate-from-memory and unverifiable.

| Class | Representative methods | Mechanism (one line) | Approx BEIR nDCG@10 | Key failure modes |
|---|---|---|---|---|
| **Dense bi-encoders** | DPR, ANCE, Contriever, E5, GTE, BGE, Nomic, Arctic | Independent single-vector encode; score = pooled cosine/dot | DPR ~0.35 · Contriever ~0.37–0.46 · E5-large ~0.50 · GTE-large ~0.52 · **bge-small ~0.51** · bge-large ~0.54 · Arctic-l ~0.55 | Single-vector bottleneck; rare-entity vocab mismatch; **negation/conditionality collapse** (cos(X, not-X)≈1); numeric/temporal literals averaged away; multi-aspect = one averaged centroid |
| **Late interaction** | ColBERT, ColBERTv2, PLAID | Per-token vectors; score = Σ_query max_doc cosine (MaxSim) | ColBERTv2 ~0.50 | 10–100× index; single-aspect domination (one high-sim token dominates); no numeric/negation logic; not $0/query numpy at scale |
| **Learned sparse** | SPLADE/++/v2, uniCOIL, DeepImpact | MLM-logit vocab-space term weights; sparse dot | SPLADE++ ~0.50 | Term-level (helps synonymy, not multi-hop/negation); numeric literals are just tokens; over-expansion drift |
| **Classical sparse** | BM25 | TF-IDF + length norm | ~0.44 | Zero recall under vocab mismatch/paraphrase; no semantics; no magnitude reasoning |
| **Rerankers** | monoBERT/T5, MiniLM-CE, RankLLaMA, RankGPT | Joint cross-encode → learned scalar (or LLM listwise) | +3–8 pt over stage-1 | Depth-limited (can't recover a stage-1 recall miss); LLM rerankers cost tokens (violates $0/query); weak on explicit constraint satisfaction |
| **Generative / query-aug** | HyDE, query2doc, doc2query | LLM synthesizes pseudo-doc / expansion | zero-shot gains | Costs a generation per query; **hallucinates the rare entities/jargon we most need help with** |
| **Hybrid fusion** | RRF, convex, TAS-B | Rank/score arithmetic over dense+sparse | ~0.48–0.53 | Blends two flawed views; no understanding of *why* a query is hard; can't target coverage/constraints; a query both retrievers miss stays missed |
| **Structure-over-text (OTHER shapes)** | RAPTOR, HippoRAG, GraphRAG | Cluster-tree / KG+PPR / community-summary over corpus | — | Change the *shape* (impose tree/graph); belong to ASP's document-tree/graph shapes; cores (clustering, PPR) barred here |

**Single current best general-purpose generation-free approach:** there is no single winner across query
types. The honest state of the art is a **dense (BGE-large/Arctic/E5-large, ~0.52–0.55) ⊕ learned-sparse
(SPLADE++, ~0.50) hybrid via RRF**. Pure single-vector dense tops out around **BEIR nDCG@10 ≈ 0.55** —
that ceiling is the thing to beat.

**Failure classes NO generation-free method solves well** (the invention targets — where dense, sparse,
late-interaction, and their RRF hybrids all degrade, and rerankers help only if stage-1 surfaced the doc):

1. **Vocabulary mismatch on rare/unseen entities** — SKUs, ticket/case IDs, part/drug numbers.
2. **Multi-aspect / faceted queries** — "refund policy for international orders damaged in transit"; one
   dominant aspect starves the others (aspect-coverage failure).
3. **Long-tail domain jargon** — enterprise-internal terms, codenames absent from web pretraining.
4. **Negation / conditionality** — "policies that do NOT cover flood"; cos(X, not-X) ≈ identical.
5. **Numeric / temporal constraints in prose** — "claims over $10,000 filed after 2021."
6. **Aspect coverage for multi-need queries** — the objective optimizes single-point similarity, not
   *set coverage* of decomposed needs.

**The common thread:** every current method collapses a query's multiple aspects into **one similarity
scalar**. No generation-free method does explicit per-aspect **coverage accounting** or **constraint
satisfaction** as first-class math at retrieval time. That seam is the invention target.

---

## 2. Datasets + metrics + licenses (recall@k-scorable, real, permissive, enterprise)

All candidates are real, permissively licensed, credential-free (public HF, no gated repo/EULA), and
ship gold qrels so recall@k / nDCG@10 is scorable generation-free. **The selection deliberately spans
BOTH regimes** — where existing methods already do well AND where they fail — per Kushal's win-everywhere
mandate.

| # | Dataset | Domain | Corpus | #Q (test) | Gold | License | Load | Baseline class |
|---|---|---|---|---|---|---|---|---|
| 1 | **FiQA-2018** | Finance forum/StackExchange QA | ~57K | 648 | binary (~1.2 rel/q) | **Apache-2.0** ✅ | `BeIR/fiqa` — **loader exists** | **LOW** (vocab mismatch, colloquial finance) |
| 2 | **NFCorpus** | Medical/nutrition IR | 3.6K | 323 | graded (dense) | Harvard open ✅ | `BeIR/nfcorpus` | **HIGH** (graded → nDCG-friendly) |
| 3 | **ArguAna** | Counter-argument retrieval | ~8.7K | 1,406 | binary (1/q) | CC-BY 4.0 (verify snapshot) | `BeIR/arguana` | **LOW** (argument stance ≠ topical match) |
| 4 | **CQADupStack** | Community QA dup-question (12 subforums) | ~40K/sub | ~13K | binary | **Apache-2.0** ✅ | `BeIR/cqadupstack` | **LOW** (question↔question paraphrase, faceted) |
| 5 | SciFact | Scientific claim verification | ~5.2K | 300 | binary (~1.1/q) | CC-BY-**NC** 2.0 ⚠️ | `BeIR/scifact` | HIGH (clean claim↔abstract) — research-only |
| 6 | TREC-COVID | Biomedical (COVID) | ~171K | 50 | graded (dense) | CORD-19 DLA ⚠️ | `BeIR/trec-covid` | HIGH (saturated) — research-only |
| 7 | Quora | Duplicate-question | ~523K | 10K | binary | Quora ToS (NC) ⚠️ | `BeIR/quora` | HIGH (near-duplicate) — research-only |

Reserve: **Touché-2020** (`BeIR/webis-touche2020`, CC-BY-4.0) — extreme low-baseline argument retrieval
(famously ranks below BM25 for dense); tiny query count (49) → noisy; tertiary robustness check only.

**Recommended PRIMARY / SECONDARY split (mirrors STaRK/MuSiQue, NQ-Tables):**
- **PRIMARY (beat-target, LOW-baseline): FiQA-2018.** Loader in-repo; genuinely enterprise (financial
  forum/support-style prose = the charter's "FAQs, claims, tickets"); a known vocabulary-mismatch dataset
  where dense and BM25 both leave headroom; sparse qrels (~1.2 rel/q) make both recall@k and nDCG@10
  meaningful. **The STaRK-PRIME analogue.**
- **SECONDARY (no-regression, HIGH-baseline): NFCorpus.** Small, clean, graded qrels → nDCG@10 is the
  natural metric; Harvard-open license (avoids SciFact's CC-BY-NC landmine). The "existing methods already
  do well" foil — the mandate requires winning where others succeed AND fail. **The MuSiQue analogue.**
- **Breadth:** ArguAna + CQADupStack (LOW) and Quora/TREC-COVID (HIGH, research-only).

**License discipline:** build **headline claims only on the Apache/open subset — FiQA, CQADupStack,
NFCorpus** — to keep the enterprise-IP framing landmine-free. SciFact/Quora/TREC-COVID are research-only
breadth (NC/DLA); load at runtime, never vendor the corpus as an ASP artifact.

**Metric.** Reuse `metrics/retrieval_metrics.py` (`ndcg_at_k`, `recall_at_k`, pytrec_eval path) for
graded/binary qrels; reuse `metrics/table_recall.py`'s `slice_report` + `classify_query_property` pattern
for per-slice breakdowns, extended with a `participation_ratio` (r_eff) facetedness gate (§4).

---

## 3. Prior-art / design-around — the do-not-read-on set

Bar key: **S** = singly-barred (named prior art). **D** = doubly-barred (also an existing ASP graph/table
primitive — reusing it collides with published/internal ASP work).

| # | Prior-art mechanism | Inventive step it claims | What the NEW method must NOT reuse | Bar |
|---|---|---|---|---|
| 1 | Dense bi-encoders (DPR/E5/BGE/GTE) | Score = single pooled query·passage inner product | Any scheme whose *final score IS a pooled dot* (embeddings may be an input, not the operator) | S (also FLOOR) |
| 2 | ColBERT / late interaction | Score = Σ_query max_doc token-sim (MaxSim) | The sum-of-per-query-token-max reduction of the alignment grid | S (present as `late_maxsim` comparator) |
| 3 | SPLADE / uniCOIL | Vocab-dim sparse weights via MLM logits; sparse dot | Learned per-vocab expansion weights; sparse dot as the score | S |
| 4 | Cross-encoder / LLM rerankers | Learned joint encode → relevance scalar | Any learned joint reranker; any generation in the scoring path | S |
| 5 | HyDE / query2doc / doc2query | LLM-synthesized pseudo-doc / expansion | LLM-synthesized text in the retrieval path | S |
| 6 | RRF / convex fusion | Reciprocal-rank / convex blend of two ranker lists | Rank/score fusion of two rankers as the combiner | **D** (`asptable` was explicitly motivated *away* from symmetric RRF) |
| 7 | PRF / RM3 / Rocchio | Pseudo-relevance feedback: reform query toward top-k centroid | Query re-formation from feedback; centroid/term-mass expansion | S |
| 8 | RAPTOR | Recursive cluster + LLM-summarize into a tree | Hierarchical cluster-summary nodes over the corpus | S (document-tree territory) |
| 9 | HippoRAG | PPR over an OpenIE KG seeded by query entities | PPR over an entity graph; OpenIE-triple graph | **D** (PPR barred) |
| 10 | GraphRAG | Community detection + LLM community summaries | Community-summary retrieval; LLM graph-summary index | S (graph territory) |
| 11 | ANCE / hard-negative mining | Training-time hard-negative curriculum | Encoder training/fine-tuning tricks (we are frozen, inference-time) | S |
| 12 | Matryoshka embeddings | Truncatable nested-dimension embeddings | Prefix-dimension coarse-to-fine cascade as the mechanism | S |
| 13 | ASP `PPR` | RWR mass propagation over a built graph | Any graph-walk mass propagation | **D** |
| 14 | ASP `BM25-seed` | BM25 to seed a candidate set for downstream propagation | BM25 as an internal seeding stage (allowed only as external floor) | **D** |
| 15 | ASP `dense-seed` | Dense top-N to seed a pool a 2nd stage reranks | Dense-cosine-as-seed feeding propagation (top-N as *input pool* is fine) | **D** |
| 16 | ASP `NEVA` | Rerank a candidate by evidence from its graph neighbors | Neighbor-borrowing; scoring a passage using its neighbors' query-similarity | **D** |
| 17 | ASP `asptable` | Gap-damped monotone additive lexical-field boosts (`boost·exp(-gap/β)`) on a dense pool + superlative gate | The `exp(-gap/β)` rank-safe form; IDF-field grounding as the boost source | **D** (self-collision) |

**Summary of what is closed:** any score that is **(i)** a pooled/token inner product, **(ii)** a learned
joint/expansion model, **(iii)** any generation, **(iv)** rank/score fusion of two rankers, **(v)** query
reformation from feedback, **(vi)** graph-walk/neighbor-evidence mass, or **(vii)** gap-damped additive
lexical boosts on a dense pool. Dense cosine and BM25 survive **only as floors/comparators**. A dense
top-N **pool** may be consumed as an input candidate set (the bar is on the *scoring operator*, not
candidate generation).

**White space (ranked by novelty × likely signal):**
- **(A) Optimal-transport geometry of the token-alignment cloud — LEAD.** Score off **functionals of the
  entropic-OT coupling** between query- and passage-token clouds (coverage, plan sharpness), *not* the
  transport cost. An answering passage has a sharp, mass-conserving, near-doubly-stochastic coupling
  (each query concept covered by a matching span); a topical non-answer has a diffuse coupling at equal
  mean similarity. Distinct from all 17 bars; captures coverage + specificity in one operator.
- **(C) Calibration/uncertainty off the neighborhood similarity distribution** — a passage whose score is
  a sharp outlier above its local density is more reliably relevant than one buried in a cluster of
  near-ties at equal cosine. Distinct from NEVA (reads the *score distribution*, transfers no evidence).
- **(B) Submodular aspect-coverage over decomposed query facets** — gated behind a dataset-agnostic
  aspect-count classifier for compound enterprise queries.
- **(D) Spectral properties of the query-conditioned Gram** — higher risk; collapses toward PPR/centrality
  if the affinity graph is walked. Fallback only.

---

## 4. THE INVENTED METHOD — `covert` (COVerage-VErified Regularized Transport, aspect-gated)

**One-line thesis:** Rerank the dense top-N pool by a **non-negative, calibration-gated boost read off
structural functionals of the entropic-OT coupling** between the query and passage token clouds —
*worst-covered aspect (soft-min) × plan sharpness − chance coupling* — with the coverage term routed by a
closed-form query-multiplicity gate, so passages whose transport plan **verifies every query facet is
covered by a sharp, mass-conserving alignment** are promoted while diffuse topical false-positives stay put.

This is the champion of the adversarial invention workflow (5 proposals × 3 judges each). It is a
**principled 2-graft fusion**, not raw COVERT: it folds in the participation-ratio aspect gate (from the
SPARC proposal) to repair COVERT's admitted weakness (soft-min inert on single-aspect queries), and adds
a light anisotropy correction to counter the token-cloud-collapse risk all three judges flagged.

### Exact math

**Floor / pool.** `bge-small-en-v1.5`, mean-pooled, L2-normed. `s_D(q,p) = cos(pool q, pool p)`. Retrieve
top-**N = 200** pool. COVERT reranks only the pool (numpy/scipy, $0/query).

**Token clouds + anisotropy correction (substrate — repairs the collapse risk).** Query tokens `U={u_i}`,
passage tokens `V={v_j}` from last hidden states (drop special/pad, cap `n_max=180`, log dropped count in
the `ContextBundle`). Compute a **global token-mean `m_tok`** over a 20–50k-token corpus sample once at
index time; set `ũ = normalize(u − m_tok)`, `ṽ = normalize(v − m_tok)`. De-saturates anisotropic token
cosines so the OT cost is informative. *(Substrate to make cosines meaningful — NOT the scorer; a light
global mean-removal, never `Σ^{-1/2}` whitening — see §5 novelty vs BERT-flow.)*

**Masses (dataset-agnostic IDF, precomputed once).** `a_i ∝ idf(w_i)`, `b_j ∝ idf(w_j)`, each on the
simplex (uniform fallback). Puts transport budget on rare/informative tokens (SKUs, tickers, case IDs).

**Cost + entropic OT (Sinkhorn, ε=0.05, L=50):**
```
S_ij = cos(ũ_i, ṽ_j) ;  C_ij = 1 − S_ij
K = exp(−C/ε) ; f←1_m, g←1_n ; repeat L: f = a/(Kg), g = b/(Kᵀf) ; T = diag(f)·K·diag(g)   # T ∈ U(a,b)
```

**Three plan-functionals (the operator):**
```
(1) coverage:   c_i = (1/a_i) Σ_j T_ij S_ij ;  Ψ = −τ log Σ_i a_i exp(−c_i/τ)      # soft-MIN over aspects, τ=0.1
(2) sharpness:  π_i(j) = T_ij/a_i ; H_i = −Σ_j π_i(j) log π_i(j) ; Σ_sharp = 1 − (Σ_i a_i H_i)/log n ∈ [0,1]
(3) chance:     S̄ = aᵀ S b         # value of the product coupling a·bᵀ ≈ what the pooled dot already encodes
    transport value: V_T = Σ_ij T_ij S_ij    # coverage aggregated by mean (single-aspect fallback)
```

**Aspect-multiplicity gate (SPARC graft — repairs the single-aspect weakness).** Over query tokens only:
```
G_ij = ũ_i·ũ_j ;  r_eff = (tr G)² / ‖G‖²_F = m² / Σ_ij (ũ_i·ũ_j)²  ∈ [1, m]
g_asp = clip((r_eff − 1)/(r* − 1), 0, 1),  r* = 2.2
```
`r_eff→1` (single-aspect) ⇒ `g_asp≈0`; large `r_eff` (compound query) ⇒ `g_asp≈1`. Closed-form, zero
learned params, reads query geometry only, dataset-agnostic.

**Non-negative OT boost (gated interpolation of coverage vs concentration):**
```
B_OT = max(0,  Σ_sharp · [ g_asp·(Ψ − S̄) + (1−g_asp)·(V_T − S̄) ] )
```
Multi-aspect (`g_asp→1`): all-facets-covered soft-min signal. Single-aspect (`g_asp→0`): excess-
concentration precision signal. `Σ_sharp` shrinks diffuse plans → 0; `−S̄` removes the bilinear mass the
dense score already captured (anti-double-count).

**Rank-safe Calibration-Gated Promotion — Φ(z), NOT `exp(−gap/β)`:**
```
μ_q, ρ_q = median, MAD of {s_D(q,p) : p ∈ pool}
z = (s_D − μ_q)/(ρ_q + 1e-3)
f(q,p) = s_D(q,p) + β0·Φ(z)·B_OT,   β0 = 0.15, Φ = standard-normal CDF
```
Sort by `f`. **One global param set, all datasets:** `N=200, ε=0.05, τ=0.1, L=50, r*=2.2, β0=0.15`.

**Rank-safety.** `B_OT ≥ 0 ⇒ f ≥ s_D` pointwise (never demotes a passage below its own dense score).
`Φ(z)` gates on the pool *distribution*: strong dense hits (`z≫0 ⇒ Φ≈1`) keep full boost and stay
protected; pool-floor noise (`z<0 ⇒ Φ→0`) is throttled and cannot leapfrog. Distinct from `asptable`'s
`exp(−gap/β)` gap-to-top damp — this is a robust z-CDF on the score distribution.

### Grafts folded in / dropped

**FOLD IN:** SPARC's participation-ratio `r_eff` aspect gate (routes coverage vs concentration);
CRISP's **AUC-first falsification discipline** (score the signal's gold/non-gold separation *before*
building the combiner — the arc that killed MACD/MFDS); COVERT's own Φ(z) combiner.

**DROP (with reason):** EGO-Rank occupancy π (all 3 judges: **inverts** on low-baseline — distractors form
the dominant covariance mode, so the boost lifts distractors); its eigen-gap γ (a redundant second gate —
Φ(z) + r_eff already cover confidence + multiplicity; held in reserve). CRISP Fisher whitening as scorer
(1/√λ amplifies bge-small's low-variance noise tail — ZCA blow-up). ORCA ρ self-gate (redundant with
r_eff; mean-subtraction is rank-invariant in-pool). SPARC submodular φ + k-means clustering (redundant
with attention-pooling; k-means unstable on anisotropic tokens — keep only the gate).

---

## 5. Novelty position (pending formal novelty scan)

Designed for novelty with explicit distinction over each closest named prior art — **no "patentable," no
absolute-SOTA claim:**
- **vs dense bi-encoders:** the pooled dot is (approximately) the `S̄ = aᵀSb` term COVERT *subtracts out*;
  the score is the residual structural evidence, provably not a pooled inner product.
- **vs ColBERT MaxSim:** MaxSim is a greedy, asymmetric, mass-*unconstrained* sum-of-max fooled by one
  high-sim token. COVERT is mass-*conserving* (`Tᵀ1=b`) and aggregates by soft-*MIN* — opposite semantics;
  a passage token cannot fake-cover two aspects.
- **vs SPLADE/uniCOIL:** no vocab-space weights, no MLM logits, no sparse dot — pure dense-token OT geometry.
- **vs cross-encoder / LLM rerankers:** no learned joint model, no generation — numpy Sinkhorn on frozen
  embeddings, $0/query.
- **vs HyDE/query2doc/doc2query:** no synthetic text anywhere.
- **vs RRF / convex fusion (doubly-barred):** one ranker, one additive evidence term — no second rank list.
- **vs PRF/RM3/Rocchio:** query cloud fixed; no feedback expansion, no re-retrieval.
- **vs WMD / Word Rotator's Distance / MoverScore / Sinkhorn-attention (the sharpest OT-IR collision):**
  those score the scalar transport **cost** `min⟨T,C⟩` (or OT-as-attention similarity). COVERT **discards
  cost** and scores **functionals of the plan** — soft-min coverage × entropy sharpness − product-coupling
  baseline, gated by a robust z-CDF. Two passages with identical WMD cost score differently. **The Rung-2
  cost-ablation explicitly tests this and kills the method if false.**
- **vs whitening / all-but-the-top / BERT-flow:** only a light global token-mean removal as substrate to
  de-anisotropize cosines — never `Σ^{-1/2}` whitening, never the scorer.
- **vs ASP `asptable` (bar 17):** different boost *source* (OT coupling geometry vs lexical IDF-field),
  different rank-safety *device* (Φ(z) pool z-CDF vs `exp(−gap/β)` gap-damp), no superlative gate.
- **vs ASP `NEVA` (bar 16):** no neighbor-evidence transfer — each passage scored from its own token cloud;
  the gate reads the *score distribution*, moves no mass between passages.
- **vs ASP `PPR`/`BM25-seed`/`dense-seed` (bars 13–15):** dense top-N is an input **pool** only; no graph
  walk, no seeding-into-propagation; BM25 only as an external floor comparator.

---

## 6. Falsification ladder (ordered, cheapest first — mirrors the graph shape's honest arc)

Run FiQA (low) + NFCorpus (high), n=50, one param set. **COVERT is DEAD if any rung fires.** This is the
same kill-first discipline that falsified MACD/MFDS (graph) and SIEVE/SPECTRA/ACG (table).

- **Rung 0 — Collapse check (cheapest, no combiner; addresses the #1 judge kill).** After anisotropy
  correction, inspect the token-cosine / `S̄` distribution and `Ψ − S̄`. If token cosines still saturate
  (`S̄→1`, `Ψ−S̄≈0` for >90% of pool) ⇒ the OT plan collapses toward the product coupling ⇒ `B_OT≈0` ⇒
  no-op ⇒ **DEAD** (the NQ-Tables/STaRK embedding-ceiling analogue — kill before investing).
- **Rung 1 — AUC (CRISP graft).** `AUC(B_OT ; is_gold)` within each pool. If ≤ 0.55 ⇒ the plan-functional
  carries no relevance signal ⇒ **DEAD**.
- **Rung 2 — Cost ablation (novelty kill).** Replace `B_OT` with WMD cost `−⟨T,C⟩`. If it matches/beats
  COVERT ⇒ the structural-functional thesis is cosmetic ⇒ **DEAD** (novelty falsified).
- **Rung 3 — Lift where there's headroom.** FiQA recall@10 / nDCG@10 ≥ floor **+1.5pt** (n=50), else **DEAD**.
- **Rung 4 — No-regression.** NFCorpus nDCG@10 ≥ floor **−1pt**, else rank-safety broken in practice ⇒ **DEAD**.
- **Rung 5 — Attribution ablations (scope the claim, not kills):** sharpness-null (`Σ_sharp≡1`),
  baseline-null (drop `−S̄`), gate-null (`g_asp≡1`). Report honestly which term carries the win.

**Single decisive kill:** an n=50 FiQA run where COVERT recall@10 ≤ floor **and** the WMD-cost ablation
≥ COVERT.

---

## 7. Build plan (`src/asp/retrieval/vector/`)

Mirror the semantic-table shape exactly (arm registry + `RetrievalContext` + embedding cache + ablation
harness). **Arms:**
1. `flat_dense` — bge-small mean-pooled cosine (FLOOR).
2. `bm25` — rank_bm25 over corpus (second FLOOR; paraphrase-slice comparator).
3. `late_maxsim` — ColBERT sum-of-max on the pool (honest comparator, as in the table shape).
4. `covert` — the champion (§4).

**Files (new):**
- `src/asp/retrieval/vector/vector_retriever.py` — mirror `table_retriever.py`: `build_index`,
  `RetrievalContext`, `ARM_REGISTRY`, `retrieve`, bge-small cache under `~/.cache/asp_vector_embeddings/`.
  **Compute decision:** cache **pooled** doc vectors for the full corpus, but compute **token clouds ONLY
  for docs that enter any query's top-N=200 pool** (per-corpus cache) — avoids ColBERT-scale per-token
  storage over 57K FiQA docs and keeps the method tractable/$0-query.
- `src/asp/retrieval/vector/arms.py` — the four arms + Sinkhorn helper (scipy), IDF-mass builder,
  anisotropy `m_tok`.
- `src/asp/retrieval/vector/query_predicates.py` — `participation_ratio(query_tokens) → r_eff`; reuse
  `classify_query_property` from `metrics/table_recall.py` for slicing.
- `src/asp/benchmarks/utils/loaders/retrieval/beir_loader.py` — generic `load_beir(name, split, limit)`
  returning the existing `FIQADataset`-shaped `{queries, corpus, qrels}`; register
  fiqa/nfcorpus/arguana/cqadupstack/scifact/quora/trec-covid (one generic file, not seven).
- `src/asp/benchmarks/vector_retrieval_ablation.py` — clone `table_retrieval_ablation.py` (arg-parsed
  `--dataset --limit --top-k --arms --checkpoint-dir`, per-query resume, aggregate + slice report).

**Metric:** reuse `metrics/retrieval_metrics.py` (`ndcg_at_k`, `recall_at_k`, pytrec_eval) for graded/binary
qrels; reuse `table_recall.py`'s `slice_report` + `classify_query_property` for per-slice breakdowns.

**Config:** `configs/experiments/vector_covert.yaml` with the one global param set (reproducibility clause).

**CLI:**
```
python -m asp.benchmarks.vector_retrieval_ablation --dataset fiqa --limit 50 \
  --top-k 10 --arms flat_dense bm25 late_maxsim covert --checkpoint-dir /tmp/vec_ckpt
```

---

## 8. Risk register

| # | Risk | Severity | Mitigation |
|---|------|----------|------------|
| 1 | **Embedding ceiling (NQ-Tables/STaRK analogue).** bge-small token states are mean-pool-trained + anisotropic → OT plan collapses to product coupling → `B_OT≈0` no-op. Flagged by **all three** COVERT judges — this is the single biggest risk. | **HIGH** | Global token-mean anisotropy correction (substrate). **Rung-0 collapse kill runs first** — abort before compute if it fires. Fallback: switch token backend to a late-interaction-trained encoder (e.g. a ColBERT checkpoint) *only as embedding source*, keeping the OT operator (still $0/query, frozen weights) — documented, not default. |
| 2 | **Prior-art collision** with WMD / Word Rotator's Distance / MoverScore / Sinkhorn-attention. | HIGH | Novelty rests on plan-*functional* composition + Φ(z) gate, not transport cost. Rung-2 cost-ablation explicitly tests it. "Pending formal novelty scan" language; no absolute-SOTA number. |
| 3 | **Coverage term inert on single-aspect queries** (COVERT's own admission → win collapses to ~+1pt). | MED | `r_eff` gate routes to the concentration term when `g_asp→0`; Rung-5 gate-null ablation attributes the win honestly. If gate inert, down-scope claim to "precision via plan sharpness." |
| 4 | **Compute / token-cloud storage** (ColBERT-scale over 57K FiQA docs). | MED | Token clouds only for pool-appearing docs; per-corpus cache; `n_max=180` cap with dropped-count logged (no silent truncation). Sinkhorn is `N·L·m·n` ≈ tens of M mults/query, vectorized, sub-second. |
| 5 | **soft-min high variance** on rare-IDF tokens that paraphrase-mismatch in the true passage. | LOW-MED | `τ=0.1` (not →0) softens the worst-case aggregator; IDF masses capped; report the r_eff-high slice separately. |
| 6 | **No help on negation/numeric (failure classes 4/5).** | LOW (scoped out) | Stated up front — COVERT is coverage + specificity, not logic. Not claimed; not a failure. |

---

## 9. Honest note on the adversarial judging

**Every one of the 5 proposals scored low on expected-win (3–4/10 across the 3-judge panels)** — a signal
worth recording, not hiding. The judges converged on one root risk: **frozen bge-small token embeddings
are mean-pool-trained and anisotropic**, so any operator over token clouds risks collapsing to a no-op.
COVERT was chosen as champion because its flaws are *risk* (embedding ceiling — mitigable, testable at
Rung 0) rather than *inversion* (EGO-Rank's occupancy provably lifts distractors on the target regime).
The honest posture: **COVERT is a well-motivated bet with an explicit, cheap kill-first ladder — not a
sure win.** If Rung 0 or Rung 1 fires, the correct outcome is a first-class negative result (the token-
embedding ceiling for OT-over-frozen-bge-small), documented exactly as the graph/table shapes documented
their falsified arms, and the fallback is a late-interaction-trained token encoder as the embedding source.

**Bottom line:** Build **COVERT (aspect-gated)** as the sole champion arm against a `flat_dense` + `bm25`
floor and a `late_maxsim` honest comparator, on **FiQA (primary) + NFCorpus (secondary)** with
ArguAna/CQADupStack breadth. Gate all investment behind **Rung-0 collapse + Rung-1 AUC**.
