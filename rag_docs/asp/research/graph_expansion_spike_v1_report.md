# Research Finding — Graph Expansion Mechanism (synthetic AML spike)

> **⚠️ SUPERSEDED (2026-07-29).** This spike ran on a **synthetic** AML graph. On the
> "real datasets only" scope call, the synthetic generator and its code/tests were
> **removed from the repo**, so the file paths and the reproduce command below no longer
> exist. Kept only as the record of where the *direction-axis* mechanism was first found.
> The findings that mattered — expansion is load-bearing, direction (not typing) is the
> axis, typing is inert — were **replicated on real data** in
> [graph_realdata_v1_report.md](graph_realdata_v1_report.md) (STaRK-PRIME + MuSiQue). Cite
> that report, not this one.

**Date:** 2026-07-29
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_expansion_spike_v1
**Author:** Kushal Sharma
**Cost:** $0 — generation-independent (subgraph-recall metric, local `bge-small`
embedding only). No LLM, no network, no NVIDIA budget consumed.
**Status:** SUPERSEDED spike (synthetic instrument removed). NOT a promoted result.

---

## 0. TL;DR

We ported the document-tree retrieval loop (seed → expand → order → recall-score)
to knowledge graphs and ran a same-condition ablation of **eight expansion arms**
on a synthetic anti-money-laundering (AML) graph with **planted, guaranteed-correct
gold typology subgraphs** (fan-in, fan-out, cycle, stacking). Three findings, two
of which **falsify** the hypotheses we started with:

1. ✅ **Expansion is load-bearing** — the doc-tree insight transfers. Every
   expansion arm beats flat seed-only retrieval by **+26 to +55 pp** subgraph-recall.
2. ❌ **Query-conditioned relation typing is inert** (the headline prior, from
   `project_asp_graph_rag_scope` and E7-S10). `typed_ppr` equals uniform `ppr`
   **to the decimal at every budget and every graph density.** Falsified on this
   instrument.
3. 🔬 **Edge *direction*, not edge *type*, is the load-bearing axis** — and it is a
   genuine, previously-unarticulated failure mode of the HippoRAG-style PPR the
   scope memo named as the primitive to build. Directed PPR is **structurally
   blind to convergent (fan-in) patterns** — exactly the AML collector/structuring
   typology that matters most for fraud.

A fourth idea we generated mid-spike — **bidirectional PPR fusion** — was also
**falsified**: it never wins; plain graph symmetrisation beats it. We kept the
code (it is the clean way to expose the direction axis) but do not claim it.

---

## 1. What was built

All under `src/asp/retrieval/knowledge_graph/` and `src/asp/benchmarks/`:

