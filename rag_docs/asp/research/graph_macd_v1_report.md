# Benchmark Report — graph_macd_v1

**Date:** 2026-07-30
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_macd_v1
**Author:** Kushal Sharma
**Datasets:** STaRK-PRIME (biomedical KG), MuSiQue-Ans (multi-hop Wikipedia)
**Backends:** expansion arms, same-condition (same seeds, same graph, same process)
**Method under test:** **MACD — Multi-Anchor Conjunctive Diffusion** (new), vs the additive-PPR SOTA
**SOTA baseline:** HippoRAG-2-style additive Personalized PageRank (our `sym_ppr` arm: single summed
restart vector). Multi-hop-QA SOTA per literature = HippoRAG 2 (arXiv:2502.14802); STaRK-PRIME
retrieval SOTA = MFAR* (Hit@1 40.9 / Recall@20 68.3).
**Cost:** no-LLM arms = $0 (recall metric, local `bge-small` seeding). `macd_llm` spends ≤1 cached
LLM call per unique query, within the **40 req/min per family** budget.

---

## 0. What was tested and why

HippoRAG (the current best training-free graph retriever) seeds one Personalized PageRank walk from
a **single restart vector that sums all query anchors**. Because PageRank is linear,
`π(Σ_a r_a) = Σ_a π(r_a)` — the combined score is the **sum** of the per-anchor diffusions. Summation
is a logical **OR**: a node ranks high if it is near *any* anchor. A compositional query ("a drug
that targets CYP3A4 **and** treats strongyloidiasis") needs the answer near **all** anchors — an
**AND**. Linear PPR cannot express AND.

**MACD** replaces the implicit sum with a tunable **power mean** over *separate* per-anchor
diffusions: `score_ρ(v) = ((1/K) Σ_a p_a(v)^ρ)^(1/ρ)`. ρ=1 = arithmetic mean = OR = HippoRAG
exactly; ρ→0 = geometric mean = AND; ρ=−1 = harmonic = hard AND. Same fixed configuration on both
datasets — **no per-dataset tuning** (charter rule). The `macd_arith` (ρ=1) arm isolates the fusion
operator: any MACD win must come from AND-vs-OR, not from a different pipeline.

**Prior art (cited honestly — the mechanism is NOT new):** Center-Piece Subgraphs (Tong & Faloutsos,
KDD 2006) already runs per-source random-walk-with-restart and fuses them multiplicatively with an
OR→softAND→AND family. Query2Box (ICLR 2020) answers conjunctive queries by intersection in
embedding space. MACD's only candidate novelty is *porting conjunctive multi-anchor diffusion into
open-IE Graph-RAG as a replacement for HippoRAG's summed PPR seed* — and the results below do **not**
support even that narrow claim in its no-LLM form.

---

## 1. Headline scorecard — recall of gold nodes in the retrieved subgraph

**STaRK-PRIME** (shared 8.1M-edge biomedical KG; answer-node recall@20; n=50):

| Arm | recall@20 % | vs baseline | Verdict |
|---|---|---|---|
| seed_only (flat floor) | 16.9 | — | — |
| **sym_ppr (HippoRAG, additive PPR)** | **40.0** | baseline | — |
| macd (AND, no-LLM anchors, ρ=0) | 32.0 | **−8.0 pp** | ❌ loses |
| macd_arith (OR, no-LLM anchors, ρ=1) | 38.0 | −2.0 pp | ❌ loses |
| macd_llm (AND, LLM anchors, ρ=0) | 11.0 | **−29.0 pp** | ❌ loses hard |

**MuSiQue** (per-question ~20-node passage graph; support-set recall at honest tight budgets; n=120):

| Arm | recall@3 | recall@5 | recall@10 |
|---|---|---|---|
| seed_only (flat) | 66.2 | 76.7 | 76.7 |
| ppr (directed) | 59.2 | 76.2 | 87.1 |
| **sym_ppr (HippoRAG)** | 57.9 | 71.2 | 82.9 |
| macd (AND, ρ=0) | 62.9 | 72.9 | 83.8 |
| macd_arith (OR, ρ=1) | 63.3 | 74.6 | 85.4 |

> **Retracted artifact (documented, not hidden):** an earlier MuSiQue run reported MACD at
> **100.0% recall@20**, a **+6.8 pp** "win" over sym_ppr. That number is **invalid**: MuSiQue graphs
> average 20 nodes and the budget was top_k=20, so MACD was returning the *entire graph* (mean 20.0
> nodes retrieved) — recall is trivially 100% for any arm that returns everything. The table above
> re-measures at **k=3/5/10 < graph size**, where ranking actually decides. This is exactly the kind
> of budget artifact that must never be reported as a method win.

---

## 2. Findings (honest)

1. ❌ **No-LLM MACD is falsified.** On STaRK it *loses* to the additive-PPR baseline by −8.0 pp; on
   MuSiQue at honest budgets it is a wash-to-marginal (−1.7 to +5.0 pp depending on k, no consistent
   sign). The core conjunctive claim — AND beats OR — is **not supported** by the no-LLM arms.

2. ❌ **AND underperforms OR in the no-LLM setting** (`macd` < `macd_arith` on STaRK by −6.0 pp;
   ≈ wash on MuSiQue). This is the crux. The unit tests prove AND correctly lifts a node reachable
   from *genuinely distinct* anchors (the diamond-graph test passes). The failure is therefore in
   **anchor construction**, not the fusion math: the no-LLM arm takes the top-3 *dense hits* as
   "anchors", but on STaRK those three hits are near-duplicates landing in the **same** graph region
   (all similar to the full query string), not distinct entities. Intersecting three copies of one
   anchor narrows onto that region and **crushes the relationally-distant answer nodes** that only
   one constraint reaches — precisely why AND < OR here.

3. **MuSiQue's gold is a hop-chain, not an all-near-all-anchors set.** MuSiQue answers require a
   *sequence* p1→p2→p3, so the conjunctive assumption (answer close to every anchor simultaneously)
   does not match its structure — expansion/direction (the prior finding) matters more than fusion.
   STaRK's compositional constraints are where AND *should* pay off, and only with **distinct**
   anchors — which is what `macd_llm` tests (§3, pending).

4. **The retracted 100% is the headline honesty lesson:** recall at a budget ≥ graph size is
   meaningless. All MuSiQue numbers are now reported at k < graph size.

---

## 3. macd_llm (LLM anchor decomposition) — the hypothesis is fully falsified

The final test: give MACD **genuinely distinct** anchors by having the LLM split each query into its
entity mentions (verified working: *"What drugs target CYP3A4 and treat strongyloidiasis?"* →
`["CYP3A4", "strongyloidiasis", …]`), then require the answer near **all** of them (ρ=0, hard AND).
Result: **macd_llm = 11.0% recall@20, −29.0 pp below the baseline** — dramatically *worse* than even
the dense-anchor MACD.

This is not a seeding artifact — it is the mechanism being wrong. With distinct anchors and a strict
conjunction, almost every candidate is crushed, because **STaRK answer nodes are typically strongly
connected to *one* constraint, not to all constraints simultaneously.** The conjunction
over-restricts: demanding non-trivial diffusion mass from *every* separate anchor eliminates the true
answers, which sit close to the drug/gene constraint but far (in diffusion terms) from the disease
constraint. The additive OR fusion, by contrast, lets a node qualify on its strongest connection —
which is what STaRK's gold actually rewards. **Better anchors made a worse method worse, confirming
the fusion operator — not anchor quality — is the problem.**

---

## 4. No per-dataset crutch (charter compliance)

Identical MACD configuration on both datasets: geometric-mean fusion (ρ=0 for `macd`),
`num_anchors=3`, `per_anchor_seeds=2`, `symmetric=True`, `top_seeds=5`. No `if dataset ==` branching;
no ρ tuned per dataset. The ρ-sweep (`macd`/`macd_arith`/`macd_harm`) is an ablation of the *method*,
reported in full, not a dataset-specific knob.

---

## 5. Decision

**NOT promoted — MACD (Multi-Anchor Conjunctive Diffusion) is falsified on both datasets.** The
additive-PPR baseline (`sym_ppr`, our HippoRAG stand-in) remains the best arm everywhere: STaRK 40.0%
vs MACD 32.0% (no-LLM) / 11.0% (LLM); MuSiQue wash-to-negative at honest budgets. Every MACD variant
— no-LLM AND, no-LLM OR, LLM AND — loses to the baseline on STaRK.

**Why the idea failed (the transferable finding):** the compositional-query intuition ("answer is
near ALL anchors") does not hold on these graphs. On STaRK the gold answer is strongly linked to
*one* constraint and only loosely to the others; on MuSiQue the gold is a *hop-chain*, not an
all-near-all set. In both, a logical **OR** (additive PPR) is the correct fusion and a strict
**AND** (conjunctive diffusion) actively destroys recall. The harder the AND (macd_llm), the worse
the result — a clean monotone falsification.

**Honesty ledger:**
- The mechanism was never novel (CEPS 2006, Query2Box 2020) — so a negative result costs no
  novelty claim; there was nothing to salvage by spinning it.
- The retracted MuSiQue 100% is documented as a budget artifact (§1), not reported as a win.
- No numbers tuned or cherry-picked; the losing arms are reported at full weight.

**Next experiment (highest information gain):** abandon conjunctive fusion. The one promoted graph
finding remains the **direction axis** (symmetrised PPR ≥ directed PPR;
`graph_realdata_v1_report.md`). To actually beat HippoRAG on STaRK, the literature points at
**learned dense+structural fusion (MFAR*/MoR, the STaRK-PRIME SOTA)** and **query-to-triple linking
(HippoRAG 2)** — i.e. improve *seeding/linking*, which is where STaRK's ceiling actually sits, rather
than the diffusion fusion operator. That is the next method to design and test.
