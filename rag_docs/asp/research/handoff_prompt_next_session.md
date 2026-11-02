# ASP Retrieval Shapes — Full Handoff Prompt

**For:** Next development session
**Repo:** `/home/C5415005/Documents/repos/ASP` (was `c:\Users\...` — moved to Linux)
**Branches:** `experiment/graph-rag` (graph shape — done), `experiment/doc-tree-retrieval-redesign` (doc-tree — done), `experiment/table-rag` (table shape — NEW, active)
**Author:** Kushal Sharma
**Last updated:** 2026-08-11

> **SCOPE NOTE (2026-08-07):** This file began as the document-tree handoff. It now carries the
> final state of **two proven shapes** — document-tree AND knowledge-graph — plus the standing
> rules that apply to the next shape (semantic-table, `experiment/table-rag`). The document-tree
> results start at "Current results — headline table"; the graph-RAG results are in the two
> banners immediately below.

---

## ✅ SHAPE STATUS AT A GLANCE (2026-08-11)

| Shape | Branch | Primary datasets | Result | vs SOTA / flat | Status |
|---|---|---|---|---|---|
| **Document-tree** | `experiment/doc-tree-retrieval-redesign` | CUAD, ObliQA, GovReport-QS | CUAD ~87–90% EM · ObliQA 76.1% recall · GovReport-QS 89.6% token-recall | +14–19 / +38 / +55 pp vs flat | **Done — publishable** |
| **Knowledge-graph** | `experiment/graph-rag` | STaRK-PRIME, MuSiQue-Ans | **STaRK 85.1% recall@20 · MuSiQue 90.0% recall@10** | **+16.8 pp over SOTA MFAR\* (68.3%)** | **Done — beats SOTA, both 85% gates cleared** |
| **Semantic-table** | `experiment/table-rag` | FeTaQA, OTT-QA-Small, OpenWikiTables, NQ-Tables | **asptable: OTT-QA 98.0% · FeTaQA 94.0% · OpenWiki 100.0% · NQ-Tables 81.5%** | +1–6.5pp vs flat-dense floor | **Research done — asptable promoted. NQ-Tables 81.5% hits bge-small ceiling (GPU needed for 90%+). Demo epic E6 partially done.** |
| **Unstructured data RAG** | `experiment/unstructured-rag` | ArguAna, NFCorpus, FiQA (primary) | ArguAna 88 · NFCorpus 32.94 R@100 · **FiQA 48.13 — LOSES −0.57 to flat at k=10** | vs flat-dense floor | **Research done — all invented methods (COVERT/APEX/SPECTRA) FALSIFIED. No novel champion; flat_dense is the honest floor. Shape decision pending (see banner).** |

---

## 🧬 KNOWLEDGE-GRAPH SHAPE — FINAL RESULTS (2026-08-06, branch `experiment/graph-rag`)

> **ASP BEATS SOTA ON BOTH GRAPH DATASETS — same parameters, no crutches, no test-split tuning.**
>
> | Dataset | Metric | ASP (`bison_grace_tars`) | Published SOTA | Flat / floor | ASP vs SOTA |
> |---|---|---|---|---|---|
> | **STaRK-PRIME** | recall@20 | **85.1%** | MFAR\* 68.3% | enriched cosine@20 = 49.1% | **+16.8 pp** |
> | **MuSiQue-Ans** | recall@10 | **90.0%** | (flat cosine@10 = 88.5%) | cosine@10 = 88.5% | +1.5 pp (graph adds little on MuSiQue) |
>
> **Both ≥85% gates CLEARED** → runs may now go to n=70 (per standing rule 1). 90% gate: MuSiQue
> cleared (90.0%); STaRK at 85.1% needs +4.9 pp and has hit the **embedding-retrieval ceiling
> (~85–86%)** — the 5 remaining STaRK zeros all require an LLM or structured chem data (gold at
> cosine ranks 2K–15K; no embedding-only heuristic separates them). See ceiling analysis below.
>
> **REPRODUCE BEST RESULT (STaRK 85.1%, MuSiQue 90.0%):**
> ```bash
> ASP_EMBED_MODEL=pritamdeka/S-PubMedBert-MS-MARCO \
>   python -m asp.benchmarks.graph_retrieval_ablation \
>   --dataset stark --limit 50 --top-k 20 --enrich-v3 \
>   --arms bison_grace_tars --checkpoint-dir /tmp/ckpt
> # MuSiQue: --dataset musique --top-k 10 (same arm, same params)
> ```
> Cache: `~/.cache/asp_graph_embeddings/stark_prime_enriched_v3_pritamdeka_s_pubmedbert_ms_marco.npz`
>
> **THE WINNING STACK (`bison_grace_tars`) — 7 composed, orthogonal mechanisms:**
> 1. **Small-graph cosine shortcut** (E14-S34): graphs ≤ top_k×2 nodes → return all by cosine.
>    Fixed MuSiQue 20-node passage graphs where gold paragraphs are unreachable by beam search (+7 pp → 90%).
> 2. **Cross-type CAPER** (E14-S29/S33): gene→drug/disease entity-hop seeds (≤100 cross-type neighbors).
> 3. **GRACE** (E14-S31): exhaustive 1-hop expansion from top-10 seeds with type-filtered bigram BM25 boost.
> 4. **TARS** (E14-S30): surgical unigram BM25 boost on the ASTAR candidate pool.
> 5. **CAPER** drug/disease entity-hop with alias fix (type-suffix stripping) + adaptive tied-NEVA slots.
> 6. **ASTAR beam** (E14-S27): A\* beam search over relational paths (beam 200, 3 hops, top-100) + NEVA rerank.
> 7. First-bracket answer-type extraction + answer-type position-boost (1.2× for keywords in chars 5–45).
>
> **THE ARC (STaRK recall@20, n=50, cumulative — every step honest, same-condition):**
> `sym_ppr 40.0% → RCAD 42.6% → PAPR 44.3% → papr_neva 50.5% → swan_papr_neva 54.1% →
> enriched swan_papr_neva 64.2% → S-PubMedBert 65.6% → GRAPE 70.2% (first to beat SOTA) →
> ASTAR 76.4% → CAPER 78.4% → TARS 80.2% → GRACE 82.0% → cross-type CAPER 84.4% →
> small-graph shortcut 85.1%`
>
> **HONEST NEGATIVES (falsified, first-class artifacts):** MACD, MFDS (−3.2 pp), GNN1 (−7.1 pp),
> IQRF (−12.6 pp), Enriched-v2 (−4.0 pp), CTGNN (−4.1 pp), Enriched-v3 standalone (−0.4 pp),
> BioBERT (−24.4 pp — classification model, not retrieval-tuned). All in E14 with root-cause notes.
>
> **STaRK CEILING ANALYSIS (5 remaining zeros, all need LLM or structured data):**
> - Q53 (rank 2251): lay-vs-medical vocabulary ("blisters" ≠ "toxic epidermal necrolysis")
> - Q87 (rank 225): molecular-weight constraint (MW=276 not in any graph text)
> - Q196 / Q211 / Q213 (ranks 6763 / 6138 / 15497): too deep in embedding space
> - VERITAS (LLM reranking) tried → regressions via unreliable cosine-threshold trigger. Not promoted.
>
> **PATH TO 90% STaRK (deferred — needs infra):** (1) fine-tune the embedding model on STaRK queries;
> (2) reliable LLM reasoning over graph structure; (3) file-backend VERITAS (offline LLM export/import);
> (4) STaRK-MAG third-dataset generality run (E14-S36, loader built, blocked on GPU to embed 1.87M nodes).
>
> **KEY MuSiQue FINDING:** passage-entity graphs add almost no value over flat cosine (90.0% vs 88.5%)
> — gold paragraphs are already top cosine hits. The graph shape's real win is on graph-native
> biomedical retrieval (STaRK), not multi-hop-passage QA.
>
> **Epic:** `product-backlog/knowledge-graph/epic-14-graph-benchmark.md` (E14-S1..S36, full story detail).
> **Memory:** `graph_rag_experiment_state.md`.

---

## 📋 SEMANTIC-TABLE SHAPE — FINAL RESULTS (2026-08-08, branch `experiment/table-rag`)

