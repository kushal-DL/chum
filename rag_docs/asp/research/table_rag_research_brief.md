# Research Brief — Semantic-Table Retrieval Shape (ASP Epic 15 seed)

**Author context:** Kushal Sharma / ASP
**Date:** 2026-08-07
**Branch:** experiment/graph-rag
**Author (planner):** Athena — Architect + Requirements Owner hats
**Method:** multi-agent SOTA survey (text-to-SQL, open-domain table retrieval, table reasoning, agentic Table-RAG) + adversarial prior-art claim-mapping under an explicit patentability mandate
**Hypothesis under test:** *the semantic-table shape is fundamentally a recall@k retrieval problem — the exact table analogue of the STaRK/MuSiQue graph setup that produced the 85.1% GRACE win. A NEW ranking operator that scores tables on structure the field's text/embedding retrievers cannot see (value distributions, functional dependencies, alignment rank, join-closure) can beat the published table-retrieval SOTA while never reusing a named prior-art mechanism.*

**Grounding:** The graph shape proved the invent-to-beat trajectory — `bison_grace_tars` reached **85.1% recall@20 STaRK / 90.0% recall@10 MuSiQue (n=50)**, beating published SOTA MFAR* (68.3%) by +16.8 pp, with a generation-free $0/query metric and no dataset-specific knob ([graph_realdata_v1_report.md](graph_realdata_v1_report.md), [graph_bison_v1_report.md](graph_bison_v1_report.md)). This brief ports that discipline to `src/asp/retrieval/semantic_table/` (currently an empty `__init__.py` — greenfield). The critical constraint that shapes every recommendation below is Kushal's **patentability mandate**: the ASP table-retrieval method must be designed for novelty — novel AND non-obvious over prior art — and must NOT reuse, re-implement, or be an obvious variant of any named open-source / published method. We cannot certify patentability here; that requires the E11-P2 prior-art scan + attorney review. Throughout, the correct language is **"designed for novelty with an explicit distinction over <named prior art>, pending formal novelty scan"** — never "patentable."

---

## 1. SOTA survey (honest, cited, with the number caveats front and centre)

The table field splits into three tasks. Only one — **open-domain table retrieval, measured by recall@k** — is the ASP semantic-table shape. Text-to-SQL execution and table reasoning are surveyed for completeness and to fix the E06 boundary, but they are **not** the E15 method.

### 1a. Text-to-SQL (surveyed, NOT the E15 method — this is E06's job)

| Benchmark | Metric | Published SOTA | Score | License | Note |
|---|---|---|---|---|---|
| Spider 1.0 | Execution Acc | MiniSeek (anon) | 91.2% EX / 81.5% test EM | CC BY-SA 4.0 | **Saturated / frozen since 2023** — not a battleground |
| BIRD-SQL (overall) | Execution Acc | AskData + GPT-4o (AT&T) | 81.95% test EX | CC BY-SA 4.0 | Exceeds 72.37% human test baseline; closed frontier-API pipeline |
| BIRD-SQL (single-model) | Execution Acc | Gemini-SQL2 (Google) | 80.04% test EX | CC BY-SA 4.0 | Best OPEN self-hostable tier: Arctic-Text2SQL-R1-32B 73.84%, XiYanSQL-QwenCoder-32B 69.03% |
| Spider 2.0-Lite | Success | Tencent Tianqiong + GLM 5.2 | 76.23% | Apache-2.0 / CC BY-SA | Enterprise BigQuery/Snowflake, account-gated |
| LiveSQLBench Base-Lite | Success | o3-mini | 44.81% | open | Contamination-free; exposes headroom saturated splits hide |

Named prior-art recipe (**barred for E15, owned by E06**): schema-linking / schema-pruning (CHESS Selector, RSL-SQL bidirectional pruning) → multi-path / decomposed candidate SQL (CHASE-SQL divide-and-conquer, MAC-SQL decomposer) → execute → self-consistency / execute-then-fix / taxonomy-guided repair (SQL-of-Thought), plus value-index fuzzy grounding (TCSR-SQL, +27.8% exec-acc). **Two reasons text-to-SQL is not the E15 primary:** (1) the live SOTA is dominated by closed frontier-API agent pipelines that violate ASP's on-prem + reproducibility + ≤30B constraints; (2) folding SQL generation in collapses E15 into the barred recipe and forfeits the patentability mandate. The retrievers we can honestly beat all report **recall@k**.

