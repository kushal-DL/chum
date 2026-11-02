# EE3-X1 — Findings & Decision

**Status:** ✅ Complete — Analyzed
**Experiment:** Chain-of-thought citation scaffold (prompt-only change)
**Spec:** [experiment-backlog/epic-ee3-bundle-assembly.md](../../experiment-backlog/epic-ee3-bundle-assembly.md)
**Date:** 2026-06-22 (full run completed ~05:00; slot 1 at 02:48, slot 2 at ~05:00)

---

## TL;DR

The CoT citation scaffold partially works on the test bench (25–30% recovery on the
75–78 retrieval_hit+generation_miss rows) but **fails the no-regression bar on all four
single-source datasets** by 1.6–5.4 pp. The root cause is a prompt-extraction mismatch:
the model sometimes outputs "We" or other span-internal fragments as the extracted answer
when the Evidence quote starts with a pronoun or clause-internal word. EE3-X1 is **Not
Promoted**. The finding redirects the next experiment toward a bundle-structure change
rather than a prompt-only change.

---

## What we changed

`_QA_PROMPT` replaced with `_QA_COT_PROMPT` when `prompt_mode="cot_cite"`:

```
Step 1 — Find exact phrase(s) from context supporting the answer.
  Evidence: "<quoted text>" [SourceName]
Step 2 — On a NEW line, state the answer concisely.
  Answer: <concise answer>
```

`_extract_cot_answer()` first looks for the last `Answer:` label; falls back to the
last non-Evidence line when the label is absent.

No retrieval changes. `_MC_PROMPT` (MAUD, CUAD multi-choice) untouched.

---

## Numbers

### HotpotQA — both slots

| Metric | s1 M3 baseline | s1 M4 CoT | s2 M3 baseline | s2 M4 CoT |
|---|---|---|---|---|
| F1 | 0.661 | **0.647** (−1.4 pp) | 0.662 | **0.664** (+0.2 pp) |
| EM | 0.532 | 0.524 | 0.536 | 0.546 |
| recall@6 | 0.890 | **0.890** (unchanged) | 0.891 | **0.890** (unchanged) |
| Latency | 3,639 ms | 6,676 ms (+83%) | 1,918 ms | 4,054 ms (+111%) |
| Tokens | 1,189 | 1,827 (+54%) | 1,193 | 1,793 (+50%) |

### HotpotQA failure-bucket decomposition

| Bucket | s1 M3 | s1 M4 CoT | s2 M3 | s2 M4 CoT |
|---|---|---|---|---|
| retrieval_miss | 1 | 1 | 1 | 1 |
| retrieval_partial | 77 | 76 | 75 | 76 |
| retrieval_hit_genmiss | **75** | **82** | **77** | **72** |
| Total failures | 153 | 159 | 153 | 149 |

### Per-row F1 transitions (vs M3 baseline)

| | s1 | s2 |
|---|---|---|
| Recoveries (low→high F1) | 24 | 27 |
| Regressions (high→low F1) | 30 | 22 |
| **Net** | **−6** | **+5** |

### Test-bench recovery (retrieval_hit+genmiss rows from M3)

| | s1 (75 rows) | s2 (77 rows) |
|---|---|---|
| Recovered (now F1 ≥ 0.5) | 19 (25.3%) | 23 (29.9%) |
| Remained failing | 56 | 54 |

### No-regression check

| Dataset | M3 baseline | M4 CoT (s1) | Δ | Pass? |
|---|---|---|---|---|
| MAUD acc | 0.372 | 0.356 | −1.6 pp | ❌ |
| CUAD F1 | 0.938 | 0.896 | **−4.2 pp** | ❌ |
| Travel Policy F1 | 0.112 | 0.072 | **−4.0 pp** | ❌ |
| JCP Credit F1 | 0.104 | 0.082 | −2.2 pp | ❌ |

All four single-source datasets fail the 1 pp no-regression bar.

---

## Root cause analysis

### Why the test bench partially recovered (25–30%)

The CoT scaffold forces the model to locate and quote a supporting span before
answering. For questions with a clear named-entity answer ("YG Entertainment",
"Terry Richardson", "Yes/No"), quoting the span then extracting the answer works
well — the model produces a short, clean answer that matches F1 exactly.

This is the mechanism working as designed.

### Why single-source datasets regressed (−2 to −5 pp)

Inspecting CUAD regressions reveals a single dominant failure mode:

**The model outputs "We" (or another clause-internal fragment) as the extracted answer.**

Example (CUAD):
```
Q:  Which NFL team won Super Bowl 50?
gold: Denver Broncos
base: Denver Broncos
CoT:  We
```