> **asptable BEATS FLAT-DENSE FLOOR ON ALL 4 DATASETS — same parameters, no crutches. 3/4 clear ≥90%.**
>
> | Dataset | Corpus | Floor R@10 | **asptable R@10** | Δ R@10 | Floor R@1 | **asptable R@1** | ≥90%? |
> |---|---|---|---|---|---|---|---|
> | **OpenWikiTables** | 54K | 99.0% | **100.0%** | +1.0pp | 71.5% | **82.0%** | ✅ |
> | **OTT-QA-Small** | 8.9K | 96.5% | **98.0%** | +1.5pp | 77.0% | **82.5%** | ✅ |
> | **FeTaQA** | 10.3K | 90.5% | **94.0%** | +3.5pp | 75.5% | **79.0%** | ✅ |
> | **NQ-Tables** | 169K | 75.0% | **81.5%** | +6.5pp | 31.5% | 33.5% | ⚠️ embedding ceiling |
>
> **All n=200, embedding = bge-small-en-v1.5 (CPU), generation-free ($0/query, like the graph shape).**
>
> **THE METHOD — `asptable`** (`src/asp/retrieval/semantic_table/arms.py`):
> A rank-safe additive reranker over the dense top-200 pool. Two gap-damped (`boost·exp(-gap/β)`,
> β=0.25) monotone promotions that NEVER demote the dense leader:
> 1. **Title-anchor** — IDF-weighted match of rare/capitalized query tokens against the table **title
>    field**. Wins the entity-confusion class (gold table's title IS the query entity, but dense
>    retrieves a same-topic sibling).
> 2. **Field-grounding** — IDF query tokens grounded across title/header/cell (weights 1.0/0.6/0.8).
>    Wins the list-vs-prose class (gold is a data-table page, dense prefers the entity's prose page).
> 3. **Superlative gate** — lexical boost switched off for superlative/comparative queries
>    (dataset-agnostic classifier, NOT a dataset switch); +1.5pp on NQ-Tables.
>
> **KEY DESIGN LESSON:** rank-safe monotone promotion is the load-bearing invariant. Every arm that
> reordered the dense list symmetrically (ACG via RRF) *regressed*; only never-demote promotion won.
>
> **REPRODUCE (n=200, all 4 datasets):**
> ```bash
> python -m asp.benchmarks.table_retrieval_ablation \
>   --dataset fetaqa --limit 200 --top-k 10 --arms flat_dense asptable \
>   --checkpoint-dir /tmp/ckpt_feta
> # change --dataset to: ott_qa | openwiki | nq_tables
> # Embedding caches: ~/.cache/asp_table_embeddings/ (warm after first run; NQ-Tables ~3h to build)
> ```
>
> **HONEST NEGATIVES (falsified, all documented in E15):**
> SIEVE (coverage kill: 0–2/50 queries numeric-constraint on these datasets), SPECTRA (18%,
> cell-unit embedding noise), ACG (−18pp, symmetric RRF), row-max-sim reranking (78%, worse
> than lexical), bge-large embedding (right direction but ~40h on CPU → GPU-blocked).
>
> **NQ-TABLES CEILING (honest):** 81.5% (floor 75.0%). Gold IS in the dense pool@200 for 98.5%
> of queries — bottleneck is ranking within pool. bge-small can't resolve semantic-paraphrase
> residual ("biggest town" → "cities" list). Fix = stronger embedder (bge-large ~40h on CPU,
> GPU-blocked, exactly like STaRK-MAG in E14). 90%+ on NQ-Tables requires GPU infra.
>
> **ANSWER-EM (FeTaQA, n=60, Nemotron-30B slot 1):**
> EM=0.000 (expected — free-form generation, same as Travel Policy/JCP Credit; EM is the wrong
> metric), token-F1=0.414, containment=0.416, hit@1=0.750. Per-query checkpoint:
> `data/benchmarks/table_fetaqa_answer_em.jsonl`.
>
> **DATASETS** (all IBM-Research retrieval suite, CC-BY-4.0, structured headers+cells):
> `ibm-research/{OTTQASmallRetrieval, FeTaQARetrieval, NQTablesRetrieval, OpenWikiTablesRetrieval}`
> — loaded via huggingface_hub (the canonical HF script-based repos return 401; IBM parquet/jsonl
> mirrors work). Embedding caches in `~/.cache/asp_table_embeddings/`.
>
> **PATENTABILITY:** `asptable` designed AROUND named prior art (SAGE/HELIOS/THYME/CRAFT/T-RAG/
> TableRAG/text-to-SQL agents/ASP graph primitives). **NOT certified patentable** — pending formal
> E11-P2 scan. All wins stated vs the flat-dense floor; no "beats SOTA" language (E15-S8 blocked:
> no web/arxiv access in-env, THYME/CRAFT absolute recall@k unverifiable).
>
> **OPEN ITEMS IN E15:**
> - **E15-S8** (SOTA reproduction): needs web access to lock THYME/CRAFT/HELIOS absolute recall@k.
> - **E15-S11/S12/S13** (TRELLIS/FUGUE/BRAID): not started; high prior-art risk; FD-family needs
>   the E11-P2 patentability scan before any patent language.
> - **E15-S14** (invention disclosure + E11-P2 prior-art scan): not started.
> - **NQ-Tables 90%**: needs bge-large (GPU). Fix is infrastructure, not method.
>
> **OPEN ITEMS IN E6** (demo semantic-table, separate from E15 research):
> - E6-S2 LangChain SQL Agent (P2), E6-S4 query-to-SQL intent classifier (P0),
>   E6-S7 MCP tool wrapper (P0), E6-S9 unit + integration tests (P1),
>   E6-S10 LangChain comparison baseline (P1) — all Not Started.
>
> **Epic:** `product-backlog/knowledge-graph/epic-15-table-benchmark.md`
> **Report:** `docs/research/table_tag_v1_report.md`
> **Research brief:** `docs/research/table_rag_research_brief.md`
> **Memory:** `table_rag_experiment_state.md`

---

## 🔤 UNSTRUCTURED-TEXT (VECTOR-RAG) SHAPE — RESEARCH DONE, NO NOVEL CHAMPION (2026-08-11, branch `experiment/unstructured-rag`)

> **The fourth and final ASP retrieval shape. All three invented methods (COVERT, APEX, SPECTRA)
> are FALSIFIED. flat_dense (commodity dense retrieval) is the honest floor for unstructured text.
> A shape-direction decision is pending — see NEXT ACTIONS.**
>
> **FINAL RESULTS (bge-base-en-v1.5, 768-dim, n=50, one fixed param set, same-condition):**
>
> | Dataset | Baseline | k | flat_dense | SPECTRA | Δ | Verdict |
> |---|---|---|---|---|---|---|
> | ArguAna | HIGH | 10 | 80.00 | 88.00 | +8.00 | ✅ but win = hub removal (KNOWN), not facets; stochastic 88/84/82 |
> | NFCorpus | HIGH | 100 | 31.33 | 32.94 | +1.61 | ✅ but win = hub removal + bigger pool |
> | **FiQA** | **LOW (PRIMARY)** | **10** | **48.70** | **48.13** | **−0.57** | ❌ **LOSES — falsifies champion** |
>
> **WHY FALSIFIED (settled 2026-08-11):** FiQA — the chartered LOW-baseline PRIMARY that gates the
> win-on-both bar — is now measured, and SPECTRA loses at its k=10 operating point on every metric
> (R@10 −0.57, nDCG@10 40.59→39.07, R@1 26.37→21.70, MRR −2.73). The E16-S11 falsification condition
> ("FiQA < floor +1.5pt ⇒ not promoted") fires outright. The loss is a statistical wash (McNemar
> p=1.0000, bootstrap 95% CI [−6.13, +3.83]). Term-attribution ablation across all 3 datasets: the
> facet-coverage term is a NET DRAG everywhere (−2.43 on FiQA), `hub_only` is the best single arm
> everywhere, and the two HIGH-baseline wins are carried by hub removal (≈ Mu & Viswanath
> all-but-the-top, a KNOWN technique) + a bigger rerank pool — NOT the novel LLM-facet/off-axis
> machinery. SPECTRA's k≥20 FiQA win (+6.0) is the "k as a per-dataset knob" crutch and does not
> count. SPECTRA reduces to "anisotropy-corrected fused reranking" — no novelty that clears prior art.
>
> **FALSIFICATION RECORD (all honest negatives, first-class):**
> - **COVERT v1/v2** — OT-plan functionals over frozen bge token clouds; boost ~100× too small (no
>   reorder), v2 anti-correlated on NFCorpus. The bge-anisotropy risk flagged at invention fired.
> - **APEX** — fixed-pool reranker; +0.86 NFCorpus, 0 ArguAna. Cannot recall a doc dense never surfaced.
> - **SPECTRA v1** (a+λ√E) — NFCorpus −0.74; unnormalized √E rewarded magnitude not coverage.
> - **SPECTRA v2 (fused, the champion bid)** — FALSIFIED as above.
>
> **VERIFIED:** adversarial audit (4 lenses) returned CLAIM_BROKEN on novelty + hidden-crutch;
> workflow w33cu4rhn (5 agents) confirmed all artifact numbers reproduce from disk and Nemesis
> returned charter_bar_met=false / FALSIFIED. Docs fully aligned (report §1/§2/§4b/§6/§8, epic
> S9/S11/S13/S14, ADR 0002 embedding-swap disclosure).
>
> **NEXT ACTIONS (shape has no champion — needs user steer):**
> 1. **Ship a minimal "anisotropy-corrected dense" arm** (hub removal = all-but-the-top, the one
>    component that repeatedly helps) and benchmark it honestly as a KNOWN-technique baseline — no
>    novelty claim. Gives the vector shape a working, defensible retriever.
> 2. **Return to invention** for a candidate-generation mechanism that beats flat at the PRIMARY
>    operating point (k=10) on a LOW-baseline dataset (FiQA) — the exact bar SPECTRA could not clear.
> 3. **Accept flat_dense as the honest floor** for unstructured text; locate ASP value in
>    routing/memory contracts, not a novel vector retriever.
>
> **EMBEDDING GPU NOTE:** all vector runs are bge-base on CPU (~4h one-time FiQA index build). Like
> the STaRK-MAG (E14) and NQ-Tables (E15) ceilings, a stronger embedder (bge-large / fine-tuned)
> could move the floor itself — infrastructure lever, not a method lever. See the hardware note below.
>
> **REPRODUCE (launch top_k = report k so SPECTRA's pool matches the metric):**
> ```bash
> export ASP_VECTOR_EMBED_MODEL="BAAI/bge-base-en-v1.5"
> python -m asp.benchmarks.vector_retrieval_ablation --dataset arguana  --limit 50 --top-k 10  --arms flat_dense spectra --model BAAI/bge-base-en-v1.5
> python -m asp.benchmarks.vector_retrieval_ablation --dataset nfcorpus --limit 50 --top-k 100 --arms flat_dense spectra --model BAAI/bge-base-en-v1.5
> python -m asp.benchmarks.vector_retrieval_ablation --dataset fiqa     --limit 50 --top-k 10  --arms flat_dense spectra --model BAAI/bge-base-en-v1.5
> ```
>
> **Epic:** `product-backlog/vector/epic-16-vector-benchmark.md` (E16-S1..S14).
> **Report:** `docs/research/vector_spectra_probe_report.md` (full falsification writeup).
> **ADR:** `docs/adr/0002-vector-shape-embedding-bge-base.md`.
> **Research brief:** `docs/research/unstructured_rag_research_brief.md`.
> **Memory:** `vector_rag_experiment_state.md`.

---

## 🖥️ HARDWARE NOTE — where a GPU (e.g. 2× Asus GX10 / GB10) would and would NOT help (2026-08-11)

> Every ASP shape has hit the SAME wall in its endgame: an **embedding-retrieval ceiling** that is an
> infrastructure limit, not a method limit. This is the single most GPU-answerable lever in the repo.
>
> **CEILINGS THAT ARE GPU-BLOCKED (embedding compute, not algorithm):**
> - **STaRK 85.1%→90%** (E14): fine-tune the embedding model on STaRK queries / embed STaRK-MAG 1.87M nodes.
> - **NQ-Tables 81.5%→90%** (E15): bge-large embedding is ~40h on CPU; gold IS in pool@200 for 98.5% of
>   queries, so the bottleneck is purely ranking resolution the embedder can't provide on CPU.
> - **Vector shape floor** (E16): bge-base on CPU is the floor; a fine-tuned / bge-large embedder could
>   lift flat_dense ITSELF on FiQA (the vocab-mismatch regime), which is exactly where SPECTRA's facets
>   tried and failed to add value.
>
> **What 2× GX10 (GB10 Grace-Blackwell, ~128GB unified LPDDR5X each, NVLink-C2C, ConnectX for a 2-node
> link) is GENUINELY good for here:**
> 1. **Embedding builds & fine-tuning** — the exact GPU-blocked items above. Embedding a corpus or
>    LoRA/full fine-tuning a bge-large / retrieval encoder on STaRK/FiQA queries is a batched-matmul,
>    high-memory-bandwidth job — the GB10's strength. A 2-node pair lets one node build indices while
>    the other fine-tunes, roughly halving the multi-day embedding backlog. **This is the highest-ROI use.**
> 2. **Self-hosting the 30B orchestrator (the Phase-1 cost-proof + token-consumption question)** —
>    128GB unified memory per node comfortably serves a 30B model in FP8/FP4 via vLLM; two nodes give
>    throughput headroom + failover. This directly attacks the "ASP consumes MORE tokens than production
>    RAG" concern (see below): the cost-proof is $/query, and a self-hosted small model makes ASP's
>    extra context tokens ~free vs frontier per-token API pricing. Two nodes ≈ enough to run BOTH model
>    families locally instead of the rate-limited (40/min/family) hosted slots.
> 3. **Fine-tuned SMALL readers/generators** — the internal prior (94% on a fine-tuned 3B vs 42% base)
>    is the template: a GX10 pair can fine-tune 3B–8B readers so a small local model matches frontier
>    accuracy on OUR datasets, which is the whole ASP cost thesis. Fine-tuning DOES boost numbers where
>    the ceiling is the model's task-fit (CUAD-style extraction, MAUD classification, FiQA vocab match),
>    NOT where the ceiling is gold-under-determination (ObliQA multi-target — no model fixes that).
>
> **What a GX10 pair will NOT fix (be honest — these are not compute problems):**
> - **ObliQA multi-target ceiling** — 1/3 of gold is not derivable from the question (EE6-X0). No
>   embedding, no fine-tune, no bigger model recovers gold the question doesn't point to.
> - **SPECTRA's novelty** — the facet machinery is a net drag; more GPU won't make an inert mechanism
>   inventive. GPU could lift the flat_dense floor (helping everyone), but that's the opposite of a
>   novelty win.
> - **MuSiQue graph advantage** — flat cosine already ≈ graph (88.5% vs 90%); nothing to accelerate.
>
> **HONEST BOTTOM LINE:** Yes — 2× GX10 would help ASP materially, but via **two specific levers**:
> (a) unblocking the embedding/fine-tune ceilings (STaRK-MAG, NQ-Tables bge-large, FiQA encoder) that
> are currently CPU-bound multi-day jobs, and (b) self-hosting the 30B orchestrator + fine-tuned small
> readers so ASP's higher token consumption becomes cheap local compute rather than metered API cost —
> which is precisely the Phase-1 cost-reduction claim. It will NOT manufacture accuracy where the
> ceiling is gold under-determination or a falsified mechanism. Fine-tuned small LLMs specifically DO
> boost numbers on task-fit-limited datasets (extraction/classification/vocab-match), and the GX10 is
> a well-matched box for that fine-tuning (unified 128GB memory, FP4/FP8 Blackwell). Recommend scoping
> a single concrete pilot first — fine-tune a retrieval encoder on FiQA + self-host 30B for a same-$
> cost-per-query comparison vs the frontier API — before committing to the full 2-node cluster.



---

## NEXT SHAPE: Unstructured Data RAG (superseded — now in progress above)

**Why it's next:** Document-tree ✅, Knowledge-graph ✅, Semantic-table ✅ (research done) — the
remaining ASP retrieval shape is **unstructured / vector RAG** on free-form text corpora that don't
have the hierarchical or relational structure the previous shapes exploit. This closes the four-shape
ASP architecture (E4 vector agent + a matching research benchmark epic) and provides the shape-
routing contrast needed for the orchestrator (E8) and the paper's comparative claim.

**Suggested branch:** `experiment/unstructured-rag`

---

## STANDING RULES (non-negotiable, set by Kushal)

1. **Do NOT test more than n=50 questions** until the method reaches **≥85% recall on BOTH STaRK AND MuSiQue**.
2. **Do NOT test more than n=70 questions** until the method reaches **≥90% recall on BOTH datasets**.
3. **ALWAYS use `--checkpoint-dir`** in every benchmark run — checkpoints after every question, resume-on-restart.
4. **No crutches** — any new method must work on both datasets with the same parameters. No dataset-specific knobs.
5. **No cheating** — do not tune parameters on test data. Methods must be principled and generalizable.

---

> **⚠️ SUPERSEDED (2026-08-06) — the numbers in this and the following graph-RAG banners are an
> earlier snapshot (70.2% GRAPE). Current best is 85.1% STaRK / 90.0% MuSiQue (`bison_grace_tars`)
> — see the "KNOWLEDGE-GRAPH SHAPE — FINAL RESULTS" banner at the top of this file. The banners
> below are kept only as the historical arc; do not cite them as current.**
>
> **2026-08-04 (historical) — grape_bison_papr_neva = 70.2% vs MFAR* 68.3% (+1.9 pp)**
> **Validated 3× on independent runs. MuSiQue: 88.5% (bge-small papr_neva unchanged)**
>
> **COMPLETE STARK ARM RANKING (S-PubMedBert + v3, n=50 FINAL):**
> | Arm | recall@20 | vs SOTA |
> |---|---|---|
> | **grape_bison_papr_neva** | **70.2%** | **+1.9 pp BEATS SOTA** |
> | grape_swan_papr_neva | 67.6% | −0.7 pp |
> | swan_papr_neva | 67.4% | −0.9 pp |
> | MFAR* (published SOTA) | 68.3% | 0 pp |
> | bison_swan_papr_neva (bge-small v1) | 64.2% | −4.1 pp |
> | grape_bison_papr_neva (bge-small v1) | 63.3% | −5.0 pp |
>
> **REPRODUCE BEST RESULT:**
> ```bash
> ASP_EMBED_MODEL=pritamdeka/S-PubMedBert-MS-MARCO \
>   python -m asp.benchmarks.graph_retrieval_ablation \
>   --dataset stark --limit 50 --top-k 20 --enrich-v3 \
>   --arms sym_ppr grape_bison_papr_neva
> ```
> Cache: `~/.cache/asp_graph_embeddings/stark_prime_enriched_v3_pritamdeka_s_pubmedbert_ms_marco.npz`
> Report: `docs/research/graph_grape_spubmed_v3_sota_report.md`
>
> **WHY IT WORKS:**
> 1. GRAPE 2-hop aggregation: h²(v) = normalize(h + AH/deg + 0.5·A²H/deg2)
>    S-PubMedBert makes same-type neighborhoods semantically rich (unlike bge-small)
> 2. v3 synthesizes "Indicated for: vaginitis" from drug→disease edges
>    structural-limit items (Sulfabenzamide, Candoxatril) now seedable
> 3. BISON+PAPR+NEVA pipeline adds +16.5 pp over sym_ppr baseline
> 4. Note: BISON adds +2.6 pp over grape_swan (GRAPE+BISON is better than GRAPE+SWAN alone)
>
> **FULL n=2801 VALIDATION IN PROGRESS:**
> PID 1177784, started 2026-08-04 12:34. Expected ~6 hours (ready by ~19:00)
> Check: `cat /tmp/grape_spubmed_v3_full_stark.log | tr '\r' '\n' | tail -20`
>
> **WHAT TO DO NEXT:**
> 1. Read full n=2801 result when ready (confirm ~70% holds on full test set)
> 2. If confirmed: Phase 4 — Terminus benchmark + paper writing
> 3. MuSiQue: S-PubMedBert hurts (84.2%); bge-small papr_neva (88.5%) is best




> **MuSiQue flat vector RAG (cosine@10) = 88.5% — essentially same as graph RAG!**
>
> **BISON (BM25-Integrated Spectral ONtological seeding):**
> `bison_score(v) = max(cosine_norm(v), bm25_norm(v))` + SWAN witness propagation.
> BM25 catches paraphrased entity names that cosine misses (+14 pp seed coverage on diagnostic).
> bison_swan_papr_neva = 64.2% (ties swan_papr_neva). bison_ppr standalone = 58.2%.
> Small graph early-exit (n≤anchor_m): skip BM25, return cosine top-k.
>
> **FLAT VECTOR RAG BASELINES:**
> - STaRK base cosine@20: 33.9% | enriched cosine@20: 49.1% | graph RAG advantage: +15.1 pp
> - MuSiQue cosine@10: 88.5% | graph RAG: 88.8% | graph advantage: only +0.3 pp
> MuSiQue finding: passage-entity graphs add almost no value over flat cosine — gold passages
> are already top cosine hits without graph traversal.
>
> **All methods (enriched text, n=50 STaRK FINAL):**
> | Arm | recall@20 | vs sym_ppr |
> |---|---|---|
> | enriched sym_ppr | 50.3% | baseline |
> | papr_neva | ~57% | — |
> | prase standalone | 58.0% | +7.7 pp |
> | bison_ppr | 58.2% | +7.9 pp |
> | swan_papr_neva | 64.2% | +13.9 pp |
> | bison_papr_neva | 63.2% | +12.9 pp |
> | **bison_swan_papr_neva** | **64.2%** | **+13.9 pp** |
>
> **Root cause of remaining 4.1 pp gap to SOTA:**
> - 8 structural-limit items: gold not in PPR top-200 (e.g. "Sulfabenzamide MW≈276 for vaginitis")
> - These require property reasoning, not graph traversal. Embedding retrieval ceiling reached.
>
> **Next directions:**
> 1. Larger PPR pool (top-k=50): 4 fixable items have gold in PPR top-200 — increase k may help
> 2. Multi-query decomposition: split query at "AND" constraints, run separate PPR per sub-query
> 3. Accept 64.2% as embedding-retrieval ceiling; pivot to LLM reranking of top-50 PPR candidates


> **Current best — STaRK: enriched swan_papr_neva (64.2%, 4.1 pp from SOTA). MuSiQue: papr_neva (88.8%)**
>
> **PRASE (Property-Residual Anchored Semantic Expansion):**
> Gram-Schmidt orthogonalization removes entity seed direction from query vector.
> Residual r_hat = q - (q·e_hat)·e_hat captures "property clause" in queries like
> "diseases associated with DICER1 that also SHOW OVARIAN DEVELOPMENT".
> Guards: residual_fraction ≥ 0.7 (query not dominated by entity) + cosine ≥ 0.4 (seed quality).
> +7.7 pp standalone, neutral on MuSiQue. PAPR is a stronger reranker on STaRK (path > residual).
>
> **Root-cause analysis of remaining 4.1 pp SOTA gap:**
> - 14/50 items have zero recall: 12 DILUTION, 1 NO-SEED, 1 WRONG-SEED
> - 8/14 structural limits: gold not in PPR top-200 → unreachable from any seed
> - Most likely fix: BM25 approximate name seeding (MFAR* uses this) for NO-SEED + paraphrased queries
>
> **Methods invented this session (all in codebase):**
> 1. NEVA: papr_neva 50.5%→88.8% — `src/asp/retrieval/knowledge_graph/subgraph_expansion.py:expand_neva`
> 2. SWAN: swan_papr_neva 54.1% — `subgraph_expansion.py:swan_seed_scores`
> 3. Enriched text: 64.2% — `src/asp/benchmarks/datasets/stark.py:_build_prime_skb(enrich_text=True)`
> 4. PRASE: 58.0% standalone — `subgraph_expansion.py:expand_prase`
>
> **Next direction (highest ROI for closing 4.1 pp SOTA gap):**
> Implement BM25 approximate name seeding. STaRK queries sometimes paraphrase entity names
> (e.g. "cyclophilin A" → PPIA gene). BM25 tokenization would catch these where cosine+SWAN fail.
> Architecture: BM25 index over node names → top-K BM25 hits merged with SWAN seeds → PPR.


> **Current best — STaRK: enriched swan_papr_neva (64.2%). MuSiQue: papr_neva (88.8%)**
>
> **Method: Enriched node text** — gene summaries + disease definitions added to STaRK node_text.
> Covers gene/protein (72%, avg 481 chars), disease (78%, avg 251 chars), drug (58%, avg 338 chars).
> node_text = "name [type]. description" (512 char truncation). Separate embedding cache:
> `~/.cache/asp_graph_embeddings/stark_prime_enriched_bge_small.npz` (192M, one-time 22min build).
> Use `--enrich-text` flag in graph_retrieval_ablation.py for STaRK runs.
>
> **Full cumulative results (enriched text + swan_papr_neva):**
> | Arm | Enriched recall@20 | vs base sym_ppr (40%) |
> |---|---|---|
> | sym_ppr | 50.3% | +10.3 pp |
> | papr_neva | 57.2% | +17.2 pp |
> | swan_neva | 62.7% | +22.7 pp |
> | **swan_papr_neva** | **64.2%** | **+24.2 pp** |
> | MFAR* SOTA | 68.3% | +28.3 pp |
>
> **Remaining gap: 4.1 pp to SOTA, 25.8 pp to 90% target.**
>
> **Four stacked mechanisms (all orthogonal, all compound):**
> 1. Enriched text: better node embeddings → better seeds/witness scores for all arms
> 2. SWAN: witness-based seeding from top-100 cosine anchors → extra seeds for opaque nodes
> 3. PAPR: path-quality reranking from seeds via relation-label cosines
> 4. NEVA: neighborhood-evidence reranking of PPR pool
>
> **Report:** `docs/research/graph_enrich_v1_report.md`
> **New code:** `enrich_text` param in `load_stark_prime_items()` + `_build_prime_skb()` in
> `src/asp/benchmarks/datasets/stark.py`; `--enrich-text` CLI flag in ablation script.
>
> **Next directions to close 4.1 pp to SOTA:**
> 1. **BM25 seeding**: hybrid BM25+cosine seeding (MFAR* uses this). Catches paraphrased entity names.
> 2. **Unenriched types**: anatomy/biological_process/effect-phenotype (57K nodes) have no descriptions.
> 3. **Deeper expansion**: 3-hop PPR to reach ~25% of gold not in 2-hop neighborhood.


> **Current best — STaRK: swan_papr_neva (54.1%). MuSiQue: papr_neva (88.8%)**
>
> **Method summary (SWAN — Spectral Witness Anchored seeding):**
> Top-100 cosine anchors propagate their cosine score to 1-hop neighbors as "witness scores".
> `witness(u) = max_{a ∈ Anchors ∩ N(u)} cosine(q, embed(a))`
> Final seeds = cosine top-k (unchanged) ∪ top-k witness extras (geomean scored).
> INVARIANT: cosine seeds NEVER replaced — only augmented. Early-exit to pure cosine when n≤anchor_m.
>
> **swan_papr_neva (full stack):** SWAN seeds → PAPR path reranking → NEVA witness reranking.
> Three orthogonal mechanisms: seeding (SWAN), path quality (PAPR), neighborhood evidence (NEVA).
>
> **Results:**
> - STaRK: swan_papr_neva=54.1% (+14.1 pp over sym_ppr 40.0%)
> - MuSiQue: swan_papr_neva=87.2% (+2.0 pp); papr_neva still best at 88.8%
>
> **Cumulative STaRK:** sym_ppr 40.0% → RCAD 42.6% → PAPR 44.3% → papr_neva 50.5% → **swan_papr_neva 54.1%**
> Published SOTA (MFAR*): 68.3% — remaining gap = 14.2 pp
>
> **Report:** `docs/research/graph_swan_v1_report.md`
> **New code:** `swan_seed_scores()` in `src/asp/retrieval/knowledge_graph/subgraph_expansion.py`;
> `swan_ppr`, `swan_neva`, `swan_papr_neva` arms in `graph_retriever.py` + ablation script;
> 5 new SWAN unit tests (59 total passing).
>
> **Next directions to close 14.2 pp gap to SOTA:**
> 1. **BM25 seeding**: approximate name matching catches paraphrased entity names. MFAR* uses this.
> 2. **Enriched node text**: STaRK node_info has gene summaries (72% coverage) + disease definitions
>    (78% coverage) unused in current `name [type]` embeddings.
> 3. **3+-hop expansion**: ~30% of gold not reachable from any SWAN seed within 2 hops.


>
> **Method summary (NEVA — Neighbor-Evidence Augmented PPR):**
> For each PPR candidate node c: score by the MAX cosine of its 1-hop neighborhood to query.
> `witness_cos(c) = max_{u ∈ N(c)} cosine(q, embed(u))`. Interpolate with PPR: `(1-λ)·ppr + λ·witness`.
> Addresses the opaque-name failure class: gene abbreviations (EDN1 rank 23888 by cosine), drug names
> (Venlafaxine rank 4562) have semantically-named neighbors that align with the query.
>
> **papr_neva (composed):** PAPR path-quality reranking → NEVA witness scoring. +10.5 pp STaRK.
> The two mechanisms are orthogonal: PAPR improves precision from named-entity seeds; NEVA improves
> precision for all candidates via local neighborhood evidence. They compound.
>
> **Cumulative STaRK progress:** sym_ppr 40.0% → RCAD 42.6% → PAPR 44.3% → **papr_neva 50.5%**
> Published SOTA (MFAR*): 68.3% — remaining gap = 17.8 pp (was 24 pp before NEVA).
>
> **Report:** `docs/research/graph_neva_v1_report.md`
> **New code:** `expand_neva()` in `src/asp/retrieval/knowledge_graph/subgraph_expansion.py`;
> `neva`, `neva_entity`, `papr_neva` arms in `graph_retriever.py` + ablation script;
> 7 new NEVA unit tests (54 total passing).
>
> **Next directions to close 17.8 pp gap to SOTA:**
> 1. Better seeding: BM25 approximate string matching against node names (catches paraphrased entity names)
> 2. 3+-hop expansion: ~30% of gold not reachable in 2 hops from cosine seeds (structural reach limit)
> 3. Enriched node embeddings: STaRK node_info contains gene summaries and disease definitions not in
>    current `name [type]` text — full content embeddings would improve cosine seeding quality


> **2026-07-28 — EE5-X1 (dense seed arm) NOT PROMOTED. Read [ee5_x1_findings.md](ee5_x1_findings.md).**
> A dense (`bge-small` cosine) seed-scoring arm fused with BM25 via RRF was added
> to attack the ObliQA recall ceiling. Diagnosis first located **100% of ObliQA
> recall loss at the BM25 seed stage** (cross-encoder rerank + bundle assembler
> drop **zero** gold — verified). ASP's seed scorer was lexical-only while flat
> already has a dense arm, so this looked like removing a handicap. **Net effect
> +0.30 pp (76.09→76.39, n=500) — within noise.** Root cause: **68% of BM25-buried
> gold is also buried by dense embeddings** (median dense rank 75); RRF is not the
> bottleneck. The residual loss is **multi-target dispersion** (gold that only
> makes sense as a set), not vocabulary mismatch. Single-target recall is already
> 85.8% and near its limit; the ceiling lives in the **multi-target subset (45%)**.
> - **No-regression:** guaranteed structurally — `dense_arm` defaults False, so all
>   published runs (CUAD/GovReport/ObliQA-baseline) are byte-identical. `--dense-arm`
>   flag exists for ablation only, per the layered-API design (internal retriever
>   stage, not a user-selected mode).
> - **New code:** `retrieval/dense_node_scorer.py` (`score_nodes_dense`, `fuse_rrf`);
>   `--dense-arm` CLI + `DocumentTreeRAGPipeline(dense_arm=...)`; retrieval-only recall
>   harness `benchmarks/ee5_x1_recall_harness.py`.
> - **Next lever (EE5-X2, mandate from this result):** query decomposition +
>   coverage-maximising bundle selection, targeting the multi-target subset. See
>   `experiment-backlog/epic-ee5-coverage-stop.md`.

> **2026-07-28 — Multi-target ceiling diagnosed → EE6 charter. NEXT ACTION: EE6-X0 probe.**
> Read [multitarget_ceiling_diagnosis.md](multitarget_ceiling_diagnosis.md) and the
> [EE6 charter](../../experiment-backlog/epic-ee6-requirement-coverage.md). Four diagnostics
> ruled out **every** similarity-based fix for the ObliQA multi-target ceiling (44.9% recall):
> dense arm +0.30 pp (EE5-X1); 68% of BM25-buried gold also buried by dense; only 5.9% of
> multi-target gold cite each other; gold passages do **not** cluster in embedding space
> (gold↔gold 0.755 vs gold↔random 0.683 = **1.11×**, noise). The signal that binds scattered
> gold into one answer is **not in the corpus geometry** — it is an external *requirement
> schema* the human annotator imposes ("a complete answer must cover {report, recipient,
> documentation, standard, exceptions}"). EE6 reframes retrieval as **requirement coverage
> over an induced tree schema** (set-cover, not ranking) — the ASP thesis in its strongest
> form. The novel/risky step is unsupervised schema induction, so **do EE6-X0 first**: a
> cheap pre-registered probe (decompose each multi-target question into slots, seed per slot,
> check if the per-slot union recovers ≥10 pp more multi-target gold than single-query top-k).
> If X0 fails, the ceiling is a dataset property and that is itself a publishable result. Do
> NOT start the EE6-X1 build until X0 clears the bar.
> - **New docs:** `docs/research/multitarget_ceiling_diagnosis.md`,
>   `experiment-backlog/epic-ee6-requirement-coverage.md`; probe script
>   `scripts/ee5_human_connection_probe.py`.

> **2026-07-28 — EE6-X0 GATE FAILED. Multi-target ceiling is gold under-determination, not a**
> **retriever bug. STOP chasing multi-target recall on ObliQA.** Read
> [ee6_x0_findings.md](ee6_x0_findings.md). The pre-registered probe (n=119, equal 20-seed
> budget): single-query baseline 57.7% vs requirement-decomposition per-slot union 49.6%
> (**−8.1 pp** — decomposition *hurts*, vs the +10 pp bar). Decisive: **slot→gold alignment
> 66.7%** — even the unbudgeted union of every induced requirement slot reaches only 2/3 of
> the gold, so **1/3 of multi-target gold is not derivable from the question at all** (it's
> related-obligation background a domain expert supplies, e.g. record-keeping/retention the
> question never names). No mechanism over `(question, corpus)` — similarity or coverage —
> recovers gold the question doesn't point to. **EE6-X1 build halted at the gate (working as
> designed).** This closes the retrieval-mechanism search for ObliQA multi-target.
> - **Paper framing (recommended):** headline ObliQA **+38 pp overall, single-target 85.8%**;
>   characterise the multi-target gap as *quantified gold under-determination* (67% reachable
>   ceiling) — a benchmark property, named precisely = a contribution, not a weakness.
> - **If multi-target must improve:** needs either a dataset with question-determined
>   multi-target gold, or an external domain-knowledge source (regulation ontology) — which
>   is a **knowledge-graph-shape** system, not the document-tree retriever.
> - **New:** `scripts/ee6_x0_schema_probe.py`, `data/benchmarks/ee6_x0_schema_probe.json`,
>   `docs/research/ee6_x0_findings.md`.

> **2026-07-28 — DECISION: Fine-tuning EE8 will NOT be pursued. Document-tree shape work is COMPLETE.**
> The retrieval-layer and generation-lever investigation is closed. CUAD is already at 87–90% EM
> with the 30B/119B slots (fine-tuning is a cost-parity lever, not an accuracy lever, and the
> Phase-1 cost proof is already established by the retrieval efficiency results). ObliQA and
> GovReport-QS metrics are retrieval-bound — a fine-tuned generator cannot move them by
> construction. **Next shape: Knowledge Graph** — see Priority 1 below.

> **2026-07-27 pivot — read [ADR 0001](../adr/0001-reject-min-context-floor-adopt-recall-datasets.md) first.**
> The `min_context_tokens` floor (EE4-X1) is **rejected** as a production mechanism.
> The MAUD floor "win" was the flat comparator regressing under the same knob, not ASP
> improving. On CUAD the floor is a **no-op** (nodes have no parents/siblings — identical
> per-row context with or without floor; the ctx700 CUAD EM drop was inference drift, not
> dilution). **The clean CUAD baseline is ~87–90% EM** (standard prompt, no floor: `rr`,
> `rr_final`, `llama_base`, `cot_ms` all land there; the old "~79% stable baseline" was
> a conflation of floor/prompt-experiment/struct runs — see the corrected CUAD note below).
> MAUD and HotpotQA are documented **applicability boundaries**, not targets.
>
> **ObliQA and GovReport-QS baselines COMPLETE (2026-07-27).**
> Run tag `rr_notokenfloor`, `--min-context-tokens 0`, n=500, 0 errored rows.
> Findings + full scorecards: `docs/research/hierarchical_datasets_findings.md`.
> Short results:
> - **ObliQA:** ASP **76.1%** recall vs flat **37.9%** (**+38 pp**). Both models identical.
>   ASP ~28% fewer tokens, ~6× faster. Metric: whole-span recall (fair — gold ~79 words).
> - **GovReport-QS:** ASP **89.6%** token-recall vs flat **34.9%** (**+55 pp**). Whole-span
>   recall is misleading for flat (1.8%) because gold sections are ~463 words and flat's
>   chunks are ~256 tokens — the correct metric is **token-recall** (fraction of gold words
>   in context). ASP spends more tokens (~4,000 vs ~1,600): a recall-for-tokens trade.
> - **ADR multi-hop prediction falsified:** ASP's advantage is larger on single-target
>   questions (+42 pp ObliQA, +98 pp GovReport-QS) than multi-target (+26 pp, +70 pp).
>   ASP wins every subset; the mechanism story is "retrieves the right intact unit", not
>   "multi-hop traversal benefit". Recorded, not buried (see ADR 0001).
> - **EE4-X2 shelved.** New loaders: `datasets/{obliqa,govreport_qs}.py`.
> - **Metrics added:** `token_recall` in `metrics/span_recall.py`; pipeline emits both.
> - **Runner hardened:** transient errors retry indefinitely; errored rows not checkpointed
>   (retry on resume); resume is backend-symmetric (ASP+flat always score same questions).

---

## Objective

Prove — with reproducible, same-run, same-condition benchmarks — that a structure-aware
retrieval pipeline (ASP document-tree) beats commodity flat hybrid RAG on enterprise
document workloads *and* that the gain is attributable to the retrieval mechanism itself,
not to any single component in isolation (heading-path chunking, prompt engineering, or
LLM knob choice).

The paper claim we are building toward:

> "After retrieval was fixed (EE2: recall@6 0.11 → 0.89), ASP dramatically outperforms
> flat RAG on structured document extraction (CUAD +7.6–17.7 pp EM across configurations)
> and is competitive on legal classification (MAUD +4.2 pp after context floor EE4-X1).
> The remaining generation gap on multi-hop QA (HotpotQA −4 pp F1) is attributable
> entirely to false-absence over-triggering on multi-source bundles — wrong-answer rates
> are identical between ASP and flat.
>
> Critically, ASP's advantage over flat RAG cannot be explained by heading-path chunking
> alone: EE-Struct (`flat_struct` — same parser, same heading paths, flat retrieval)
> captures *none* of the CUAD gain (Δ vs plain flat: −0.2 pp Nem, −3.4 pp Mis) and is
> *worse* than plain flat on MAUD (−12.8 pp Nem, −8.4 pp Mis). Tree traversal,
> parent/sibling expansion, and authority-ordered bundle assembly are the load-bearing
> mechanisms."

---

## Rules of engagement (do not violate)

1. **Dataset-agnostic** — no `if dataset == "hotpotqa"` logic, no hardcoded thresholds.
2. **No-regression** — CUAD, Travel Policy, JCP Credit must stay within 1 pp of their current baselines.
3. **Principled** — every change must be preceded by a one-sentence statement of which
   property of the retriever it adds.
4. **Measured against flat** — ASP numbers in isolation are not a result. Same-run flat
   hybrid RAG on the same questions is always the comparator.
5. **No LLM knob changes yet.** Temperature, top_p, reasoning_budget are EE0's domain and
   EE0 is blocked until bundle format is stable.
6. **No test-split tuning.** Tune on a held-out validation slice.

Negative results are first-class artifacts. Write them up in `docs/research/` whether
they win or lose.

---

## Current results — headline table

Baseline: `rr_final` (round-robin seed selection promoted from EE2-X1, standard prompt
except HotpotQA which uses `cot_cite` gated per EE3-X1b). Same-run, same-question
comparison of ASP vs flat.

**Document-tree shape — primary three datasets (final, publishable):**

| Dataset | ASP | Flat | Δ | Primary metric |
|---|---:|---:|---:|---|
| CUAD | ~87–90 | ~70 | **+14–19** | EM% (span extraction) |
| ObliQA | 76.1 | 37.9 | **+38.2** | Span recall% |
| GovReport-QS | 89.6 | 34.9 | **+54.7** | Token-recall% |

Both recall datasets are model-agnostic (Nemotron-30B = Llama-49B to the decimal).

**Documented applicability boundaries (not targets):**

| Dataset | Model | ASP | Flat | Δ | Primary metric |
|---|---|---:|---:|---:|---|
| HotpotQA | Nemotron | 50.3 | 54.2 | −3.9 | EM% |
| HotpotQA | Mistral | 52.6 | 56.9 | −4.2 | EM% |
| MAUD | Nemotron | 36.2 | 39.4 | −3.2 | Accuracy% |
| MAUD | Mistral | 37.8 | 41.6 | −3.8 | Accuracy% |
| Travel Policy | Nemotron | F1 11.8 | F1 7.6 | +4.2 | F1% (EM=0 by design) |
| Travel Policy | Mistral | F1 9.6 | F1 8.9 | +0.7 | F1% |
| JCP Credit | Nemotron | F1 10.6 | F1 8.1 | +2.5 | F1% |
| JCP Credit | Mistral | F1 10.9 | F1 9.1 | +1.8 | F1% |

**CUAD baseline — CORRECTED 2026-07-27 (supersedes the old "~79%" caveat).** Pulling
per-run numbers from the result JSONs: every **standard-prompt, no-floor** run scores
**86.8–90.2% EM** (`rr` 90.2, `rr_final` 86.8/88.6, `llama_base` 88.8–90.2, `cot_ms`
87.2–88.8). The old "~79% stable baseline" was an artifact of averaging together three
*different* conditions: (a) `ctx700` floor runs (81.0/78.8 — but the floor **never fired**
on CUAD; per-row context is identical to no-floor at avg 240 tok / 0 rows ≥700, so the
drop is inference drift), (b) prompt experiments (`cot_inline`, `soft_ms`, `source_tag`
at 75–77%), and (c) the `struct_baseline` 3-backend run on different slots (66.8–73.6%).
**Use ~87–90% EM as the no-regression anchor** for the standard/no-floor config. Δ vs flat
is +14–18 pp across all runs. (The floor does not affect CUAD either way — CUAD nodes have
no parents/siblings to expand, so `--min-context-tokens` is a no-op there.)

**Travel Policy/JCP Credit EM = 0% is a metrics problem, not a retrieval problem.** EM is the wrong metric
for open-ended policy/financial QA. F1 is the right metric. `short_answer` fields have
been added to both gold files (`eval_questions.json`, `ground_truth.yaml`) to support a
future LLM-judge evaluation. Do not re-run Travel Policy/JCP Credit to try to fix EM.

**MAUD note:** the `rr_final` MAUD numbers above use the default context (no floor).
EE4-X1 (context floor 700 tokens) flips MAUD to ASP-wins: **+4.2 pp Nem, +1.2 pp Mis**.
That result is in `data/benchmarks/maud_m{3,4}rr_ctx700_results.json`. The remaining
issue is that the floor also fires on CUAD and adds mild dilution — EE4-X2 (score-variance
gate) is the queued fix.

---

## Experiment history — complete

### EE1 — Recall Audit (Done)
Established HotpotQA recall@6 = 0.110 (all citations from single article). High F1 was
LLM world-knowledge masking. **Decision:** recall is the primary retrieval metric.
See: `docs/research/ee1_findings.md`

### EE2-X1 — Round-Robin Seed Selection (Done, Promoted)
Per-`doc_slug` seed quota lifted recall@6 **0.110 → 0.891 (8.1×)**. F1 flat (masking
confirmed). Exposed 78 retrieval-hit / gen-miss rows — the EE3 test bench.
No-regression: all 5 datasets within bar.
See: `docs/research/ee2_x1_findings.md`

### EE3-X1 — CoT Citation Scaffold, ungated (Not Promoted)
25–30% test-bench recovery but failed no-regression on all single-source datasets.
Root cause: fallback extractor picked "We" from mid-paragraph quotes.
See: `docs/research/ee3_x1_findings.md`

### EE3-X1b — Multi-Source-Gated CoT (Partially Promoted)
Gate on `num_sources > 1` eliminated single-source regressions. Test-bench recovery
30–31%. Net F1 transitions vs `rr_final`: −1 (Nem) / −9 (Mis).
**Current best HotpotQA prompt variant.** ASP F1 = 65.2% (Nem) / 63.8% (Mis).
See: `docs/research/ee3_x1b_findings.md`

### EE3-X1c — Regression Failure Analysis (Done)
26 regressions decomposed into 9 fixable (extraction artifacts) + 17 hard (bridge-reasoning
divergences where pre-commitment disrupts multi-hop chain). Not fixable by extraction
alone.
See: `docs/research/ee3_x1c_findings.md`

### EE3-X1d — Inline Citation Scaffold (Not Promoted)
Inverted Evidence→Answer to Answer→Evidence. Extraction bug found and fixed (extractor
was not stripping `Answer:` prefix). After fix, net transitions vs X1b: **−33 (Nem) /
−41 (Mis)**. Inline format disrupts bridge reasoning more than it fixes extraction.
See: `docs/research/ee3_x1d_findings.md`

### EE3-X5 — Multi-Source Soft Not-Found Prompt (Not Promoted)
Softened "not found" instruction for `num_sources > 1` bundles. Net transitions:
**−38 (Nem) / −32 (Mis)**. False-absences barely moved (66→57 Nem; 63→66 Mis vs flat's
24–35). Root cause: false-absence is structural (entity-location failure across 6 chunks),
not instruction-text driven.
See: `docs/research/ee3_x5_findings.md`

### EE3-X3 — Source Tagging in Multi-Source Bundle (Not Promoted)
Added `[Source A]`, `[Source B]` chunk prefixes + `Sources:` legend header, gated on
`num_sources > 1`. False-absences dropped **65→50 (Nem) / 62→48 (Mis)** — entity
localisation improved. But net F1 vs X1b: **−67 (Nem) / −40 (Mis)** because `[Source A]`
prefix reactivated the "We" extraction artifact from EE3-X1 (42/87 Nem regressions
predicted exactly "We"). CUAD and EW no-regression passed.
See: `docs/research/ee3_x3_findings.md`

### EE3-X3b — Source Tagging + Standard Prompt (Not Promoted)
Ran X3 assembler with `--prompt-mode standard` to bypass the "We" extraction artifact.
False-absences did NOT drop — went back to 65 (Nem) / 58 (Mis), nearly baseline. Net
transitions: **−50 (Nem) / −41 (Mis)**. Wrong-answer counts also rose. Flat baseline also
degraded (58.8% vs 65.8% rr_final F1) — label format disrupts model comprehension
independently of CoT.

**Conclusion on source-tagging direction:** Two independent runs (X3 and X3b) confirm the
current `[Source A]` block-prefix format hurts generation more than it helps localisation.
Do not revisit without a fundamentally different format (lighter inline annotation
appended to the compact header, not a separate prefix block) and a held-out validation
slice.
See: `docs/research/ee3_x3_findings.md` (X3b appended)

### EE4-X1 — Context Floor 700 tokens (Partially Promoted)
`min_context_tokens=700` floor in bundle assembler raised MAUD context 497 → 825 tokens.
**MAUD flipped to ASP-wins for the first time:** +4.2 pp Nem / +1.2 pp Mis. Among the 345
rows where the floor fired, +2.9 pp / +1.4 pp accuracy improvement — clean causal signal.
Context starvation was the correct diagnosis.
**Downside:** floor also fires on CUAD (avg 240 tok) and adds contract boilerplate → mild
CUAD dilution. Floor needs a smarter gate before production. That gate is EE4-X2.
See: `docs/research/ee4_x1_findings.md`

### EE-Struct — Structure-Aware Chunking Baseline (Done)

**What we ran:** A third backend `flat_struct` (`src/asp/benchmarks/pipelines/flat_structured_rag.py`)
that uses the same document-tree parser as ASP (`_parse_to_tree`, `_fallback_paragraph_nodes`)
to produce section-level nodes with heading paths, converts each node to a heading-path-prefixed
text chunk (`"{heading_path}\n{title}\n{content}"`), and then runs plain hybrid BM25+vector+RRF+
cross-encoder retrieval — no tree traversal, no parent/sibling expansion, no authority
ordering. HiQA-style "structure-aware chunking".

**Run tag:** `rr_struct_baseline`. n = 500 per dataset. Same-run ASP + flat + flat_struct.

**Result — CUAD:**

| Model | Metric | ASP | Flat | Flat-Struct | ASP−Struct | Struct−Flat |
|---|---|---:|---:|---:|---:|---:|
| Nemotron (m3) | EM% | 73.6 | 58.6 | 58.4 | **+15.2** | −0.2 |
| Nemotron (m3) | F1% | 77.5 | 62.1 | 62.5 | +15.0 | +0.4 |
| Mistral  (m4) | EM% | 66.8 | 59.2 | 55.8 | **+11.0** | −3.4 |
| Mistral  (m4) | F1% | 71.3 | 63.2 | 60.4 | +10.9 | −2.8 |

**Result — MAUD (no context floor — see caveat):**

| Model | Metric | ASP | Flat | Flat-Struct | ASP−Struct | Struct−Flat |
|---|---|---:|---:|---:|---:|---:|
| Nemotron (m5) | Accuracy% | 28.8 | 31.6 | 18.8 | +10.0 | **−12.8** |
| Mistral  (m6) | Accuracy% | 27.6 | 32.0 | 23.6 |  +4.0 |  **−8.4** |

**Efficiency (CUAD, both models roughly equal):**

|                 | Avg context tokens | Avg latency ms |
|---              |---:|---:|
| ASP             |  240 |   51–171 |
| Flat            |  ~682 | ~8,000 |
| Flat-Struct     | ~1,600 | ~8,000 |

Flat-Struct's larger avg context on CUAD (~1,600 tok vs flat's ~682) is because heading
paths inflate each retrieved node before de-duplication caps kick in, without producing an
accuracy benefit. On MAUD, flat_struct's context collapses to ~230 tok (heading-path
chunking produces very short section-nodes for MAUD's flat clauses).

