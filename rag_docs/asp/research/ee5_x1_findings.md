# Findings — EE5-X1: Dense Seed-Scoring Arm (BM25+dense RRF fusion)

**Date:** 2026-07-28
**Branch:** `experiment/doc-tree-retrieval-redesign`
**Run tag:** `ee5_x1_dense` (retrieval-only recall harness)
**Status:** **Analyzed — NOT PROMOTED (negative result)**
**Related:** [ADR 0001](../adr/0001-reject-min-context-floor-adopt-recall-datasets.md), [`hierarchical_datasets_findings.md`](hierarchical_datasets_findings.md)

---

## Hypothesis (one sentence)

> ASP's document-tree retriever loses ObliQA recall at the *seed-scoring* stage
> because its seed scorer is lexical-only BM25, while the flat baseline it beats
> already has a dense arm; giving ASP a dense (bge-small cosine) seed arm fused
> with BM25 via RRF will recover the vocabulary-mismatch misses and lift recall
> toward 90%.

**Property added:** semantic (embedding-space) recall at seed selection, so a
gold passage that shares little surface lexical overlap with the question can
still enter the seed pool.

---

## Why this was the highest-value swing (the diagnosis that motivated it)

Before writing any code, the ObliQA `rr_notokenfloor` checkpoint was
instrumented to locate *where* recall is lost. For every gold passage, its rank
was traced through BM25 → tree-traversal → cross-encoder rerank → bundle
assembly (n=200 sampled):

| Stage where gold was lost | Count |
|---|---:|
| Never scored by BM25 (zero lexical overlap) | 7 |
| Scored but ranked below the top-20 seed cutoff | 64 |
| Dropped by the cross-encoder reranker | **0** |
| Dropped by the bundle assembler (budget) | **0** |

**The reranker and bundle assembler drop nothing.** 100% of the recall loss is
that BM25 never surfaces the gold into the 20-node seed pool. And ASP's seed
scorer ([`bm25.py`](../../src/asp/benchmarks/retrieval/bm25.py)) is lexical-only,
while the flat baseline ([`hybrid.py`](../../src/asp/benchmarks/retrieval/hybrid.py))
runs `bge-small` + RRF. ASP was winning **+38 pp with a strictly weaker seed
scorer** — so adding the dense arm looked like removing a self-imposed handicap,
not a thumb on the scale.

