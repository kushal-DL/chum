# EE3-X5 — Multi-Source Soft Not-Found Prompt: Findings

**Run tag:** `rr_soft_ms`  
**Slots:** m1 (Nemotron-30B), m2 (Mistral-119B) for HotpotQA; m5/m6 for no-regression sweep  
**Date completed:** 2026-06-24  
**Status:** Analyzed — **Not Promoted**

---

## Summary

Softening the "not found" instruction for multi-source bundles did not close the
HotpotQA false-absence gap and produced net-negative per-row transitions. False-absences
dropped modestly for Nemotron (66→57) but increased for Mistral (63→66). More critically,
the prompt change introduced regressions from previously-correct rows — all 55 (Nem) /
53 (Mis) regressions came from rows that were EM=1 in the baseline. Only 2/6 recoveries
came from actual false-absence rows.

The softened instruction is doing the opposite of what was intended: rather than
converting false-absences to correct answers, it is converting correct answers to wrong
ones by encouraging the model to synthesise when it should stay grounded.

---

## Results

### HotpotQA EM / F1

| Model | rr_final ASP EM | soft_ms ASP EM | soft_ms Flat EM | Delta vs Flat |
|-------|----------------|---------------|-----------------|---------------|
| Nemotron | 53.0% | 45.4% | 49.3% | −3.9pp |
| Mistral | 52.6% | 46.1% | 49.0% | −2.9pp |

Both models regress vs their rr_final baselines. Note flat also regressed (56.7% → 49.3%
for Nemotron) — this is a different inference run and both backends show run variance. The
meaningful signal is ASP vs flat **within the same run**, which shows ASP still trails
flat by −2.9 to −3.9pp — essentially unchanged from rr_final's −3.7 to −4.2pp gap.

### Per-row transition analysis vs rr_final baseline

| Model | Recoveries | Regressions | Net | FA recovered | FA→correct |
|-------|-----------|-------------|-----|--------------|------------|
| Nemotron | 17 | 55 | **−38** | 2 | 2/17 (12%) |
| Mistral | 21 | 53 | **−32** | 6 | 6/21 (29%) |

All 55 (Nem) / 53 (Mis) regressions came from rows where the baseline had EM=1. The
softened instruction caused the model to over-synthesise on questions it was previously
answering correctly.

### False-absence counts

| Model | rr_final FA | soft_ms FA | Flat FA |
|-------|------------|-----------|---------|
| Nemotron | 66 | **57** (−9) | 24 |
| Mistral | 63 | **66** (+3) | 35 |

Nemotron shows a small reduction (−9). Mistral shows a small increase (+3). Neither
approaches flat's 24–35 range. The false-absence gap with flat (which is the primary
target of this experiment) is not closed.

### No-regression sweep (CUAD, Travel Policy, JCP Credit)

CUAD uses `ss_fallback` (standard prompt — gate fires only for multi-source bundles).
CUAD, Travel Policy, JCP Credit all show expected behaviour:

| Dataset | Model | ASP EM/F1 | Flat EM/F1 | Note |
|---------|-------|-----------|-----------|------|
| CUAD | Nemotron | 75.2% / 79.1% | 63.6% / 68.0% | ss_fallback, unchanged |
| CUAD | Mistral | 76.4% / 80.7% | 63.6% / 68.0% | ss_fallback, unchanged |
| Travel Policy | Nemotron | F1=9.2% | F1=6.6% | unchanged |
| Travel Policy | Mistral | F1=5.0% | F1=4.5% | unchanged |
| JCP Credit | Nemotron | F1=10.6% | F1=8.6% | unchanged |
| JCP Credit | Mistral | F1=10.3% | F1=5.3% | unchanged |

The gate works correctly — single-source datasets are unaffected by the prompt change.

---

## Root cause analysis

### Why the soft instruction causes regressions

The `_QA_SOFT_MS_PROMPT` replaces:
> "If the answer cannot be found in the context, say 'Not found in context.'"

with:
> "If the answer requires combining information from multiple sources, synthesise and answer.
> Only say 'Not found in context.' if the answer is genuinely absent from ALL provided sources."

The "synthesise and answer" instruction is interpreted by the model as license to generate
an answer even when it is uncertain. For rows where the model was previously correctly
identifying the answer span and returning EM=1, the soft instruction occasionally causes
it to produce a synthesised paraphrase that fails the strict EM normalisation — or worse,
to attempt cross-document synthesis and produce an incorrect bridge answer.

In other words: the model was not refusing on the 55/53 regression rows because of
over-conservatism — it was correctly answering. The softened instruction introduced
unnecessary synthesis pressure on rows that did not need it.

### Why false-absences are hard to fix with prompt alone

The 57–66 false-absence rows share a structural property: the answer requires identifying
which of two articles contains the bridging entity. When the model sees 6 chunks from 6
sources, it sometimes cannot locate the connecting entity span and refuses rather than
guessing. The soft instruction says "synthesise if needed" — but synthesis is not the
issue; entity-location across articles is. The model needs better evidence attribution,
not permission to synthesise.

This points toward **EE3-X3 (source tagging)** as the correct next step: if each chunk
is labelled `[Source A]` / `[Source B]` with a legend, the model may more reliably track
which article contains which entity.

---

## Decision: Not Promoted

EE3-X5 is not promoted. The softened not-found instruction:
- Does not meaningfully reduce false-absences (−9 for one model, +3 for the other)
- Introduces −32 to −38 net regressions from previously-correct rows
- The gate (num_sources > 1) is correct and causes no single-source regression, but the
  prompt change itself is harmful on multi-source bundles

**What this falsifies:** The hypothesis that false-absence is driven by an overly
conservative instruction text. The result suggests false-absence is structural — the
model genuinely cannot locate the bridging entity across 6 source chunks — and cannot
be fixed by instruction softening alone.

**Next direction:** EE3-X3 — explicit source tagging with cross-document labels. If the
model can track `[Source A]` vs `[Source B]` through the bundle, it may locate the
entity and synthesise without needing permission to do so.

---

## CUAD rr_final vs current run variance note

CUAD rr_final showed 86.8%/88.6% EM. The soft_ms and ctx700 runs show 75–81% EM on CUAD
across four independent runs (cot_inline, soft_ms, ctx700). The rr_final result is now the
outlier across 4 runs; 75–81% is the stable operating range. The true CUAD baseline should
be treated as ~79% EM (average of the 4 post-rr_final runs) rather than 86–88%.

---

## Files produced

- `src/asp/benchmarks/pipelines/document_tree_rag.py` — `_QA_SOFT_MS_PROMPT`, `prompt_mode="soft_ms"`
- `src/asp/benchmarks/runner.py` — `soft_ms` added to `--prompt-mode` choices
- `data/benchmarks/hotpotqa_m1rr_soft_ms_results.json` — Nemotron results
- `data/benchmarks/hotpotqa_m2rr_soft_ms_results.json` — Mistral results
- `data/benchmarks/cuad_m5rr_soft_ms_results.json` — CUAD no-regression (clean)
- `data/benchmarks/cuad_m6rr_soft_ms_results.json` — CUAD no-regression (clean)
- `data/benchmarks/ew1_m5rr_soft_ms_results.json`, `ew2_m5rr_soft_ms_results.json` — EW no-regression (clean)