**Interpretation:**
- **CUAD:** heading-path chunking captures **essentially zero** of ASP's benefit
  (Δ Struct−Flat is ≤ 0). Tree traversal, expansion, and authority-ordered bundle assembly
  are the load-bearing mechanisms.
- **MAUD:** heading-path chunking without expansion is *worse than plain flat* by 8–13 pp
  — the very short section-nodes (~230 tok) starve the model. This is a strong negative
  result: it confirms that the parent/sibling expansion mechanism (proved by EE4-X1) is
  what makes structure useful on MAUD; structure without expansion is net-negative.
- Result files:
  - `data/benchmarks/cuad_m{3,4}rr_struct_baseline_results.json`
  - `data/benchmarks/maud_m{5,6}rr_struct_baseline_results.json`
- **Writeup:** `docs/research/ee_struct_findings.md` ✅ Done (2026-07-01)

---

## Where things stand — the paper claim

**CUAD baseline clarification (2026-07-27):** The old "stable ~79% baseline" was wrong — it conflated floor runs, prompt experiments, and struct runs. The correct anchor is **~87–90% EM** for the standard prompt / no-floor config (`rr`, `rr_final`, `llama_base`, `cot_ms` all land there). The floor (`ctx700`) is a no-op on CUAD (per-row context identical; ctx700 CUAD drop was inference drift, not floor dilution).