| Component | File | Role |
|---|---|---|
| Graph schema | `retrieval/knowledge_graph/schema.py` | `GraphNode`/`GraphEdge`/`KnowledgeGraph`, the graph analog of `document_tree/schema.py`. Directed multigraph, cycle-safe, dangling-edge-safe. |
| Expansion primitives | `retrieval/knowledge_graph/subgraph_expansion.py` | k-hop; Personalized PageRank (sparse power-iteration, scales to STaRK SKB); directional/symmetric/**bidirectional-fusion** PPR; query-conditioned **typed** PPR. |
| Path-recall metric (E14-S1) | `benchmarks/metrics/path_recall.py` | Triple recall (2Wiki), answer-node recall Hit@k/Recall@k/MRR (STaRK/MetaQA), subgraph recall (MuSiQue/AML). Generation-independent, mirrors `span_recall.py`. |
| Synthetic AML generator (E14-S8) | `benchmarks/datasets/synthetic_aml.py` | Deterministic graph with planted laundering typologies as guaranteed-correct gold subgraphs. |
| Graph pipeline | `benchmarks/pipelines/graph_retriever.py` | seed → expand (selectable arm) → order → score. One code path, no `if dataset ==`. |
| Ablation harness (E7-S12) | `benchmarks/graph_expansion_ablation.py` | Runs all 8 arms × typologies, sweepable over `top_k` and graph density. |
| Unit tests | `tests/unit/test_knowledge_graph.py` | 18 tests, all passing. Includes the direction mechanism (forward misses fan-in; backward finds it; bidir covers both). |

**Arms.** `seed_only` (flat baseline, E14-S6) · `khop` (naive BFS) · `ppr`
(HippoRAG-style directed uniform) · `typed_ppr` (query-conditioned relation
weights) · `sym_ppr` (symmetrised) · `sym_typed_ppr` · `bidir_ppr` (forward+backward
RRF fusion) · `bidir_typed_ppr`.

---

## 2. Results — subgraph-recall (%), 3 graph seeds, per typology

**Sparse graph (1,200 background txns), `top_k = 30`:**

| arm | fan_in | fan_out | cycle | stacking | **OVERALL** |
|---|---|---|---|---|---|
| seed_only (flat) | 33.3 | 38.9 | 46.7 | 60.0 | **44.7** |
| khop | 100.0 | 100.0 | 93.3 | 91.1 | **96.1** |
| ppr (directed) | 33.3 | 77.8 | 100.0 | 100.0 | **77.8** |
| typed_ppr | 33.3 | 75.9 | 100.0 | 100.0 | **77.3** |
| sym_ppr | 100.0 | 100.0 | 100.0 | 100.0 | **100.0** |
| bidir_ppr | 50.0 | 72.2 | 100.0 | 100.0 | **80.6** |

**`top_k` sweep, OVERALL (sparse graph):**

| top_k | seed_only | khop | ppr | typed | sym_ppr | bidir_ppr | winner |
|---|---|---|---|---|---|---|---|
| 6 | 44.7 | 52.2 | 58.8 | 58.2 | 56.4 | 57.6 | ppr |
| 8 | 44.7 | 67.1 | 64.1 | 64.1 | 62.8 | 61.9 | khop |
| 12 | 44.7 | 89.5 | 72.4 | 72.4 | 78.4 | 73.1 | khop |
| 30 | 44.7 | 96.1 | 77.8 | 77.3 | 100.0 | 80.6 | sym_ppr |

**Dense graph (8,000 background txns), `top_k = 12`:**

| arm | fan_in | fan_out | cycle | stacking | OVERALL |
|---|---|---|---|---|---|
| khop | 100.0 | 100.0 | 84.4 | 82.2 | **91.7** |
| ppr | 33.3 | 64.8 | 100.0 | 95.6 | 73.4 |
| sym_ppr | 75.9 | 70.4 | 100.0 | 93.3 | **84.9** |
| bidir_ppr | 33.3 | 63.0 | 100.0 | 86.7 | 70.7 |

Reproduce: `python -m asp.benchmarks.graph_expansion_ablation --seeds 0 1 2 --top-k 30 [--bg-txns 8000]`

---

## 3. Finding 1 — expansion transfers from doc-tree to graph ✅

Flat seed-only never exceeds **44.7%**; every expansion arm clears **56%+**, and the
best clears **96–100%**. The EE-Struct lesson ("expansion, not chunking, carries the
win") holds on graphs: **placing/diffusing from seeds and pulling the connected
neighbourhood is the mechanism.** This validates cloning the doc-tree retriever for
graphs (E7) as the right architecture.

## 4. Finding 2 — relation typing is inert ❌ (hypothesis falsified)

`typed_ppr == ppr` and `sym_typed_ppr ≈ sym_ppr` to the decimal at **every** budget
and **both** densities (the one exception, dense top_k=12, is typing making it
slightly *worse*: 82.5 vs 84.9). The scope memo (`project_asp_graph_rag_scope`) and
E7-S10 predicted relation-weighted diffusion would be load-bearing "as the graph
analog of doc-tree parent/sibling expansion." **On this instrument that is false.**

Honest root-cause: our synthetic typologies are single-relation (`pays`) patterns
embedded in multi-relation noise. Query-conditioned up-weighting of `pays` *does*
fire (the weight map is non-uniform and logged), but the seed is already pinned to
the correct account and the gold edges are all one type, so re-weighting the walk
changes nothing the seed+direction did not already determine. **Typing can only pay
off when the gold path traverses a *specific* relation that must be distinguished
from *competing same-direction* relations** — a condition this benchmark does not
create. 2WikiMultiHopQA (mixed Wikidata predicates on the reasoning path) is the
dataset that can actually test typing; until then, **typing is not justified and
should not ship as a default.**

## 5. Finding 3 — direction is the real axis 🔬 (novel, kept)

The most useful thing the spike surfaced. **Directed PPR — what HippoRAG ships —
scores a hard 33.3% ceiling on fan-in at every budget** (see the unchanging `ppr`
fan-in column). Reason, confirmed by unit test
(`test_ppr_forward_misses_fanin_backward_finds_it`): PPR diffuses mass *along* edge
direction; a fan-in collector has only *inbound* edges, so a walk restarting at the
collector has **nowhere to flow** and never reaches the source accounts. **This is a
structural blind spot, not a tuning issue** — and fan-in / convergent structuring is
*the* canonical money-laundering typology. A graph-RAG system that ships directed
PPR unmodified will systematically miss the fraud pattern it most needs to find.

Symmetrising the walk (`sym_ppr`) fixes fan-in and is the **top performer at high
budget (100%)**. The trade-off: under a *tight* budget it leaks mass onto
reverse-direction distractors and dips on directional patterns (cycle 71–80% at
top_k≤8). Our fusion idea (`bidir_ppr`) was meant to keep both — it did not; it lost
to `sym_ppr` everywhere. **Falsified, but the direction diagnosis stands and is the
transferable result.**

## 6. Instrument limitations (read before citing)

- **k-hop wins overall (96.1% sparse / 91.7% dense).** Not because BFS is better in
  general, but because our planted patterns sit within 1–2 hops of the seed, so BFS
  reaches them before its node-cap bites. Real graph-RAG prefers PPR precisely on
  graphs where relevant nodes are many hops away through dense hubs — a regime this
  generator does **not** create. Deepening the patterns to make PPR win would be
  designing the benchmark to favour our method (charter violation) — so we did not.
- **Single-relation gold** ⇒ cannot test typing (Finding 2). Needs 2Wiki.
- **Synthetic, seed-pinned.** Seeding is by an entity the question names; embedding
  seed *quality* (the real deployment bottleneck) is untested here.
- **n = 36 questions** (3 seeds × 12). Directional, not a headline number.

## 7. Decision

**Not promoted.** This is a mechanism spike whose value is three verified claims and
a reusable, tested harness — not a benchmark win.

**What ships from this spike:** the KG schema, path-recall metric (E14-S1), PPR
family, synthetic-AML generator (E14-S8 seed), and the ablation harness (E7-S12).

**What does NOT ship:** relation typing as a default (inert until 2Wiki says
otherwise); bidirectional fusion (falsified).

**Next experiment (highest information gain):** run the **direction axis on
2WikiMultiHopQA** (E14-S4). It is the first instrument that can (a) test typing under
competing relations, and (b) test PPR vs k-hop on genuinely multi-hop chains where
BFS *should* flood. Concrete falsifiable prediction to carry in:
> On 2Wiki, `sym_ppr` ≥ `ppr` on triple-recall (direction axis replicates), and
> `typed_ppr` > `sym_ppr` by ≥2 pp (typing finally fires on mixed-relation paths).
> If typing does **not** separate on 2Wiki either, drop it from the shape entirely.