### 1b. Open-domain table retrieval (THE E15 method — recall@k)

| Method | Mechanism | Dataset | Reported result | License |
|---|---|---|---|---|
| **HELIOS** (arXiv:2603.02248, 2026) | Edge-based bipartite table-passage "star" subgraph retrieval; early+late fusion unified; star-level LLM refinement | OTT-QA | **relative +42.6% recall / +39.9% nDCG** vs prior SOTA (absolute not in abstract) | Wikipedia-derived, permissive |
| **THYME** (arXiv:2503.02251, 2025) | Field-aware hybrid matching: cells favour word/phrase (fine), titles favour passage (coarse); dense+lexical per field | NQ-Tables + OTT-QA | "significantly outperforms SOTA" (absolute in paper tables) | Wikipedia-derived, permissive |
| **CRAFT** (arXiv:2505.14984, 2025) | Training-free sparse→dense re-rank cascade; LLM-generated table summaries as retrievable surrogates | NQ-Tables | beats sparse/dense/hybrid (absolute in paper) | permissive |
| **T-RAG** (arXiv:2504.01346, 2025) | Hierarchical intra/inter-table memory index + multi-stage filter-rank + graph-aware prompting; introduced MultiTableQA (57,193 tables) | MultiTableQA | "leading recall/accuracy/runtime" (self-authored benchmark) | GitHub release — **verify LICENSE file** |
| **SAGE** (arXiv:2602.16964, 2026) | Offline chunk-similarity graph + percentile edge pruning; online seed → first-hop expand → hybrid dense+sparse filter | OTT-QA + STaRK | **+5.7 recall pts OTT-QA / +8.5 STaRK** | permissive |
| **CGPT** (arXiv:2601.15849, 2026) | Cluster-guided partial tables + LLM-generated synthetic queries as hard-negative contrastive supervision for the embedder | OTTQA/FetaQA/MimoTable/E2E-WTQ | **+16.54% avg Recall@1** over QGpT | mixed — verify |
| **TableRAG** (Google, arXiv:2410.04739, 2024) | Dual dense index — schema index (col name + dtype + example cells) + cell index (B most-frequent cells) + LLM query expansion | Arcade/BIRD million-token | schema +9.4% / cells +11.5% acc; degrades 83.1→68.4 at scale | Arcade/BIRD, permissive |

**HONESTY CAVEAT (load-bearing).** Every table-retrieval SOTA above reports either **relative gains** (HELIOS, SAGE, CGPT) or numbers on a **self-authored split** (T-RAG). No absolute recall@k is defensible from abstracts. **E15-S8 (SOTA-reproduction) must lock the absolute same-condition recall@k for THYME/CRAFT (NQ-Tables) and HELIOS (OTT-QA) into `docs/research/table_sota_repro_report.md` before any "beat SOTA" language is used** — exactly as E14 reproduced MFAR* 68.3% before claiming 85.1%.

### 1c. Table reasoning over a given table (surveyed, NOT E15 — context only)