| Dataset | Status | Gap remaining |
|---|---|---|
| **CUAD** | ✅ ASP beats flat +14–19 pp EM — consistent across 5 configurations, 3 models; heading-path chunking contributes 0 (EE-Struct). Baseline ~87–90% EM. | None — publishable |
| **ObliQA** | ✅ NEW 2026-07-27. ASP +38 pp recall (76.1% vs 37.9%). Model-agnostic. ASP ~28% fewer tokens, ~6× faster. | None — publishable |
| **GovReport-QS** | ✅ NEW 2026-07-27. ASP +55 pp token-recall (89.6% vs 34.9%). Recall-for-tokens trade. Model-agnostic. | Token-recall vs whole-span distinction documented (§6.6 honesty note). |
| **MAUD** | ⚠️ **Reframed as applicability boundary.** Floor "win" was flat regressing. No-floor baseline: ASP −3.2/−3.8 pp. MAUD has no genuine hierarchy — flat clause list. | None needed — boundary, not target. |
| **HotpotQA** | ⚠️ **Reframed as applicability boundary.** ASP trails flat −4 pp F1; gap is false-absence over-triggering (wrong-answer rates identical to flat). World-knowledge-answerable, short intros. | EE3-X4 (adaptive bundle width) = last retrieval-layer option. If fails → accept and attribute. |
| **Travel Policy / JCP Credit** | ⚠️ ASP beats flat on F1 (+2–7 pp). | EM is wrong metric; short_answer fields added to gold for future LLM-judge eval. |

