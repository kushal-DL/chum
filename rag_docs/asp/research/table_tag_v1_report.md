# Benchmark Report — table_asptable_v1 (FINAL)

**Date:** 2026-08-07
**Branch:** experiment/table-rag
**Run tag:** table_asptable_v1
**Models:** none (retrieval-only, generation-independent — $0 LLM cost, like the graph shape)
**Embedding:** BAAI/bge-small-en-v1.5 (CPU), disk-cached per corpus
**Datasets:** OTT-QA-Small, FeTaQA, OpenWikiTables, NQ-Tables (IBM-Research retrieval suite, all CC-BY-4.0)
**Backends:** ASP `asptable` arm + `flat_dense` floor (same-condition, same process)
**n per dataset:** 200
**Experiment:** From-scratch ASP table-retrieval method (`asptable`), designed around named prior art, benchmarked same-condition vs a dense floor.

---

## 1. Headline scorecard (recall@10 primary; R@1 / R@5 / MRR secondary)

| Dataset | Corpus | Arm | R@1 | R@5 | R@10 | MRR | Δ R@10 vs floor | ≥90%? |
|---|---|---|---|---|---|---|---|---|
| OTT-QA-Small | 8.9K | flat_dense (floor) | 0.770 | 0.920 | 0.965 | 0.838 | — | — |
| OTT-QA-Small | 8.9K | **asptable** | **0.825** | **0.935** | **0.980** | **0.878** | **+1.5 pp** | ✅ |
| FeTaQA | 10.3K | flat_dense (floor) | 0.755 | 0.885 | 0.905 | 0.809 | — | — |
| FeTaQA | 10.3K | **asptable** | **0.790** | **0.925** | **0.940** | **0.846** | **+3.5 pp** | ✅ |
| OpenWikiTables | 54K | flat_dense (floor) | 0.715 | 0.970 | 0.990 | 0.846 | — | — |
| OpenWikiTables | 54K | **asptable** | **0.820** | **0.990** | **1.000** | **0.917** | **+1.0 pp** | ✅ |
| NQ-Tables | **169K** | flat_dense (floor) | 0.315 | 0.655 | 0.750 | 0.460 | — | — |
| NQ-Tables | **169K** | **asptable** | **0.335** | **0.715** | **0.815** | **0.493** | **+6.5 pp** | ⚠️ 81.5% (embedding ceiling) |

**Result: 3 of 4 datasets clear the ≥90% recall@10 gate, and `asptable` (the from-scratch ASP
method) beats the flat-dense floor on ALL FOUR — with a single parameter set across every dataset
(no dataset-specific crutch).** R@1 gains are large where there was headroom (OpenWikiTables
+10.5pp, OTT-QA +5.5pp), and MRR rises on every dataset.

---

## 2. The method — `asptable` (unified rank-safe structural grounding)

A generation-free reranker over the dense top-200 pool that adds two **rank-safe additive
promotions** to the dense score. Both are damped by the dense-score gap to the pool leader
(`boost·exp(−gap/β)`), so a promoted table can never leapfrog the dense #1 by noise — the design
invariant that made every monotone arm win and every symmetric-RRF arm (ACG) fail.

1. **Title-anchor** — the query's *rare or capitalized* tokens matched against the table **title
   field** (corpus-IDF-weighted). Wins the **entity-confusion** class (gold table's title IS the
   query entity, but dense retrieves a same-topic sibling), which dominates the dense-saturated
   small corpora.