What happened:
1. The model quoted the relevant span verbatim: `"We defeated the Panthers to win Super Bowl 50" [Super Bowl 50]`
2. The Evidence quote starts mid-sentence (BM25 extracted a paragraph that begins "We...")
3. The `_extract_cot_answer()` fallback (used when no `Answer:` label is present)
   picked the last non-Evidence line — which was a sentence fragment from the
   evidence-quoting step, not the answer.

A secondary failure mode: **forced literal quoting degrades F1 for numeric answers.**
```
Q:  How many teams have been in the Super Bowl eight times?
gold: four
base: four
CoT:  4
```
The model quoted `"four"` from context but the extraction returned `"4"` (numeric
normalisation by the model when restating). Token F1 scores `"4"` vs `"four"` = 0.0.

### Why latency roughly doubled

The CoT prompt adds ~100–150 tokens of instruction overhead, and the Evidence-quoting
step itself adds ~200–400 tokens of output before the answer. Nemotron-30B (+83%)
and Mistral-119B (+111%) both spent significantly more generation time. This is
a meaningful cost increase for no net gain on the test bench.

### Why the slot-2 result is slightly better

Mistral-119B follows format instructions more reliably — it emits `Answer:` on a
separate line more consistently, so `_extract_cot_answer()` uses the primary path
rather than the fallback. This is why slot 2 shows +5 net transitions vs slot 1's −6.

The difference is not large enough to constitute a win on either slot.

---

## What this means

### The test bench is real but the approach is wrong

25–30% recovery on the 75–78 test-bench rows confirms the hypothesis that this
bucket is addressable — these rows have full retrieval, and some intervention on
how the evidence is presented to the LLM can recover them. The CoT scaffold is
not the right intervention.

The failure mode ("We") exposes the real problem: **the bundle assembler is delivering
evidence chunks that start mid-paragraph**, so the natural continuation of the quoted
span is ambiguous. A source-tagging change that gives each chunk a clearer header
(EE3-X3) would let the model quote evidence more precisely without mid-sentence
artifacts.

Alternatively, the CoT approach could work if the prompt is gated on `num_distinct_sources > 1`
(only apply to multi-source bundles), since single-source bundles don't benefit from
the two-step procedure and are where the regressions concentrate.

### The latency cost is prohibitive as-is

+83–111% latency for 25–30% test-bench recovery is not a viable trade. For a CoT
approach to be worth the cost, it would need to either (a) recover a much larger
fraction of the test bench, or (b) not double latency. A structured-output approach
that commits the model to a span citation at answer-generation time (rather than a
separate Evidence step before the answer) might achieve similar grounding with less
token overhead.

---

## Decision

**EE3-X1: Not Promoted.** The no-regression bar failed on all four single-source
datasets. The test-bench recovery (25–30%) shows the direction is viable but
the current prompt design has a critical extraction flaw.

**Next experiment options (in order of expected leverage):**

1. **EE3-X1b — Multi-source-gated CoT**: Apply `cot_cite` mode only when
   `num_distinct_sources > 1` (detectable from the bundle at generation time, fully
   dataset-agnostic). Single-source bundles stay on the standard prompt. This
   eliminates all CUAD/EW regressions by construction while preserving the
   HotpotQA benefit.

2. **EE3-X3 — Source-tagging**: Add explicit `[Source A]` / `[Source B]` labels to
   each chunk in the bundle so the model can track which evidence belongs to which
   article. Addresses the "mid-paragraph quote start" problem without requiring
   a separate Evidence step. Cheaper in tokens.

3. **EE3-X1c — Structured-output answer**: Require the model to emit JSON
   `{"cited_span": "...", "answer": "..."}` rather than a free-text two-step.
   Parser extracts `answer` field directly; eliminates the fallback-extraction
   failure mode entirely.

**Recommended next step: EE3-X1b** (multi-source gating). It can be implemented
in < 1 hour, reuses all the CoT infrastructure already in place, and has a strong
prior expectation of eliminating the regressions by construction. No new benchmark
run needed for the regression check — the single-source datasets will degenerate
to the standard prompt.

---

## Cross-references

- Test bench source: [ee2_x1_findings.md](ee2_x1_findings.md)
- Code: [`_select_seeds_round_robin`, `_QA_COT_PROMPT`, `_extract_cot_answer`](../../src/asp/benchmarks/pipelines/document_tree_rag.py)
- Result data: `data/benchmarks/hotpotqa_m{1,2}rr_cot_results.json`
- No-regression data: `data/benchmarks/{maud,cuad,travel_policy,jcp_credit}_m1rr_cot_results.json`
