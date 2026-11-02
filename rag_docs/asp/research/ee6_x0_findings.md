# Findings — EE6-X0: Schema-Induction Feasibility Probe (GATE — FAILED)

**Date:** 2026-07-28
**Branch:** `experiment/doc-tree-retrieval-redesign`
**Status:** **Gate FAILED — EE6-X1 full build NOT started (as pre-registered)**
**Probe:** `scripts/ee6_x0_schema_probe.py` · data: `data/benchmarks/ee6_x0_schema_probe.json`
**Related:** [EE6 charter](../../experiment-backlog/epic-ee6-requirement-coverage.md), [multitarget_ceiling_diagnosis.md](multitarget_ceiling_diagnosis.md)

---

## Pre-registered question

Can a requirement schema induced from the question alone (no gold) point its
per-slot retrieval at multi-target gold that single-query retrieval buries? The
charter fixed the bar **before** running: the per-slot union must recover **≥10 pp
more** multi-target gold than a single-query baseline at the same seed budget.

## Method

- 119 ObliQA multi-target questions (gold ≥ 2 passages).
- **Decompose:** orchestrator LLM (Nemotron-30B) splits each question into 1–6
  requirement slots (dataset-agnostic prompt: "the distinct things a complete
  answer must satisfy"; no gold inspection).
- **Two arms, equal 20-seed budget:** (a) single-query BM25 top-20; (b) round-robin
  union of per-slot BM25 top lists, capped at 20 total.
- **Measured at the seed level** (where EE5-X1 located the loss — rerank/bundle
  drop zero gold). No answer generation.

## Result — FAILED, decisively

| Metric | Value |
|---|---:|
| Baseline single-query multi-target recall (20 seeds) | **57.73%** |
| Per-slot union multi-target recall (20 seeds) | **49.61%** |
| **Δ (union − baseline)** | **−8.12 pp** |
| Mean slots induced per question | 4.04 |
| **Slot→gold alignment** (fraction of gold reachable by *any* slot's top-20) | **66.72%** |
| Pass bar (≥ +10 pp) | ❌ **No** (got −8.12) |

Decomposition made multi-target recall **worse**, not better. Stable across the run
(20/40/60/80/100-question checkpoints all showed baseline ~0.56 vs union ~0.44).

## Interpretation — two distinct failures, both informative

1. **Budget dilution (the −8 pp).** Splitting a fixed 20-seed budget across ~4
   slots gives each slot ~5 seeds. The strong facet loses depth (it needed >5 to
   reach its gold) faster than the weak facets gain coverage. Per-slot retrieval
   is *less* efficient than single-query at equal budget — a real cost of
   decomposition, not an implementation artifact.

2. **The ceiling number: 66.7% slot→gold alignment.** Even taking the **full,
   unbudgeted** top-20 of **every** slot, the induced requirement slots
   collectively reach only **two-thirds** of the gold. **One-third of multi-target
   gold is not retrievable by ANY question-derived requirement slot.** This is the
   decisive fact: the schema-coverage premise is *partially* false. The slots are
   somewhat real (66.7% is higher than any single similarity signal measured), but
   a third of the answer set is simply not derivable from the question's stated
   requirements at all.

## What this falsifies, and what survives

- **Falsified:** "requirement-coverage retrieval over question-induced slots lifts
  multi-target recall." At equal budget it lowers it; even uncapped it tops out at
  67% coverage. **EE6-X1 (the XL set-cover build) is not justified** — the gate
  exists precisely to catch this before weeks of work, and it did.

- **Survives / sharpened:** the ObliQA multi-target ceiling is now best explained
  as **under-determination** — a third of the gold passages are things a domain
  expert includes from *background knowledge of the regulation*, not things the
  question asks for. The question "what documentation accompanies a SAR?" has gold
  that includes passages a compliance officer knows are *related obligations*
  (record-keeping, retention periods) that the question never names. No mechanism
  operating on `(question, corpus)` — similarity OR coverage — can recover a gold
  passage the question does not, even implicitly, point to.

## Decision

**EE6 direction: NOT PROMOTED. Build halted at the gate (working as designed).**

The multi-target ceiling on ObliQA is substantially a **dataset property**
(under-determined gold), not a retriever deficiency — which is one of the two
outcomes the charter pre-registered as publishable. Recommended framing for the
paper:

- **Report ObliQA as +38 pp overall / single-target 85.8%** — the honest,
  strong, defensible headline. ASP retrieves the right *intact unit* far better
  than flat.
- **Characterise the multi-target gap as gold under-determination**, quantified:
  only 67% of multi-target gold is reachable from question-derived requirements;
  33% requires domain background the question does not encode. This is a *property
  of the benchmark*, and naming it precisely is a contribution, not a weakness.
- **Do not chase multi-target recall further on ObliQA** without either (a) a
  dataset whose multi-target gold is question-determined, or (b) an external
  domain-knowledge source (a regulation ontology) — which would be a different
  system (knowledge-graph shape), not the document-tree retriever.

## Artifacts

- Probe: `scripts/ee6_x0_schema_probe.py` (reproducible; `python -m scripts.ee6_x0_schema_probe`)
- Data: `data/benchmarks/ee6_x0_schema_probe.json` (summary + per-question rows)
- Diagnosis chain: [multitarget_ceiling_diagnosis.md](multitarget_ceiling_diagnosis.md)