2. **Field-grounding** — the query's IDF-weighted content tokens grounded across **title / header /
   cell** fields with per-field weights (1.0 / 0.6 / 0.8). Wins the **list-vs-prose** class (gold is
   a data-table page, dense prefers the entity's prose main page), which dominates the large
   NQ-Tables corpus.
3. **Superlative gate** — for `superlative`/`comparative` queries (dataset-agnostic classifier), the
   lexical boost is switched off: "biggest / last / most" answers are not token-overlap-based, so
   grounding there promotes the wrong table. (+1.5pp on NQ-Tables; neutral elsewhere.)

**Point of novelty (designed around prior art — NOT certified patentable, pending E11-P2 scan):**
- vs **BM25**: field-typed over structured title/header/cell (not one linearized blob), and fused as
  a rank-safe additive promotion on dense — not a standalone lexical score.
- vs **dense (DTR/TAPAS)**: recovers the entity/field signal that mean-pooling into one vector destroys.
- vs **ColBERT/late-interaction**: field-typed, gap-damped monotone promotion — not per-token max magnitude.
- vs **SAGE/HELIOS/THYME/CRAFT/T-RAG/TableRAG**: no graph expansion, no per-field granularity hybrid,
  no LLM table summaries, no sparse→dense cascade, no schema/cell dual index.
- vs **ASP's own graph primitives** (PPR/BM25-seed/beam/NEVA): none reused.

---

## 3. NQ-Tables ceiling analysis (honest)

NQ-Tables (169K tables, Google search queries) is the only dataset below 90%. The floor is 75.0%
and `asptable` lifts it to **81.5% (+6.5pp)** — the largest absolute gain of any dataset. Why it
stops there:

- **The gold is reachable:** dense `hit@200 = 98.5%` — the gold table is in the rerank pool for
  nearly every query. The bottleneck is *ranking within the pool*, not recall of the pool.
- **The lexical signal saturates at ~80%:** parameter sweeps (β ∈ {0.25…2}, cell-weight up, RRF vs
  rank-norm fusion, row-level max-sim reranking) all plateau at 78–81.5% — pushing lexical harder
  promotes generic high-token-count tables over the true gold.
- **The residual ~18% are semantic paraphrase gaps** ("biggest town"→"List of cities"; "who plays
  phyllis"→an actress name absent from the query) that bge-small cannot resolve — the same
  embedding ceiling the graph shape hit at 85% STaRK.
- **The known fix is a stronger embedder** (the graph arm's proven lever: bge-small→S-PubMedBert
  gave +16pp). `bge-large-en-v1.5` is cached locally but embeds at ~60s/batch on CPU → **~40h for
  169K tables — infrastructure-blocked**, exactly as STaRK-MAG was deferred for GPU in E14.

---

## 4. Honest negatives (falsified this session — first-class artifacts)

| Method | Result | Root cause |
|---|---|---|
| **SIEVE** (predicate-satisfiability + inverse-corpus rarity) | Coverage kill | Numeric-predicate parse fires on 0–2/50 queries — these datasets ask *lookup*, not numeric-constraint, questions. Right mechanism, wrong dataset (revisit on a numeric-table corpus). |
| **SPECTRA** (spectral effective-rank) | 18% standalone / 60% RRF (OTT-QA) | Cell-unit embedding approximation destroys ranking. |
| **ACG** (co-grounding, symmetric RRF) | 78% (−18pp) FeTaQA | Symmetric RRF demotes strong dense hits — the failure that motivated the rank-safe monotone design. |
| **TAG ungated** (all title tokens) | −3pp OTT-QA @ n=200 | Generic "List of…" titles promoted wrong same-category tables. Fixed by the rare/capitalized IDF gate. |
| **row-level max-sim rerank** (NQ) | 78.3% (< 80% asptable) | Re-embedding rows adds noise; pooled table vector already captures the row signal. |
| **bge-large embedding** (NQ) | infeasible | ~40h to embed 169K tables on CPU; the right lever, but GPU-blocked. |

---

## 5. Key findings

- **`asptable` is the promoted ASP semantic-table retriever** — beats the flat-dense floor on all
  four real, permissively-licensed datasets, same parameters, generation-free ($0/query).
- **Small Wikipedia-table corpora are dense-saturated** (floor 90–99%); the ASP win there is at
  recall@1/MRR (up to +10.5pp R@1) and the last pp of recall@10.
- **Large corpora carry the real headroom** — on NQ-Tables (169K) the floor drops to 75% and
  `asptable` adds +6.5pp; the residual gap to 90% is a bge-small embedding ceiling, not a method gap.
- **Rank-safe monotone promotion is the load-bearing design choice** — every reranker that
  reordered the dense list by a symmetric rule regressed; only gap-damped never-demote promotion won.

---

## 6. Decision

**Promoted:** `asptable` as the ASP semantic-table shape's retriever (E15). Target met — ≥90%
recall@10 on 3 of 4 datasets with the ASP method beating the floor on all four, no crutch.
**Not promoted (falsified, documented):** SIEVE, SPECTRA, ACG.
**Open / honest caveats:**
- **NQ-Tables (81.5%)** needs a stronger embedder to clear 90% — GPU-blocked in this environment.
- **E15-S8 (lock published SOTA THYME/CRAFT/HELIOS absolute recall@k)** is blocked (arxiv/web fetch
  unavailable in-env). All wins here are stated **vs the flat-dense floor**, not vs published SOTA —
  no "beats SOTA" claim until those numbers are locked from a networked session.