**HotpotQA high-water mark (all runs):**
- ASP best F1: 66.2% (original `rr` run, standard prompt, m2)
- Flat: 71.7% F1 (rr_final m2) — gap ~5.5 pp F1
- Best deployed variant: X1b (`cot_cite` gated), F1 65.2% Nem / 63.8% Mis
- False-absence floor: **62** (X1b) — source-tagging direction (X3, X3b, X5) exhausted

---

## What is next — priority order

### ✅ Document-tree shape — COMPLETE

The document-tree retrieval shape is fully proven and publishable. No further retrieval
experiments needed. The three headline results are final:

| Dataset | ASP | Flat | Δ | Metric |
|---|---:|---:|---:|---|
| CUAD | ~88 | ~70 | **+14–19 pp** | EM% |
| ObliQA | 76.1 | 37.9 | **+38.2 pp** | Span recall% |
| GovReport-QS | 89.6 | 34.9 | **+54.7 pp** | Token-recall% |

EE8 (fine-tuned reader) will NOT be pursued — CUAD is already at ceiling for the 30B
slots, and the two recall datasets are generation-independent.

---

### Priority 1 (NOW): Knowledge Graph shape — KG1

**Why it's next:** The document-tree shape proves that structure-aware retrieval over a
native tree dramatically outperforms flat RAG on hierarchical documents. The next shape
in the ASP protocol is the **knowledge graph** — for datasets where the retrieval signal
is entity relationships and graph traversal (fraud patterns, supply chains, regulatory
obligation networks), not document hierarchy.

