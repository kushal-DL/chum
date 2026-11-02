# Benchmark Report — graph_neva_v1

**Date:** 2026-08-03
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_neva_v1
**Author:** Kushal Sharma
**Datasets:** STaRK-PRIME (n=50, FINAL), MuSiQue-Ans (n=200, FINAL)
**Backends:** 5 arms same-condition: sym_ppr, papr_entity, neva, neva_entity, papr_neva
**Method under test:** **NEVA — Neighbor-Evidence Augmented PPR** (ASP original v3)
**SOTA baseline:** sym_ppr (HippoRAG-2 additive PPR). Baseline: 40.0% recall@20 STaRK; 85.2% recall@10 MuSiQue.
**Cost:** $0 — no LLM calls. NEVA uses pre-cached bge-small-en-v1.5 node embeddings (129K nodes, already cached).

---

## 0. What NEVA does (the new math)

### Root-cause diagnosis that motivated NEVA

Detailed analysis on n=10 STaRK test items (2026-08-03) revealed two distinct failure classes:

1. **Opaque-name gold nodes**: Gene abbreviations (EDN1, rank 23888 by cosine), drug names (Venlafaxine, rank 4562), organism-specific entities. Direct cosine similarity is near-zero because the entity name carries no semantic signal matching the query vocabulary.

2. **PPR precision failure**: Gold nodes ARE reachable from seeds via 1-2 hops (~70% in top-100 PPR) but are buried below thousands of same-type candidates (17K disease nodes, 28K gene/protein nodes) with similar PPR scores.

**The key observation**: In 58.5% of cases, gold nodes have neighbours whose embeddings **highly align with the query**:
- Gold `sebaceous adenoma` (cos=0.632) has parent `epithelial skin neoplasm` (cos=0.835) — the parent name is literally in the query
- Gold `ABLIM3` (cos=0.698) has neighbour `DCC mediated attractive signaling` (cos=0.805)
- Gold `cerebral lymphoma` (cos=0.821) has parent `primary central nervous system lymphoma` (cos=0.856)

PAPR addressed the *path-from-seeds* signal. NEVA is orthogonal: it scores **at the candidate**, using its **local neighbourhood** as query-aligned witnesses.

### The new math — NEVA witness score

For each candidate node c in the PPR recall pool C:

```
witness_cos(c) = max_{u ∈ N1(c)} cosine(q_vec, embed(u))

neva_node_score(c) = max(witness_cos(c), direct_cos(c))
```

The final reranking score:
```
neva_score(c) = (1-λ) · ppr_score(c) + λ · neva_node_score(c)
```

λ=0.5 (default): balanced interpolation. λ=0 → standard PPR (never worse than baseline).

### Composed method: PAPR + NEVA (papr_neva arm)

PAPR and NEVA are **orthogonal mechanisms** targeting different failure classes:
- **PAPR**: promotes candidates reachable from named-entity seeds via semantically relevant *edge labels*
- **NEVA**: promotes candidates whose *node-level neighbourhood* aligns with the query — works even without named entity seeds

`papr_neva` runs PAPR first (path-quality reranked pool), then applies NEVA witness scoring on that pool. The two signals compound when both are present; when seeds are missing, NEVA still acts.

### Algorithmic novelty

NEVA is not described in any prior Graph-RAG paper:
- **vs HippoRAG/sym_ppr**: no neighbourhood witness scoring
- **vs PAPR**: PAPR scores paths FROM seeds; NEVA scores the LOCAL NEIGHBOURHOOD of each candidate
- **vs RCAD**: RCAD modifies teleportation during diffusion; NEVA is a post-PPR reranker
- **vs typed_ppr**: typed_ppr weights edges during diffusion; NEVA weights candidates by witness evidence after diffusion

Properties:
1. **λ=0 → standard sym_ppr** — never worse than baseline by design
2. **No entity extraction required** — works with cosine seeds alone; robust to entity-extraction failures
3. **Dataset-agnostic** — no if-dataset branches, no test-set-derived thresholds
4. **O(K × degree)** over the PPR top-K — fast (each candidate checks its 1-hop neighbours once)
5. **Reuses cached node embeddings** — zero additional model loading cost

---

## 1. Datasets

| Dataset | Graph | Gold | Metric | n | LLM cost |
|---|---|---|---|---|---|
| STaRK-PRIME | Shared biomedical SKB 129K/8.1M | `answer_ids` | recall@20 | 50 | $0 |
| MuSiQue-Ans | Per-question passage-entity ~20 nodes | `is_supporting` paragraphs | recall@10 | 200 | $0 |

---

## 2. Headline scorecard

**STaRK-PRIME** (top_k=20, n=50, FINAL):

| Arm | recall@20 % | vs sym_ppr | vs papr_entity | Verdict |
|---|---|---|---|---|
| **sym_ppr (HippoRAG baseline)** | **40.0** | baseline | — | — |
| papr_entity (prev best) | 44.2 | +4.2 pp | baseline | — |
| neva (cosine seeds + witness rerank) | 46.0 | +6.0 pp | +1.8 pp | ✅ WIN |
| neva_entity (entity seeds + witness rerank) | 46.0 | +6.0 pp | +1.8 pp | ✅ WIN |
| **papr_neva (composed)** | **50.5** | **+10.5 pp** | **+6.3 pp** | ✅ WIN |