WTQ TabLaP 90.4% (but under a non-comparable protocol; clean apples-to-apples GPT-4-class prompting = Mix-SC 73.6%); Chain-of-Table 67.31% prompting-only; TabFact ~90% (saturated); FinQA TAT-LLM-70B 76.81% EX (human 91.16%); TAT-QA 81.42 EM / 88.49 F1; **MultiHiertt MT2Net 37.05 EM vs 83.12 human — a ~45 pp gap, the single largest headroom in the table world**, driven by multi-hierarchical headers + multi-table retrieval + multi-step arithmetic. The one consistent winning pattern across this line is **decoupling computation from reasoning** (TAT-LLM's external executor +5 to +18 pp) — an empirical vindication of ASP's "the LLM never does arithmetic" principle, but a reasoning/E06 concern, not a retrieval mechanism.

---

## 2. Key datasets + metrics + licenses (recall@k-scorable, real, permissive)

Filter applied (ported from the graph brief §4 and E14): genuinely table-shaped, permissively licensed, loadable without credentialing, ships gold table-ids / gold table sets so **recall@k is scorable generation-free**.

| Dataset | Role | Task / Metric | Size | License | Load | Why it fits |
|---|---|---|---|---|---|---|
| **NQ-Tables** | **PRIMARY (beat-SOTA target)** | Single-table retrieval, recall@k | ~169K tables / ~9.6K queries | **CC-BY-SA 3.0** (Wikipedia) | ungated HF `google-research-datasets/nq_tables` | Cleanest pure-retrieval corpus with a reproducible SOTA (THYME, CRAFT); carries the numeric/lookup slices SIEVE/TRELLIS/FUGUE target. **The STaRK-PRIME analogue.** |
| **OTT-QA** | **SECONDARY** | Multi-hop table+text, recall@k / nDCG | ~400K tables + ~5M passages / ~2K dev-test | **MIT / CC-BY-SA** (Wikipedia) | GitHub `wenhuchen/OTT-QA` | Multi-hop corpus where SPECTRA (multi-facet) and BRAID (join-reachable bridges) have their falsification slice; SOTA to beat = HELIOS. **The MuSiQue analogue.** |
| **MultiTableQA** | **OPTIONAL (license PENDING)** | Multi-table retrieval, recall | 57,193 tables / 23,758 questions | **UNCONFIRMED** — GitHub `jiaruzouu/T-RAG` | HF/GitHub | Best large-scale STaRK-analog; the only source of BRAID's multi-table gold. **Do NOT commit until LICENSE is verified permissive; SOTA is self-authored — reproduce same-condition first.** |

**Screened OUT (license / task landmines):** Spider 2.0 (account-gated enterprise DBs; text-to-SQL, not recall@k); TAT-DQA (**CC-BY-NC-SA, non-commercial**); MultiHiertt / FinQA / TAT-QA (numeric-reasoning, not retrieval); HeteQA / Multi-Table-RAG-Lib (FT-RAG) / TCSR-SQL sets (unconfirmed licenses). BIRD is CC BY-SA but is a text-to-SQL task (E06).

**Metric.** `table_recall` mirrors `metrics/path_recall.py` — recall@k / hit@k / MRR over a ranked list of table-ids, plus dataset-**agnostic** query-property gates (numeric / operation / multi-hop slices) so slice-conditioned bars never become an `if dataset ==` hack.

---

## 3. Prior-art / design-around — the do-not-read-on set

The patentability mandate treats the following as prior art to design **around**, never to build **on** or "improve." Reusing any of these mechanisms is either barred (named prior art) or **doubly barred** (also an ASP graph-shape / HippoRAG primitive, so obvious for tables).

| Prior-art method | Inventive step it claims | What E15 must NOT reuse |
|---|---|---|
| **SAGE** | Structure-aware seed → first-hop-expand → hybrid-filter over an offline chunk-similarity graph | The whole seed-and-expand loop — **doubly barred** (identical to ASP GRACE/PPR) |
| **HELIOS** | Bipartite table-row/passage "star" subgraph as retrieval unit; early+late fusion; star-level LLM refinement | Any table-passage bipartite-graph retriever reads directly on this |
| **THYME** | Per-field matching granularity (cell=word, title=passage) hybrid | "Embed each table field separately at its own granularity" |
| **CRAFT** | LLM-generated table-summary-as-surrogate; sparse→dense cascade | Both the summary trick and the cascade |
| **T-RAG** | Hierarchical intra/inter-table memory index + multi-stage filter-rank + graph-aware prompting | The hierarchical-index + multi-stage combination |
| **TableRAG** (Google) | Dual schema-index + cell-index dense retrieval; **frequency-as-relevance** cell budgeting | The schema/cell split AND frequency cell budgeting |
| **DTR / TAPAS / CGPT / entity-enhanced** | Dense structure-aware table encoding; entity-type highlighting; cluster-guided contrastive fine-tuning | Dense-cosine table embedding as the SEED — **doubly barred** (ASP graph primitive) |
| **Text-to-SQL recipe** (CHASE/CHESS/XiYan/DIN/DAIL/MAC/RSL/SQL-of-Thought/TCSR) | schema-linking → candidate SQL → self-consistency / execute-select | The entire recipe and every named add-on — **this is E06, not E15** |
| **ASP graph / HippoRAG primitives** | PPR, BM25 seeding, dense-cosine seeding, beam-over-paths, NEVA neighbor-evidence reranking | All of them for tables = obvious. Also barred as documented-falsified: MACD power-mean conjunction, typed_ppr, bidir_ppr |

**Rejected-as-obvious combinations** (documented so inventors do not chase them): "field-aware encoder + graph expansion" = THYME+SAGE bolted together (the survey's own headline suggestion); "generate K SQL, execute, vote" = self-consistency + barred recipe; "LLM table-summary surrogate" = CRAFT verbatim; "entity-type signals in encoder" = entity-enhanced verbatim; "column-gated cell retrieval + per-column value index" = TableRAG cell index + TCSR value index + standard schema-linking (only defensible if the joint conditioning is provably distinct from a cascade).

---

## 4. The ASP opportunity — surviving NOVEL mechanisms

Five candidate mechanisms survive the design-around screen. Each is a **ranking operator** (never SQL generation), $0/query (no LLM — spaCy + regex + cached embeddings + scipy only), returns a `ContextBundle` of structured table units with `SourceAuthority(source_type='table')`, and carries an **explicit distinction over its closest prior art plus its own honesty flag**. Recommended build order: **SIEVE first** (cleanest novelty seam, no noisy FD mining), **SPECTRA second** (cheapest — no offline mining beyond cached embeddings).

### 4a. SIEVE — predicate-satisfiability mass with inverse-corpus rarity + unit invariance `[PRIMARY PATENT BID]`

- **Mechanism.** Offline ($0): per column, cache a value-distribution signature — inferred datatype, cardinality, equi-depth quantile histogram / t-digest over numeric+temporal values, and a unit/dimension fingerprint (USD, %, kg, year) from headers + value formatting; also cache a corpus-wide distribution of predicate-satisfaction mass per canonical dimension. Online ($0, spaCy+regex): parse the query into typed predicates P=(attribute-concept, comparator, literal); for each predicate pick the best column by header-embedding cosine; compute `m_j` = the fraction of that column's empirical CDF satisfying the predicate with literal and column canonicalized to a shared dimension. Score the column NOT by raw `m_j` but by (i) a **discriminativeness / information-gain functional** peaked at intermediate, non-vacuous mass (down-weighting m→0 "nothing qualifies" and m→1 "predicate is vacuous") AND (ii) an **inverse-corpus-satisfiability weight** `w_j = −log(fraction of corpus columns that also satisfy P)` — an IDF over value-ranges. Table score = additive sum over predicates (additive, NOT the falsified MACD conjunction). Non-numeric queries abstain to the dense floor (logged). Fuse with dense floor by RRF.
- **Point of novelty.** (a) Inverse-corpus-satisfiability rarity weighting — relevance = "this column satisfies the constraint AND few corpus columns do," a corpus-relative operator absent from query-optimizer selectivity and from D3L/LSH-Ensemble/Aurum joinability; (b) a discriminativeness functional provably non-monotone in raw selectivity (a planner computes raw selectivity, never discriminativeness), elevated to a unit-dimension-canonicalized predicate-satisfiability **retrieval invariant**. The inventive step is the NL-query → typed-predicate → unknown-schema open-domain web-table ranking transfer.
- **Closest prior art.** Histogram-based selectivity estimation (Selinger 1979; t-digest); zone-maps / Parquet row-group pruning; D3L (Bogatu 2020), LSH-Ensemble (Zhu 2016), Aurum (Fernandez 2018) column signatures; TableRAG frequency-sampled cells. SIEVE reuses no cited table retriever.
- **Why it could beat SOTA.** Dense embedders map "revenue over $1M" and any revenue table to nearby vectors regardless of whether values exceed $1M — magnitude is not linearly recoverable from a text embedding, so topical distractors are cosine-indistinguishable from the answer. SIEVE's rarity-weighted satisfiability term is an axis orthogonal to text similarity that breaks exactly that tie (MultiHiertt shows ~45 pp headroom on numeric tables).
- **Falsification.** Pre-registered kill: on the numeric/comparative/temporal slice (dataset-agnostic classifier) of NQ-Tables + OTT-QA, `sieve_dense_rrf` recall@10 does not beat the bge-small dense floor by ≥+5 pp at n=50. Coverage kill: <15% of queries yield a parseable predicate on BOTH corpora → out of scope, drop. Invariant kill: unit-canonicalization mislabels dimensions on >20% of a held-out slice → shelve. Novelty kill: if the discriminativeness functional collapses to a monotone transform of raw selectivity under audit → reads on the DB primitive, shelve.

### 4b. SPECTRA — spectral effective-rank of the query-cell alignment matrix

- **Mechanism.** Offline ($0): cache bge-small embeddings of cells+headers with logged truncation caps. Online ($0): decompose the query into M term embeddings; cheap ANN prefilter (NOT the score) selects candidates; for each candidate form the cross-alignment matrix A∈R^{M×N}, A_ij = cos(q_i, cell_j); compute top-r singular values via `scipy.sparse.linalg.svds` (r∈{2,3,4}); score = normalized Ky Fan r-norm `E_r(A) = (1/√min(M,N)) Σ_{k≤r} σ_k(A)`. Ship an r=1 arm and a ColBERT-max-sim ablation arm as honest comparators.
- **Point of novelty.** RANK-STRUCTURE aggregation of the alignment spectrum rather than ColBERT's per-row MAGNITUDE aggregation. Max-sim sums the per-term maximum (a greedy pool a single dominant cell can saturate); E_r scores the effective rank of the coverage — energy spreads across σ_1..σ_r only when M distinct query facets are explained by M different cells. **Screen correction: permutation invariance is DROPPED as a novelty pillar** (max-sim and mean pooling are already permutation-invariant); the sole defensible novelty is the spectral effective-rank operator.
- **Closest prior art.** ColBERT late interaction (SPECTRA swaps only the pooling operator); dense-cosine table embedding. **External untested risk:** LSA, nuclear-norm / singular-value / subspace-alignment matching in IR.
- **Why it could beat SOTA.** Multi-hop / multi-constraint queries (OTT-QA) need a table jointly covering several distinct facets across different cells. Pooled cosine dilutes multi-facet signal; max-sim can be maxed by one cell — a false positive with rank-1 alignment. E_r with r>1 scores high only under high-rank alignment, the structural signature of a jointly-answering table.
- **Falsification.** Pre-registered kill: on OTT-QA multi-hop, SPECTRA (r∈{2,3,4}) does not beat BOTH the single-vector cosine floor AND the ColBERT-max-sim ablation by ≥+3 pp recall@20 at n=50. Signal kill: if median effective rank of A on gold tables ≈1 (single-facet queries), drop. Novelty flag: no novelty claim until E11-P2 scans singular-value/nuclear-norm/LSA IR matching.

### 4c. TRELLIS — query-operation-to-FD-lattice homomorphism `[HIGH prior-art risk]`

- **Mechanism.** Offline ($0): per table mine approximate functional dependencies (TANE/PYRO stripped-partition sampling, columns capped, larger tables sampled with logged truncation), store the determination closure + candidate keys; embed column names. Online ($0): parse the query into a typed operation DAG — (group-key concept) −[determines]→ (measure concept) with aggregation/superlative annotation, via a dataset-agnostic operation-cue lexicon on a spaCy dependency parse; align concepts to columns by header cosine. Score = maximum partial homomorphism from the query operation DAG into the table's FD closure, normalized by DAG size. Fuse with dense floor by RRF; include an isolation ablation (trellis alone vs dense vs RRF).
- **Point of novelty.** The operator is a QUERY-OPERATION-to-SCHEMA homomorphism (parsed algebraic DAG embedded into inferred FD closure), NOT schema-to-schema matching (Similarity Flooding/COMA) and NOT FD mining itself (that is only the offline representation). Invariant: answerability-by-algebra — a table can outrank the floor for an aggregation/superlative query only if it admits the grouping attribute as a determinant of the measure.
- **Closest prior art.** TableRAG schema index; CRAFT LLM schema summaries. **External risk (higher):** FD-discovery (TANE/PYRO/HyFD); data-lake discovery (Table Union Search, Josie, D3L, Aurum, Starmie, DeepJoin); graph schema matching (Similarity Flooding, COMA).
- **Why it could beat SOTA.** For group-by/superlative queries the answer table must structurally support the operation. Text similarity places a topically-close but operationally-incapable table (a flat list lacking the grouping key) above the true one; TRELLIS rewards structural capability.
- **Falsification.** Pre-registered kill: on the aggregation/superlative slice of OTT-QA + MultiTableQA, `trellis_dense_rrf` recall@10 does not exceed the floor by ≥+2 pp at n=50 AND the isolation ablation shows RRF-dense alone matches it. Feasibility kill: approximate-FD mining precision <60% on a labeled slice → shelve. **Prior-art flag: highest-risk FD family — no novelty language until E11-P2 clears "query-operation-to-FD-closure homomorphism."**

### 4d. FUGUE — differential determination-contrast operator `[HIGH prior-art risk; follow-on to TRELLIS]`

- **Mechanism.** Offline ($0): mine approximate FDs with edge confidences `g(X→Y)` = fraction of rows where X determines Y, plus candidate keys. Online ($0): map the query to an operation shape (key,target) for lookups or (group-by, aggregated); align to columns by name cosine. Score = a **differential contrast**: high only if the query-implied key→target determination is high-confidence (max-product widest-path over the FD lattice) AND the target is NOT better-determined by a competing in-table column — `score = g(key→target) − max_c g(c→target)` — so a table where "population" is keyed by a different entity than the query's "city" is demoted. Additively fuse with the floor; abstain when no operation shape parses.
- **Point of novelty.** A CONTRAST/differential determination operator that detects and demotes the "right words, wrong entity / denormalized non-key" false-positive class — ranking on whether the query's key is the BEST determinant relative to competing columns. Bare "does key→target hold" is trivially ~1.0 for any candidate key, so the novelty and the causal gain both live in the multi-entity competitive contrast, not in FD mining or a determinacy check.
- **Closest prior art.** T-RAG memory index; TableRAG schema index; SQL schema-linking (CHESS/RSL-SQL). **External:** TANE approximate-FD + g3 partition-error; Aurum PK-FK graph; SANTOS column-pair relationships; determinacy theory (Nash-Segoufin-Vianu).
- **Why it could beat SOTA.** On lookup/aggregation queries, similarity and schema-linking surface many tables mentioning both attributes where the relation is NOT a determination (attribute non-key or a different entity in a denormalized fragment). The differential contrast demotes those near-miss false positives.
- **Falsification.** Pre-registered kill: FUGUE additively fused with the floor does not beat the floor by ≥+4 pp recall@10 on NQ-Tables lookup/aggregation queries at n=50. No-signal kill: median `g(key→target) − best competitor` on gold tables <0.2 → collapses to the floor, drop. **Prior-art flag: high-risk FD family — gate any novelty claim on E11-P2.**

### 4e. BRAID — query-conditioned, attribute-terminated join-closure objective `[HIGH prior-art risk; blocked on MultiTableQA license]`

- **Mechanism.** Offline ($0): MinHash-LSH (datasketch) over normalized column value-sets to detect candidate join keys; build a value-overlap column graph (edges where Jaccard ≥ θ); store header embeddings. Online ($0): seed ENTITY columns by cell match to query entity mentions and TERMINATE on the query's TARGET-ATTRIBUTE column by header-embedding semantic match (not a value-overlap edge); BFS the value-overlap graph from entity columns toward the attribute-terminated target; rank competing closure paths by join-selectivity / value-overlap strength; return the MINIMAL join-connected table SET with the join path in `SourceAuthority` provenance; log a missing link as a `DroppedItem`. Scored by recall@k over the returned table set.
- **Point of novelty (narrowed per screen).** A query-conditioned, dual-signal closure OBJECTIVE whose terminator is a header-embedding SEMANTIC match to the query's target attribute (not a value-overlap edge), fused with value-overlap reachability from entity-seeded columns, where the scored unit is a minimal join-connected table SET evaluated by recall@k, and competing paths are ranked by join-selectivity — an answer-tuple-assembly score, not mere path existence. **Honest concessions:** the MinHash-LSH value-overlap graph reads on Aurum's EKG (conceded, cited, NOT claimed novel); "monotone certification under corpus growth" is a trivial reachability property (implementation note, NOT a novelty claim).
- **Closest prior art.** Aurum (Fernandez, ICDE 2018) column EKG with MinHash-LSH + PK/FK edges; JOSIE (SIGMOD 2019), LSH-Ensemble (VLDB 2016) joinable-table search; SAGE/HELIOS/FT-RAG neighbor-expansion retrievers (which rank by similarity after expansion, not by join-sufficiency).
- **Why it could beat SOTA.** Multi-hop table QA fails when the bridge table shares NO query terms with the query — only a join key with the answer table — so similarity-ranked retrievers (including SAGE/HELIOS) miss it. BRAID retrieves the bridge because it is join-reachable AND terminates on a semantic attribute match.
- **Falsification.** Pre-registered kill: on OTT-QA multi-hop / MultiTableQA, BRAID table-recall@k does not beat a dense-cosine + first-hop-expand baseline by ≥+3 pp on the multi-hop subset at n=50, OR value-overlap edges connect <50% of gold multi-hop chains. **Honesty flag: benchmark same-condition against a JOSIE-style value-overlap baseline, not only the dense floor; gate all novelty language on E11-P2.**

### Patentability caveat (mandatory framing)

The primary patent bid is **SIEVE**, designed-for-novelty rather than certified. We **cannot** claim any of these five are patentable: three (TRELLIS, FUGUE, BRAID) sit adjacent to well-established **uncited** literature (TANE FD-discovery, Aurum/JOSIE/SANTOS data-lake discovery, Similarity Flooding schema matching, determinacy theory), and SPECTRA must clear LSA/nuclear-norm IR prior art. Formal patentability requires the **E11-P2 prior-art scan (`docs/patent/prior_art_scan.md`) plus attorney review of novelty AND non-obviousness** against those specific bodies. The correct language throughout E15 is *"designed for novelty with an explicit distinction over <named prior art>, pending formal novelty scan."* **Timing hard-constraint (E11):** a provisional must be filed BEFORE any public disclosure (paper, preprint, public repo) — pre-filing disclosure permanently destroys absolute-novelty rights outside the US.

---

## 5. How this mirrors the graph shape

E15 is deliberately the structural sibling of E14, so it reads as the same playbook on a new shape:

| Graph shape (E14, proven) | Table shape (E15, this brief) |
|---|---|
| Generation-free primary metric `answer_node_recall` over node-ids (`metrics/path_recall.py`), $0/query | `table_recall` recall@k/hit@k/MRR over table-ids (`metrics/table_recall.py`), $0/query |
| `GraphRAGPipeline(arm=...)` → `GraphRetrievalResult` dataclass, arm dispatch | `SemanticTablePipeline(arm=...)` → `TableRetrievalResult` dataclass, arm dispatch |
| `graph_retrieval_ablation.py` (ARMS tuple, `run_<dataset>`, `--dataset/--limit/--top-k/--arms/--checkpoint-dir`) | `table_retrieval_ablation.py` mirroring the CLI exactly |
| Same-condition vs published SOTA (MFAR* 68.3%) AND flat floor; honest negatives recorded | Same-condition vs THYME/CRAFT/HELIOS (reproduced in E15-S8) AND `flat_dense` bge-small floor |
| STaRK-PRIME (large, shipped graph) + MuSiQue (multi-hop) | NQ-Tables (large corpus) + OTT-QA (multi-hop) [+ MultiTableQA pending license] |
| N-progression: n≤50 until the 85% gate clears on both, then scale | n≤50 per slice until the pre-registered per-slice pp bar clears, then n=500; no test-split tuning |
| ContextBundle with `SourceAuthority` + `dropped_items` (no silent truncation) | Same contract, `source_type='table'`, matched predicate/FD/join-path as provenance |

The one place the analogy breaks — and the reason E15 is not just "GRACE for tables" — is that **reusing the graph shape's own seed→expand→PPR mechanism for tables is explicitly barred** (doubly obvious: SAGE prior art AND ASP graph-shape prior art). The table shape must invent ranking operators over *structure text embeddings cannot see* (value distributions, FD lattices, alignment rank, join closure), which is exactly what §4 does.

---

## 6. Framing, dataset, and SOTA-baseline recommendations

**Framing (Athena recommends): open-domain table RETRIEVAL measured by recall@k, as the sole primary framing.** Generation-free, $0/query, honors the ContextBundle contract, structurally separates E15 from E06, and keeps every point of novelty in the ranking operator. Do NOT run text-to-SQL execution accuracy as a primary arm — it is closed-frontier-dominated and collapses E15 into the barred recipe. A text-to-SQL downstream-lift arm is a P3 stretch only, to show a promoted retriever improves E06 — not a framing.

**SOTA-baseline table (no absolute number asserted on purpose — E15-S8 locks them first):**

| Dataset | Metric | Published SOTA | Bar | Flat / floor comparator |
|---|---|---|---|---|
| NQ-Tables (PRIMARY) | recall@k | THYME / CRAFT | absolute recall@10/@20 LOCKED in E15-S8 (abstracts are relative-only) | `flat_dense` bge-small cosine over linearized tables |
| OTT-QA (SECONDARY) | recall@k / nDCG | HELIOS (relative +42.6% recall) | absolute recall@20 pulled from paper tables in E15-S8; SAGE +5.7 as reference | `flat_dense` floor + ColBERT-max-sim ablation arm |
| MultiTableQA (OPTIONAL) | recall | T-RAG (self-authored) | reproduce same-condition before any claim | `flat_dense` floor |

**Story outline (E15, mirroring E14's story format — full detail belongs in `product-backlog/semantic-table/epic-15-table-benchmark.md`):**

- **E15-S1** `table_recall` metric module (recall@k/hit@k/MRR over table-ids + query-property gates) — P0
- **E15-S2** NQ-Tables loader (`datasets/nq_tables.py`, mirror `stark.py`) — P0
- **E15-S3** OTT-QA loader (`datasets/ott_qa.py`) — P1
- **E15-S4** MultiTableQA loader — **P2, BLOCKED on LICENSE verification**
- **E15-S5** `SemanticTablePipeline` + `TableRetrievalResult` (generation-free, mirror `GraphRAGPipeline`) — P0
- **E15-S6** `table_retrieval_ablation.py` harness (ARMS, `run_<dataset>`, CLI flags) — P0
- **E15-S7** `flat_dense` floor + dataset-agnostic query-property classifier + ColBERT-max-sim ablation — P0
- **E15-S8** SOTA reproduction: lock absolute recall@k for THYME/CRAFT/HELIOS → `docs/research/table_sota_repro_report.md` — **P0 (gates all "beat SOTA" language)**
- **E15-S9** Experiment: SIEVE (+5 pp numeric-slice bar) — P1
- **E15-S10** Experiment: SPECTRA (+3 pp OTT-QA multi-hop bar) — P1
- **E15-S11** Experiment: TRELLIS (+2 pp aggregation-slice bar) — P1
- **E15-S12** Experiment: FUGUE (+4 pp lookup-slice bar) — P2, follow-on to S11
- **E15-S13** Experiment: BRAID (+3 pp multi-hop bar) — P2, depends on S4
- **E15-S14** Invention disclosure + prior-art/novelty scan → `docs/patent/prior_art_scan.md`; satisfy E11-P2 (file provisional BEFORE public disclosure) — P1

**Open decisions for Kushal:**

1. **Framing fork** — confirm recall@k as the sole primary framing (recommended); keep SQL in E06.
2. **Dataset** — NQ-Tables + OTT-QA cleared; **verify the T-RAG repo LICENSE before committing E15-S4** (BRAID/S13 is blocked until this resolves).
3. **Build-first** — Athena recommends SIEVE (S9); SPECTRA (S10) is the cheapest second bet.
4. **FD-family redundancy** — TRELLIS and FUGUE share the FD-lattice representation and are both borderline / high prior-art risk. Build ONE first (recommend TRELLIS); treat FUGUE as a follow-on only if TRELLIS clears its FD-mining feasibility kill. Do not staff both in parallel.
5. **Patent timing (E11 hard constraint)** — decide whether E15-S14 gates paper/repo publication, and engage an attorney for the E11-P2 scan of the uncited DB / data-discovery prior art (Selinger/t-digest, D3L, Aurum, JOSIE, LSH-Ensemble, TANE, SANTOS, Similarity Flooding, LSA/nuclear-norm matching).
6. **N-progression** — confirm the E14 rule carries: n≤50 per slice until the per-slice pp bar clears on the primary dataset, then n=500. No test-split tuning; all knobs (K bins, r, θ, confidence floors, fusion weights) tuned on a held-out validation slice only.

---

*Seeds `product-backlog/semantic-table/epic-15-table-benchmark.md` (to be created alongside `knowledge-graph/epic-14-graph-benchmark.md`); complements — does NOT duplicate — the E06 text-to-SQL demo (`product-backlog/epic-06-semantic-table.md`). Patent workstream: E11-P2. Structural exemplars: [graph_rag_research_brief.md](graph_rag_research_brief.md), [graph_realdata_v1_report.md](graph_realdata_v1_report.md).*