The EE6-X0 result points directly here: 1/3 of ObliQA multi-target gold is not derivable
from the question alone — it's related-obligation background that a domain expert supplies.
The document-tree retriever has no mechanism for this; a knowledge-graph shape that models
obligation relationships can. This is the strongest possible motivation: a failure mode of
the finished shape becoming the first benchmark for the next shape.

**Proposed scope:**
1. **Select a graph-native dataset** — candidates from the ObliQA/regulatory domain:
   - ObliQA multi-target subset (119 Qs with gold under-determination) — use the same
     corpus, build a regulation-obligation graph over the 40 ADGM rulebook documents
   - REBEL / DocRED — entity-relation extraction benchmarks with ground-truth graphs
   - Microsoft GraphRAG benchmark set (community detection + global QA)
   - PolicyQA (confirmed in memory as included in the ASP dataset scope)
2. **Build `src/asp/retrieval/knowledge_graph/`** — Neo4j backend, mirroring the
   document-tree shape's interface contract (`retrieve() → ContextBundle`).
3. **Benchmark same-condition vs flat** — same runner, same evaluation harness, new shape.

**Key architectural similarity to document-tree:**
- Both shapes retrieve *structured units*, not chunks
- Both use graph traversal (tree edges vs. entity/relation edges)
- Bundle assembly contract is identical — the ASP protocol's shape neutrality is the claim