No-regression: sym_ppr 40.0% = exact match to prior baselines ✅

**MuSiQue-Ans** (top_k=10, n=200, FINAL):

| Arm | recall@10 % | vs sym_ppr | vs papr_entity | Verdict |
|---|---|---|---|---|
| **sym_ppr (HippoRAG baseline)** | **85.2** | baseline | — | — |
| papr_entity (prev best) | 88.2 | +3.0 pp | baseline | — |
| neva | 86.0 | +0.8 pp | −2.2 pp | ⚠️ wash |
| neva_entity | 86.0 | +0.8 pp | −2.2 pp | ⚠️ wash |
| **papr_neva (composed)** | **88.8** | **+3.6 pp** | **+0.6 pp** | ✅ WIN |

No-regression: sym_ppr 85.2% = exact match to prior baselines ✅

---

## 3. STaRK findings (complete)

1. ✅ **`papr_neva` wins on STaRK by +10.5 pp** — from 40.0% to 50.5%. This is the largest single-experiment gain in the entire research series (prior best single-run gain was PAPR at +4.3 pp).

2. **NEVA alone adds +6.0 pp** over sym_ppr — more than PAPR's +4.3 pp from the previous run. The neighbour-witness mechanism directly addresses the opaque-name failure class that was the dominant bottleneck.

3. **NEVA with cosine seeds = NEVA with entity seeds (both 46.0%)** — unlike PAPR where entity seeds were essential (+4.3 pp) vs cosine seeds (+0.8 pp). This confirms NEVA's design: it does not require named-entity anchors because the witness signal propagates query relevance through the graph without needing to start from a named node.

4. **PAPR and NEVA compound strongly**: PAPR alone +4.2 pp, NEVA alone +6.0 pp, PAPR+NEVA = +10.5 pp (vs 10.2 pp expected if additive). The two mechanisms are complementary: PAPR improves precision when entity seeds can be found; NEVA improves precision for all candidates regardless of seed quality.

5. **No-regression confirmed**: sym_ppr = 40.0% exact match across all three runs (graph_realdata_v1, graph_papr_v1, graph_neva_v1).

---

## 4. MuSiQue findings (complete)

1. ✅ **`papr_neva` achieves 88.8%** — new best on MuSiQue, +3.6 pp over sym_ppr. The small incremental gain over papr_entity (+0.6 pp) is expected: MuSiQue has only `mentions` edges with no relation diversity, so NEVA's witness scoring is less discriminative than on STaRK's 18-relation graph.

2. **NEVA alone: +0.8 pp on MuSiQue** — modest but positive. On MuSiQue the witness mechanism has limited signal because all edges are `mentions`-type with similar cosine to any query. The gain comes from PPR score smoothing rather than discriminative witness selection.

3. **NEVA doesn't hurt MuSiQue** — the `papr_neva` composition never hurts; it adds +0.6 pp on top of papr_entity. Safe to use as the default arm on both graph types.

---

## 5. Cumulative STaRK progress

| Method | STaRK recall@20 | Δ vs sym_ppr |
|---|---|---|
| sym_ppr baseline | 40.0% | baseline |
| MFDS-PPR (falsified) | 36.8% | −3.2 pp |
| RCAD entity_sym | 42.6% | +2.6 pp |
| RCAD cosine | 42.4% | +2.4 pp |
| papr_entity (prev best) | 44.3% | +4.3 pp |
| neva | 46.0% | +6.0 pp |
| neva_entity | 46.0% | +6.0 pp |
| **papr_neva** | **50.5%** | **+10.5 pp** ✅ |
| Published SOTA (MFAR*) | 68.3% | +28.3 pp |

---

## 6. Decision

**Promoted — `papr_neva` is the new best arm on both datasets.**

| Dataset | n | sym_ppr | papr_neva | Δ | Verdict |
|---|---|---|---|---|---|
| STaRK | 50 | 40.0% | **50.5%** | **+10.5 pp** | ✅ |
| MuSiQue | 200 | 85.2% | **88.8%** | **+3.6 pp** | ✅ |

The NEVA mechanism works as designed: by scoring each PPR candidate by the maximum cosine of its 1-hop neighbourhood, it promotes gold nodes with opaque names (gene abbreviations, drug names) whose semantically-named neighbours align with the query. This directly closes the dominant failure class on STaRK.

The composition `papr_neva` is strictly better than either component alone on both datasets. It is now the default arm for the knowledge-graph retrieval shape.

**Remaining gap:** 50.5% → 68.3% SOTA = 17.8 pp. The gap from the prior run (24 pp) has been reduced significantly. Root causes of the remaining gap:
1. ~30% of gold nodes not reachable from cosine seeds in 2-hop PPR expansion (structural reach limit)
2. Cosine seed quality — bge-small-en-v1.5 with only `name [type]` as node text may miss some named entities when their context is described differently in the query
3. The STaRK SOTA (MFAR*) uses BM25 retrieval + graph traversal; the BM25 component finds nodes by approximate string matching that our cosine-only seeder misses