Gold BM25-rank recoverability (why top-k alone can't fix it):

| gold in BM25 top-k | fraction |
|---|---:|
| top-1 | 43.6% |
| top-6 | 61.7% |
| top-20 | 73.3% |
| top-50 | 81.6% |

---

## Method

- **Mechanism.** New module
  [`dense_node_scorer.py`](../../src/asp/benchmarks/retrieval/dense_node_scorer.py):
  `score_nodes_dense()` embeds every node with `BAAI/bge-small-en-v1.5` (the
  *same* model as the flat baseline — deliberately not a stronger one, to avoid
  confounding the comparison) and returns cosine-to-query; `fuse_rrf()` combines
  the BM25 and dense rankings with reciprocal-rank fusion (k=60, rank-based so no
  scale normalisation or weight tuning — no new dataset-specific knob).
- **Seed-stage only.** The fused score drives round-robin seed selection *only*.
  Tree-traversal sibling/ancestor thresholds keep raw BM25 scores, so the arm
  changes *which seeds enter the pool* and nothing downstream.
- **Opt-in.** `DocumentTreeRAGPipeline(dense_arm=False)` by default; CLI
  `--dense-arm`. Per the layered-API design, the dense arm is an *internal* stage
  of the tree retriever (like round-robin seeding), not a user-selected mode —
  the flag exists for ablation, not production dispatch.
- **Measurement.** ObliQA recall is generation-independent (computed on the
  assembled context, identical across models — confirmed in
  `hierarchical_datasets_findings.md`). Measured via a retrieval-only harness
  ([`ee5_x1_recall_harness.py`](../../src/asp/benchmarks/ee5_x1_recall_harness.py))
  — no LLM calls. The baseline arm reproduced the published **76.09%** to the
  decimal, validating the harness before trusting the delta.
- **n = 500**, same items as `rr_notokenfloor`. Flat not re-run — its 37.9% recall
  is generation-independent and unaffected by ASP retriever changes (fixed
  comparator already on record).

---

## Results

| Metric | Baseline (BM25) | Dense (BM25+RRF) | Δ |
|---|---:|---:|---:|
| **avg span recall** | 76.09% | 76.39% | **+0.30 pp** |
| avg token recall | 89.73% | 89.80% | +0.07 pp |
| full recall (all gold found) | 69.6% | 69.4% | −0.20 pp |
| single-target recall (n=381) | 85.83% | 86.09% | +0.26 pp |
| multi-target recall (n=119) | 44.92% | 45.34% | +0.42 pp |

**The dense arm is a no-op on ObliQA recall (+0.30 pp, within noise).** The
vocabulary-mismatch hypothesis is **falsified at scale.** (A 30-item smoke test
had shown +3.33 pp — a small-sample artifact that did not survive n=500. Recorded
as a caution against trusting sub-100-item deltas.)

Result files:
- `data/benchmarks/obliqa_ee5_x1_dense_recall.json`
- `data/benchmarks/ee5_x1_diagnosis.json`

---

## Root cause — why the dense arm failed

For the gold passages BM25 buried (rank ≥ 20), where does the *dense* arm rank
them? (n=150 sampled):

| | value |
|---|---:|
| BM25-buried gold passages | 53 |
| …that dense recovers to top-20 | 17 (**32.1%**) |
| …that dense **also** buries | 36 (**67.9%**) |
| dense median rank of buried gold | **75** |
| RRF conservatism loss (dense found but fusion dropped) | **0** |

Two clean facts:

1. **RRF is not the bottleneck.** Fusion surfaced *exactly* what the dense arm
   found (17 = 17) — no fusion-weighting problem, so tuning RRF/adding weights
   would not help.
2. **Dense embeddings bury 68% of the same gold BM25 buries** (median dense rank
   75 of ~500 nodes). The misses are **not** vocabulary mismatch — if they were,
   dense would find them. They are **multi-target dispersion**: these gold
   passages only make sense *as a set* (the other clauses of a multi-part
   obligation), and individually resemble neither the question's keywords *nor*
   its semantics. No single-vector similarity — lexical or dense — ranks a
   passage that isn't independently query-like.

This aligns with the earlier co-location analysis: **64.5% of multi-gold passages
are structurally far apart** in the tree (not siblings/cousins), and recall
collapses by gold count (single 85.8% → gold=2 47.9% → gold=4 15.6%). The
ObliQA ceiling is a **multi-target set-retrieval problem**, not a scoring-signal
problem.

---

## No-regression

Guaranteed **structurally**: `dense_arm` defaults to `False`, so CUAD,
GovReport-QS, and the ObliQA baseline paths are byte-identical to their published
runs (verified: default off, `fuse_rrf` deterministic). No LLM re-run needed — a
disabled code path cannot regress. (The real CUAD corpus is additionally not
loadable in the current offline environment; a live CUAD EM re-run with the arm
*on* is deferred to an environment where the corpus is available, but is not
required for no-regression since the arm is off by default.)

---

## Decision

**NOT PROMOTED.** Negative result, first-class per the charter. The dense seed
arm is retained as an **off-by-default ablation flag** (`--dense-arm`) — it is
correct engineering (parity with flat's retriever, ~zero cost when a query *is*
semantic), and it may matter on a future corpus whose misses genuinely are
vocabulary mismatch. It simply does not move ObliQA, because ObliQA's residual
loss is not vocabulary mismatch.

**What this falsifies:** "ASP's recall gap is a weak (lexical-only) seed scorer."
It is not. BM25 and dense agree on what they can't find.

**What it points to (the real next experiment).** The ceiling is **multi-target
set coverage**, confirmed twice (co-location + dense-also-buries). The candidate
mechanisms that attack *that*, none of which is a per-dataset knob:

1. **Query decomposition / multi-vector seeding** — split a multi-part question
   into sub-queries, seed each independently, union the pools. Directly targets
   the "gold only makes sense as a set" failure a single query vector can't.
2. **Coverage-maximising bundle selection** (submodular / MMR over facets)
   instead of top-k-by-score — stop over-sampling the one strong facet and
   allocate seed/bundle slots across distinct facets. This is the queued **EE5 —
   Two-Pass Coverage Stop** direction, now with a measured mandate.

Recommendation: EE5-X2 = query decomposition + facet-coverage selection, measured
the same way (retrieval-only recall, single vs multi-target subsets). The honest
expectation: this can lift the **multi-target** subset (currently 45%), which is
where the ObliQA ceiling actually lives; single-target is already 86% and near
its achievable limit.