**Key difference (the new engineering challenge):**
- Document-tree: structure is given by the document's own heading hierarchy
- Knowledge graph: structure must be induced (entity extraction, relation typing, schema)
  or supplied by an external ontology

This is exactly the EE6 insight: the structure that connects multi-target gold passages is
an *external regulation ontology* — a knowledge-graph-shape problem, not a document-tree
problem.

**Suggested first task (KG1-X0 — cheap feasibility probe):**
Before building the full KG pipeline, verify that a graph built over the ObliQA corpus
(entity = regulation obligation, edge = "conditions", "references", "applies-to") actually
connects the multi-target gold passages that the document-tree shape misses. If the
gold-obligation graph is dense enough that graph traversal from the question reaches the
missing gold, the KG shape is worth building. If not (gold obligations are also
disconnected in the graph), document the ceiling and move to a cleaner graph-native dataset.

**Effort estimate:** M (feasibility probe) → XL (full KG shape + benchmark)

---

### Priority 2: Paper write-up

The publishable claims are already in hand without any further experiments:

1. **CUAD** — ASP +14–19 pp EM, consistent across 5 configurations, 3 base models. EE-Struct ablation proves traversal mechanism, not heading-path chunking. ~3× cheaper context, faster.
2. **ObliQA** — ASP +38 pp recall, cheaper + faster. Model-agnostic.
3. **GovReport-QS** — ASP +55 pp token-recall (intact-section retrieval). Model-agnostic.
4. **MAUD / HotpotQA** — documented applicability boundaries with clear mechanism explanations.

The case study v2.2 (`docs/research/doc_tree_rag_case_study_v2.md`) is the draft. The paper section that remains thin is the **"when to use document-tree shape"** routing criterion — the subset analysis provides the empirical basis but the criterion hasn't been formalised as a decision rule yet.

---

## Infrastructure notes

### Pipeline files
```
src/asp/benchmarks/pipelines/document_tree_rag.py    — main ASP pipeline (all prompts/logic)
src/asp/benchmarks/pipelines/flat_hybrid_rag.py      — flat BM25+vector+rerank baseline
src/asp/benchmarks/pipelines/flat_structured_rag.py  — struct-aware chunking baseline (EE-Struct)
src/asp/benchmarks/retrieval/bundle_assembler.py     — context bundle (EE4-X1 floor here)
src/asp/benchmarks/retrieval/bm25.py                 — BM25 + round-robin seeding (EE2-X1)
src/asp/benchmarks/retrieval/dense_node_scorer.py    — dense seed arm + RRF fusion (EE5-X1, off by default)
src/asp/benchmarks/ee5_x1_recall_harness.py          — retrieval-only recall harness (no LLM; EE5-X1)
src/asp/benchmarks/runner.py                         — checkpoint-based runner
src/asp/benchmarks/model_client.py                   — multi-slot LLM client (6 API keys)
src/asp/benchmarks/datasets/travel_policy_jcp_credit.py      — Travel Policy/JCP Credit loader (short_answer added)
```

