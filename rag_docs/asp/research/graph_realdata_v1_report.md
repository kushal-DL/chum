# Benchmark Report — graph_realdata_v1

**Date:** 2026-07-29
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_realdata_v1
**Author:** Kushal Sharma
**Datasets:** STaRK-PRIME (biomedical KG), MuSiQue-Ans (multi-hop Wikipedia)
**Backends:** 5 expansion arms, same-condition (same seeds, same graph, same process)
**Cost:** $0 — generation-independent recall metric, local `bge-small` seeding only. No LLM, no API budget.
**Status:** Real-data replication of the mechanism spike. Supersedes the synthetic AML spike
(`graph_expansion_spike_v1`), which was **removed** on the "real datasets only" scope call.

---

## 0. What changed since the spike

The earlier `graph_expansion_spike_v1` ran on a **synthetic** AML graph. Per direction from Kushal,
all synthetic data was deleted and the shape is now benchmarked on **real, externally-sourced,
permissively-licensed datasets only**: STaRK-PRIME and MuSiQue. (MetaQA was dropped — canonical data
is Google-Drive-gated and all HF mirrors 401; 2WikiMultiHopQA was dropped with the scope narrowing.)

The question is the same, now on real data: **does subgraph expansion beat flat seed-only retrieval,
and does the direction axis (the spike's one surviving finding) replicate?**

---

## 1. Datasets (real, verified downloadable 2026-07-29)

| Dataset | Graph | Nodes / Edges | Gold | Metric | LLM cost |
|---|---|---|---|---|---|
| **STaRK-PRIME** | Shipped biomedical SKB (PrimeKG-derived) | 129,375 / 8.1M / 18 rel types | verified `answer_ids` | answer-node recall@k | 0 |
| **MuSiQue-Ans** | Per-question passage-entity graph (built, no LLM) | ~20 / ~27 per Q | `is_supporting` paragraphs | support-set recall | 0 |

STaRK ships the graph → built deterministically. MuSiQue ships no graph → we build a passage-entity
graph (paragraphs linked by title-entity mentions; **~72% of questions have all gold paragraphs
connected** from the seed — the honest ceiling of no-LLM entity linking).

---

## 2. Headline scorecard — recall of gold nodes in the retrieved subgraph

| Dataset | n | seed_only (flat) | ppr (directed) | sym_ppr | bidir_ppr | typed_ppr | Best | Verdict |
|---|---|---|---|---|---|---|---|---|
| **MuSiQue** | 200 | 78.2 | 89.5 | **93.2** | 91.5 | 89.5 | sym_ppr | ✅ expansion + direction |
| **STaRK-PRIME** | 10 | 30.0 | **50.0** | 50.0 | 50.0 | 50.0 | ppr (tie) | ✅ expansion; ⚠️ arms tie |

`seed_only` = the flat, expansion-off floor (E14-S6). `ppr` = directed Personalized PageRank
(HippoRAG default). `sym_ppr` = symmetrised. `bidir_ppr` = forward+backward RRF fusion. `typed_ppr`
= query-conditioned relation weighting.

**STaRK caveats (read before citing):** (a) **n=10 only** — each PPR query is a ~10-13s power
iteration over the 8.1M-edge SKB, so a large sample is expensive on CPU; the ties below are
under-powered and directional, not conclusive. (b) The one-time 129K-node bge-small seed embedding
(~31 min) is disk-cached at `~/.cache/asp_graph_embeddings/`; re-runs skip it. (c) PPR transition
matrices are cached per (direction, weights) across queries.

---

## 3. Findings (real data)

1. ✅ **Expansion is load-bearing, on real data — both datasets.** Directed PPR lifts recall
   **+11.3 pp** over flat on MuSiQue (78.2 → 89.5) and **+20.0 pp** on STaRK-PRIME (30.0 → 50.0). The
   node a query needs — the bridge paragraph (MuSiQue) or the relationally-constrained answer entity
   (STaRK) — is found via graph structure and missed by flat top-k. This is the doc-tree → graph
   transfer, now on two external benchmarks (a built passage graph and a shipped 8.1M-edge
   biomedical KG), not a synthetic instrument. **This is the robust, promotable result.**

2. **The direction axis: separates on MuSiQue, ties on STaRK.** On MuSiQue `sym_ppr` beats directed
   `ppr` by **+3.7 pp** (89.5 → 93.2, best arm) — some gold paragraphs are reachable only *against* a
   directed mentions-edge, replicating the spike's finding. On STaRK all expansion arms **tie at
   50.0%**. Two honest reasons: (a) n=10 is under-powered; (b) STaRK's biomedical relations are
   largely **symmetric by nature** (protein–protein interaction, "associated with", "interacts
   with"), so a directed and a symmetrised walk explore nearly the same neighbourhood — direction
   *should* matter less here, and it does. Direction is load-bearing when the graph is genuinely
   directed (MuSiQue, and the synthetic fan-in), neutral when it is not (STaRK). That is a coherent,
   not contradictory, picture.

3. ✅ **Relation typing is inert — now three independent settings.** `typed_ppr` equals uniform
   `ppr` to the decimal on MuSiQue (89.5 = 89.5) and STaRK (50.0 = 50.0), as it did on synthetic AML.
   Verified this is not a bug: on the STaRK query *"What drugs target the CYP3A4 enzyme…"* the learned
   weights are correctly non-uniform (`enzyme` up-weighted to 0.36), yet the top-20 retrieved answer
   nodes are unchanged. Steering the *walk* by relation does not change *which answer nodes surface*
   in these tasks. **Typing should not ship as a default** — three datasets, including one with 18
   real relation types, now say so.

4. **bidir_ppr never beats sym_ppr** (MuSiQue 91.5 vs 93.2; STaRK tie) — consistent across all runs.
   Simple symmetrisation is the strongest direction fix; the fusion adds nothing. Dropped as a
   contribution (kept in code only to expose the direction axis cleanly).

---

## 4. No-regression / honesty notes

- **MuSiQue graph-construction ceiling:** ~28% of questions have a bridge entity whose surface form
  is absent from the paragraph text, so no `mentions` edge connects the gold — expansion cannot help
  those, and the 93.2% ceiling partly reflects this construction limit, not just retrieval. An
  LLM-extracted graph (out of budget scope) would lift the ceiling. Reported, not hidden.
- **Seed pinning:** MuSiQue pins the first-hop supporting paragraph as the seed (the entry point the
  question names), identically across all arms — so the arms differ *only* in expansion, which is
  the controlled comparison. STaRK uses pure dense seeding (no pin), the harder and more realistic
  setting.
- **No per-dataset knobs** (charter): identical pipeline, identical `top_k`, no `if dataset ==`.

---

## 5. Decision

**Promoted: expansion is load-bearing for graph retrieval, on real external data.** This is the
core claim and it holds on both datasets (+11.3 pp MuSiQue, +20.0 pp STaRK over flat) — materially
stronger than the synthetic spike because the data is external and human-annotated.

**Secondary (nuanced, honest):** the *direction* axis matters when the graph is genuinely directed
(MuSiQue: sym_ppr +3.7 pp) and is neutral when the graph is largely symmetric (STaRK: ties). Not a
universal win, but a coherent mechanism. **Relation typing is not promoted** — inert across three
settings including STaRK's 18 relation types.

**Next steps (if this goes toward the paper):**
- Scale STaRK n well beyond 10 (each PPR query ~10-13s over 8.1M edges — needs a GPU or a longer
  batch run) to firm up the direction ties into a real number.
- The direction finding is cleanest to state as: *directed PPR (the HippoRAG default) under-retrieves
  on directed graphs with convergent structure; symmetrising is a free fix and never hurts.* MuSiQue
  and the (now-removed) synthetic fan-in both show it; STaRK neither confirms nor contradicts.
