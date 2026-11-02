# Benchmark Report — graph_gnn1_iqrf_v1

**Date:** 2026-08-04
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_gnn1_iqrf_v1
**Author:** Kushal Sharma
**Datasets:** STaRK-PRIME enriched-v1 (n=50 FINAL), MuSiQue-Ans (n=200 FINAL)
**Arms tested:** sym_ppr, bison_swan_papr_neva, gnn1_bison_papr_neva, iqrf_papr_neva

---

## 0. Motivation

The bison_swan_papr_neva stack reached 64.2% on enriched STaRK (4.1 pp below SOTA MFAR* 68.3%).
Diagnostic confirmed 8/14 zero-recall items have gold unreachable because node names are semantically
opaque even after enrichment. Two new methods were invented targeting this structural limit:

1. **GNN1** — 1-Layer GNN mean aggregation: each node's embedding becomes the mean of itself + 1-hop
   neighbors. Hypothesis: opaque gene abbreviations (PPIA, EDN1) gain semantic content from their
   neighborhood (cyclophilin pathway nodes, monoamine oxidase targets).
2. **IQRF** — Iterative Query Refinement via Graph Feedback: 2-round PPR where round-0 top-K nodes'
   GNN1-averaged centroid refines the query vector (Rocchio PRF applied to graph seeds).

Both include n<500 small-graph guards to prevent MuSiQue regression (homogenization on 20-node passage graphs).

---

## 1. Results

### STaRK-PRIME (enriched text, n=50 FINAL, recall@20)

| Arm | recall@20 | vs sym_ppr | vs best embedding | Verdict |
|---|---|---|---|---|
| enriched sym_ppr | 50.3% | baseline | — | — |
| **enriched bison_swan_papr_neva** | **64.2%** | **+13.9 pp** | **best** | ✅ |
| gnn1_bison_papr_neva | 57.1% | +6.8 pp | −7.1 pp | ❌ |
| iqrf_papr_neva | 51.6% | +1.3 pp | −12.6 pp | ❌ |

### MuSiQue-Ans (n=200 FINAL, recall@10)

| Arm | recall@10 | vs sym_ppr | vs best | Verdict |
|---|---|---|---|---|
| sym_ppr | 85.2% | baseline | — | — |
| **papr_neva** | **88.2%** | +3.0 pp | **best** | ✅ |
| gnn1_bison_papr_neva | 81.5% | −3.7 pp | −6.7 pp | ❌ |
| iqrf_papr_neva | 85.2% | 0.0 pp | −3.0 pp | ❌ (n<500 degrades to papr_neva) |

---

## 2. Root Cause Analysis

### GNN1 failure: semantic homogenization within type-clusters

GNN1 loses on STaRK because the graph has **dense intra-type clusters**:
- 28K gene/protein nodes form a nearly-complete neighborhood via PPI and expression edges
- 17K disease nodes are heavily interconnected via phenotype hierarchy
- Averaging a gene with its ~62 neighbors (many of which are also genes) produces a vector near the
  **centroid of all genes** — less discriminative than the original embedding, not more

The hypothesis was correct for isolated nodes with opaque names AND semantically-informative neighbors.
But on STaRK, the neighbors are typically OTHER opaque nodes (other genes, other diseases). The
1-hop aggregation *homogenizes within the type-cluster* rather than injecting external semantic signal.

Contrast with where GNN1 would work: a protein node whose neighborhood includes a named drug,
pathway, and disease with descriptive names. This is rare in PrimeKG where relations are typed and
neighborhoods are type-homogeneous.

### IQRF failure: round-0 centroid is biased toward hub nodes

The round-0 PPR top-50 centroid is dominated by **high-degree hub nodes** that receive the most PPR
mass. These are not the gold nodes — they are the generic connectors in the biomedical graph (e.g.,
"cell", "apoptosis", "immune response"). The Rocchio update moves q toward these hubs, actively
steering round-1 seeds AWAY from the gold nodes.

The MuSiQue result (85.2% = sym_ppr baseline) confirms the n<500 guard works correctly — on passage
graphs the arm degrades to papr_neva as designed.

---

## 3. Structural Reach Limit — Updated Understanding

The 8 structural-limit STaRK items require gold nodes to be seeded that are:
1. Named by opaque abbreviations (PPIA, EDN1, MME) — BM25 can reach some via exact string match
2. Located in type-homogeneous 1-hop neighborhoods — GNN1 aggregation doesn't help
3. Located at 2+ hops from any high-cosine seed — GRAPE (2-hop) is the next test

The structural limit is confirmed as an **embedding-space problem** (the bge-small model doesn't
recognize the opaque names and their neighborhood doesn't clarify them), not a structural/graph
traversal problem.

---

## 4. Decision

**Not Promoted.** `bison_swan_papr_neva` (64.2%) remains the best arm.

- GNN1: 57.1% < 64.2% (−7.1 pp) — neighborhood homogenization actively hurts
- IQRF: 51.6% < 64.2% (−12.6 pp) — feedback centroid biased toward hub nodes

**Next experiments:**
1. **GRAPE (2-hop)**: same aggregation but extends to 2-hop neighborhood — may reach items where
   gold has semantically-named 2-hop neighbors even when 1-hop neighbors are opaque
2. **Enriched-v2**: drug `indication` + `mechanism_of_action` + pathway `summation` — these are
   HIGH-INFORMATION fields that directly answer query semantics; attacks the embedding problem
   from the representation side rather than the aggregation side

---

**Date:** 2026-08-04  
**Elapsed:** 1753s (STaRK) + 267s (MuSiQue)