### Prompt modes (`document_tree_rag.py`)

| `prompt_mode` | Description | Status |
|---|---|---|
| `standard` | Plain Q&A, strict not-found | Production baseline |
| `cot_cite` | Evidence-first CoT, gated on `num_sources > 1` | **Best HotpotQA variant (X1b)** |
| `cot_inline` | Answer-first, cite-after, gated | Not promoted (worse than cot_cite) |
| `soft_ms` | Softened not-found for multi-source | Not promoted (causes over-synthesis) |

### Source-tagging state
`bundle_assembler.py` currently still emits `[Source X]` prefixes + legend header for
multi-source bundles (EE3-X3 code is in place). Both X3 (`cot_cite`) and X3b (`standard`)
were net-negative. Options:
- **Revert:** remove the `_letter_label` helper and the `source_tags` block in
  `assemble_context_bundle()`.
- **Keep gated:** the current code is gated on `num_sources > 1`, so it does not affect
  single-source datasets. Safe to leave in place while a lighter inline-annotation
  variant is designed.

### Run tag conventions
```
rr_final             — round-robin baseline (clean, all 5 datasets)
rr_cot_ms            — EE3-X1b gated CoT (best HotpotQA variant)
rr_cot_inline        — EE3-X1d inline citation (corrected, not promoted)
rr_soft_ms           — EE3-X5 soft not-found (not promoted)
rr_ctx700            — EE4-X1 context floor (partially promoted, MAUD win)
rr_source_tag        — EE3-X3 source tagging + cot_cite (not promoted)
rr_source_tag_std    — EE3-X3b source tagging + standard prompt (not promoted)
rr_struct_baseline   — EE-Struct structure-aware chunking baseline (done)
rr_ctx_var           — EE4-X2 score-variance gate (SHELVED — never run, see ADR 0001)
rr_notokenfloor      — ObliQA + GovReport-QS recall baselines, no floor (2026-07-27)
ee5_x1_dense         — EE5-X1 dense seed arm (--dense-arm), retrieval-only harness (not promoted, 2026-07-28)
```

### How to run a benchmark
```bash
# Single slot, single dataset, both backends
.venv/Scripts/python.exe -m src.asp.benchmarks.runner \
  --datasets hotpotqa --backends asp flat --limit 500 \
  --run-tag rr_ctx_var --model-slots 1 --rate-delay 2.0 --min-context-tokens 700 --no-report \
  > data/benchmarks/run_s1.log 2>&1 &

# Three-backend struct comparison (as used for EE-Struct)
.venv/Scripts/python.exe -m src.asp.benchmarks.runner \
  --datasets cuad --backends asp flat flat_struct --limit 500 \
  --run-tag rr_struct_baseline --model-slots 3 --rate-delay 2.0 --no-report \
  > data/benchmarks/cuad_struct_m3.log 2>&1 &

# Slot convention: 1/3/5 = Nemotron-30B; 2/4/6 = Mistral-119B (see .env)
```

### How to run the recall datasets in parallel (ObliQA + GovReport-QS)

Chroma collections are keyed by **dataset name, not slot**, so two concurrent
processes on the *same* dataset collide on `create_collection` and one dies.
**Pre-warm each index once, then launch single-slot processes:**

```bash
# 1. Warm both indices once (builds Chroma + BM25, then exits):
.venv/Scripts/python.exe -c "from src.asp.benchmarks.datasets import load_obliqa_items, load_govreport_qs_items; \
from src.asp.benchmarks.retrieval.hybrid import HybridIndex; \
[HybridIndex(n).build(l(split='test', limit=500)) for n,l in \
[('obliqa',load_obliqa_items),('govreport_qs',load_govreport_qs_items)]]"

# 2. Launch 4 single-slot processes (all skip ingestion — no race):
for slot in 1 2; do .venv/Scripts/python.exe -m src.asp.benchmarks.runner \
  --datasets obliqa --backends asp flat --limit 500 \
  --run-tag rr_notokenfloor --model-slots $slot --rate-delay 3.0 --no-report \
  > data/benchmarks/run_obliqa_m$slot.log 2>&1 & done
for slot in 3 4; do .venv/Scripts/python.exe -m src.asp.benchmarks.runner \
  --datasets govreport_qs --backends asp flat --limit 500 \
  --run-tag rr_notokenfloor --model-slots $slot --rate-delay 3.0 --no-report \
  > data/benchmarks/run_govqs_m$slot.log 2>&1 & done
```

Rate math: 4 parallel = 2 slots per model pool → `--rate-delay 3.0` (20 rpm each)
keeps each 40-rpm pool at its limit. If running only 2 slots (one per pool), bump
to `--rate-delay 1.5` (40 rpm each).

Results: `data/benchmarks/{dataset}_m{slot}{tag}_results.json`
Checkpoints: `data/benchmarks/{dataset}_m{slot}{tag}_{backend}_checkpoint.jsonl`

---

## Dataset notes

### CUAD (500 contract-clause extraction questions)
- Primary metric: **EM** (span extraction).
- ASP beats flat **+14–18 pp EM** across every measured configuration — most reliable win.
- Baseline (standard prompt, no floor): **~87–90% EM / ~91–93% F1** (see corrected CUAD
  note above). The old "~79%" was a conflation of floor/prompt-experiment/struct runs.
- EE4-X1 floor is a **no-op on CUAD** (nodes have no parents/siblings to expand; per-row
  context identical with/without floor). EE4-X2 shelved (ADR 0001).
- EE-Struct: heading-path chunking alone captures 0 of the gain (`flat_struct` ≈ flat).

### MAUD (500 legal M&A multiple-choice classification)
- Primary metric: **accuracy** (no F1 — MC format).
- Default context: ASP trails flat by 3–4 pp.
- EE4-X1 context floor (min 700 tok) flips it: ASP +4.2 pp (Nem) / +1.2 pp (Mis).
- EE-Struct: structure without expansion is *worse than plain flat* (−8 to −13 pp) —
  confirms expansion is the load-bearing mechanism on MAUD.

### HotpotQA (500 multi-hop bridge/comparison)
- Primary metrics: **recall@6** (retrieval) + **F1** (generation).
- Best ASP F1: 66.2% (rr original, standard prompt, m2).
- Best deployed variant: X1b (`cot_cite` gated), F1 65.2% Nem / 63.8% Mis.
- False-absence floor: **62** (X1b). Wrong-answer counts identical between ASP and flat —
  the entire gap is false-absences.
- Source-tagging direction exhausted (X3, X3b). Prompt engineering direction exhausted.
  Next lever is retrieval-layer (EE3-X4 adaptive width) or accept.

### Travel Policy / JCP Credit (20 / 40 open-ended enterprise QA)
- **EM = 0% is expected and correct** — long-form gold answers; EM is the wrong metric.
- Primary metric: **F1** — ASP beats flat +2–7 pp consistently.
- `short_answer` fields added to gold files for future LLM-judge evaluation.
- A few Travel Policy false-absences (retrieval fetching wrong source-country policy) — separate
  issue, not blocking the paper.

### ObliQA (500 ADGM financial-regulation questions) — NEW, recall-only
- Primary metric: **span/passage recall** (`avg_span_recall_pct`, `full_recall_pct`).
  EM/F1 are ~0 by construction (gold is passage references, no answer string).
- Genuine nested tree from dotted PassageIDs (e.g. `14.2.3.Guidance.10.`), 40 docs /
  13,732 passages. This is the primary deep-hierarchy document-tree dataset.
- No context floor — `--min-context-tokens 0`. Analyse the multi-passage subset.
- Loaders: `datasets/obliqa.py`, `utils/loaders/document_qa/obliqa.py`.

### GovReport-QS (500 questions over GAO/CRS reports) — NEW, recall-only
- Primary metric: **section recall** (same `span_recall` path).
- Nested tree from section `depth`; retrieval corpus = full report, gold = sections
  aligned to the question's summary paragraph (proper subset → non-trivial recall).
- Loaded via HF Parquet mirror (`launch/gov_report_qs`, config `paragraph`) — the
  legacy loading script is rejected by `datasets>=3`.
- Loaders: `datasets/govreport_qs.py`, `utils/loaders/document_qa/govreport_qs.py`.

---

## Where the last artifacts landed

| Artifact | Path |
|---|---|
| Latest benchmark results (per experiment) | `data/benchmarks/*_results.json` |
| Checkpoints (row-level) | `data/benchmarks/*_checkpoint.jsonl` |
| Per-experiment findings docs | `docs/research/ee*_findings.md` |
| BM25 formula draft | `docs/research/v3_drafts/bm25_formula.md` |
| Expand-procedure draft | `docs/research/v3_drafts/expand_procedure.md` |
| Struct-baseline narrative draft | `docs/research/v3_drafts/struct_baseline.md` |
| Worked-example draft | `docs/research/v3_drafts/worked_example.md` |
| EE4-X2 forward plan | `docs/research/v3_drafts/ee4_x2_forward.md` |
| Manual CUAD trace | `docs/research/worked_example_trace.txt` |
| Handoff (this file) | `docs/research/handoff_prompt_next_session.md` |

---

## Constraints reminder (do not violate)

1. **No dataset-specific logic.** No `if dataset == "hotpotqa"`. No thresholds derived
   from inspecting benchmark answers.
2. **No test-split tuning.** Tune on a held-out validation slice.
3. **Flat baseline runs alongside.** ASP numbers in isolation are not a result.
4. **No LLM knob changes yet.** Temperature, top_p, reasoning_budget are EE0's domain.
5. **EE4 floor is gated before production.** Do not commit `min_context_tokens=700`
   globally without the score-variance gate (EE4-X2) — it hurts CUAD.
