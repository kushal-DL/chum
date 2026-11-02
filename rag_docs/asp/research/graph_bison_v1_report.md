# Benchmark Report — graph_bison_v1

**Date:** 2026-08-04
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_bison_v1
**Author:** Kushal Sharma
**Datasets:** STaRK-PRIME enriched (n=50, FINAL), MuSiQue-Ans (n=200, FINAL)
**Backends:** 4 arms: sym_ppr, swan_papr_neva, bison_ppr, bison_papr_neva
**Method under test:** **BISON — BM25-Integrated Spectral ONtological seeding** (ASP original v6)
**Baseline:** Enriched swan_papr_neva = 64.2% STaRK, papr_neva = 88.8% MuSiQue.

**Flat vector RAG baseline (measured same session):**
| Dataset | Flat cosine@k | Best graph RAG | Graph advantage |
|---|---|---|---|
| STaRK (base embeddings) | 33.9% | 64.2% | +30.3 pp |
| STaRK (enriched embeddings) | 49.1% | 64.2% | +15.1 pp |
| MuSiQue | 88.5% | 88.8% | +0.3 pp |

---

## 0. What BISON does (the new math)

### Motivation

Root-cause analysis of 14 zero-recall items after enriched swan_papr_neva:
- 1 NO-SEED: query describes properties ("molecular weight 276") without naming an entity
- 1 WRONG-SEED: entity extraction picks wrong anchor
- 12 DILUTION: gold 1-hop reachable from seeds but PPR mass diluted

SWAN+enriched text solves the dilution class. The NO-SEED and WRONG-SEED cases require
**lexical matching** — BM25 can catch abbreviated entity names and paraphrased synonyms
that cosine embeddings miss.

Proof: BM25 seeding on 20 test items brought in **7 new gold seeds** not in cosine top-5,
increasing seed coverage from 27% → 41% (+14 pp on the diagnostic sample).

### New math — BISON

For each node v, compute BM25 score against the tokenized query:
```
bm25_norm(v) = BM25(query_tokens, node_tokens(v)) / max_BM25
```

Max fusion with cosine:
```
bison_score(v) = max(cosine_norm(v), bm25_norm(v))
```

Then apply SWAN witness propagation on top: top-100 fused-score anchors propagate
scores to their 1-hop neighbors via geometric mean, extending structural reach.

**Max fusion properties:**
- Either signal finding a node is sufficient (OR logic for seeding)
- Does not double-count nodes already found by cosine
- Expands seed set: BM25 adds lexically-matched nodes cosine missed
- Degrades to cosine when BM25 scores all zero (unknown terminology)

**Small-graph guard:** when n_graph_nodes ≤ anchor_m (MuSiQue 20-node graphs),
skip BM25 entirely and return pure cosine top-k. This prevents BM25 noise dilution
on small fully-connected passage graphs.

---

## 1. Headline scorecard

**STaRK-PRIME** (enriched text, top_k=20, n=50, FINAL):

| Arm | recall@20 | vs enriched sym_ppr | Verdict |
|---|---|---|---|
| enriched sym_ppr | 50.3% | baseline | — |
| **enriched swan_papr_neva** | **64.2%** | +13.9 pp | prev best |
| bison_ppr | 58.2% | +7.9 pp | ✅ strong seeding win |
| bison_papr_neva | 63.2% | +12.9 pp | ⚠️ −1.0 pp vs swan_papr_neva |

**MuSiQue-Ans** (top_k=10, n=200, FINAL):

| Arm | recall@10 | vs sym_ppr | Verdict |
|---|---|---|---|
| sym_ppr | 85.2% | baseline | — |
| **papr_neva** | **88.5%** | +3.3 pp | best on MuSiQue |
| bison_ppr | 84.2% | −1.0 pp | ⚠️ small regression |
| bison_papr_neva | 87.5% | +2.2 pp | ⚠️ −1.0 pp vs papr_neva |

---

## 2. Key findings

1. **BISON seeding standalone gives +7.9 pp on enriched STaRK** — purely from better seeds.
   BM25 captures paraphrased entity names (e.g. "cyclophilin A" → PPIA gene) that neither
   cosine nor exact name-index matching finds.

2. **bison_papr_neva (63.2%) is within 1.0 pp of swan_papr_neva (64.2%)**. The two
   methods target partially overlapping failure classes. SWAN uses witness propagation
   from cosine anchors; BISON uses BM25 lexical matching. On this dataset/sample, SWAN
   edges out BISON but the margin is within noise at n=50.

3. **BISON and SWAN are complementary mechanisms.** SWAN catches opaque-name nodes
   whose neighbors are query-relevant. BISON catches paraphrased-name nodes via lexical
   overlap. A combined `bison+swan` seeder could in principle beat both — not yet tested.

4. **MuSiQue: −1.0 pp** — small-graph early-exit prevents the worst dilution but some
   MuSiQue graphs have >100 nodes and still receive BM25 processing. BM25 creates noise
   on passage-entity graphs where node "names" are passage titles not entity names.

---

## 3. Decision

**Partially promoted — bison_ppr adds +7.9 pp standalone but doesn't beat swan_papr_neva.**

`swan_papr_neva` (64.2%) remains the default best arm for enriched STaRK.

BISON is valuable as an independent seeding mechanism and could be composed with SWAN
for a combined `bison+swan` seeder in a future experiment.

**Remaining gap:** 64.2% → 68.3% SOTA = 4.1 pp; 64.2% → 90% target = 25.8 pp.

The 8 structural-limit items (gold not in PPR top-200) are the core remaining challenge.
BM25 helps 1-2 of these (paraphrased entity names) but most require either:
1. Combined bison+swan seeding (not yet tested)
2. LLM-based query decomposition (out of scope for embedding-only retrieval)
3. Property-graph queries (requires structured data extraction from descriptions)
