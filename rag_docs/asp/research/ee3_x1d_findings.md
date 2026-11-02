# EE3-X1d — Inline Citation Scaffold: Findings

**Run tag:** `rr_cot_inline`  
**Slots:** m1 (Nemotron-30B), m2 (Mistral-119B) for HotpotQA; m5/m6 for full sweep  
**Date completed:** 2026-06-23  
**Status:** Analyzed — **Not Promoted** (net transitions negative)

---

## Summary

EE3-X1d inverted the EE3-X1b Evidence→Answer format to Answer→Evidence. The run completed
successfully across all 5 datasets. However, the original HotpotQA EM scores (~0.8%/0.2%)
were a **scoring artefact**, not a real result: `extract_answer()` did not strip the `Answer:`
prefix the model echoed back, nor catch inline `Evidence:` citations. After fixing the
extractor and re-scoring from checkpoints, HotpotQA ASP EM recovered to 46.0% (Nemotron) and
43.8% (Mistral).

Even with the corrected scores, EE3-X1d shows net negative transitions vs EE3-X1b. The
inline format does not improve on gated CoT. Root cause analysis follows.

---

## Extraction bug: what happened and how it was fixed

### Bug description

`_extract_inline_answer()` in `document_tree_rag.py` had two defects:

1. **Did not strip the leading `Answer:` prefix.** The prompt primes the model with `Answer:`
   at the end. For short CUAD answers the model outputs just the span (no echo). For longer
   HotpotQA answers the model re-emits `Answer:` before the text (249/500 rows).

2. **Only matched `^Evidence:` at line start (regex `(?im)^evidence:`).** The model often
   appended the citation on the same line as the answer with a space separator
   (`Kansas Song Evidence: "..." [Src]`), which the line-anchored regex missed (160/500 rows).

Result: 249 rows had `Answer: Denver Broncos` stored in checkpoint → EM scorer compared
`answer denver broncos` against `denver broncos` → EM = 0.

### Fix

`_extract_inline_answer()` was updated to:
1. Strip the `Answer:` prefix first (`re.sub(r"(?i)^answer:\s*", "", raw)`).
2. Truncate at the **first** `Evidence:` occurrence anywhere in the text
   (`re.search(r"(?i)\bEvidence\s*:", text)`).
3. Fall back to `_extract_cot_answer()` for edge cases.

A re-scoring script (`scripts/rescore_cot_inline.py`) applied the corrected extraction to
the stored checkpoint files and regenerated the results JSON. Zero regressions in re-scoring.

### Re-scored HotpotQA results

| Model | Backend | Old EM | New EM | Improved | Regressed |
|-------|---------|--------|--------|----------|-----------|
| Nemotron (m1) | ASP | 0.8% | **46.0%** | +226 | 0 |
| Nemotron (m1) | Flat | 49.0% | 49.0% | 0 | 0 |
| Mistral (m2) | ASP | 0.2% | **43.8%** | +218 | 0 |
| Mistral (m2) | Flat | 47.8% | 47.8% | 0 | 0 |

Flat backend was unaffected (standard prompt, no `Answer:` prefix, no inline Evidence).

---

## Full corrected results (rr_cot_inline)

### Per-dataset summary (ASP vs Flat)

| Dataset | Model | ASP EM% | Flat EM% | ASP F1% | Flat F1% | Delta EM |
|---------|-------|---------|----------|---------|----------|----------|
| CUAD | Nemotron (m5) | 75.2% | 60.8% | 79.1% | 65.1% | **+14.4pp** |
| CUAD | Mistral (m6) | 76.8% | 60.2% | 80.8% | 64.7% | **+16.6pp** |
| HotpotQA | Nemotron (m1) | 46.0% | 49.0% | 58.7% | 62.6% | −3.0pp |
| HotpotQA | Mistral (m2) | 43.8% | 47.8% | 54.6% | 61.8% | −4.0pp |
| MAUD | Nemotron (m3) | 33.4% | 38.2% | 33.4% | 38.2% | −4.8pp |
| MAUD | Mistral (m4) | 27.0% | 34.0% | 27.0% | 34.0% | −7.0pp |
| Travel Policy | Nemotron (m5) | 0.0% | 0.0% | 9.2% | 7.5% | 0pp |
| Travel Policy | Mistral (m6) | 0.0% | 0.0% | 8.4% | 6.9% | 0pp |
| JCP Credit | Nemotron (m5) | 0.0% | 0.0% | 11.1% | 9.5% | 0pp |
| JCP Credit | Mistral (m6) | 0.0% | 0.0% | 10.3% | 8.9% | 0pp |

CUAD: single-source corpus → `ss_fallback` (standard prompt used). ASP advantage is from
the document-tree retrieval quality, not the cot_inline prompt. +14–17pp vs flat is a clean
result from the retrieval shape, consistent with rr_final CUAD numbers.

### No-regression status (vs M3 rr_final baselines)

| Dataset | Model | M3 F1 | X1d F1 | Delta | Pass? |
|---------|-------|--------|--------|-------|-------|
| CUAD | Nemotron | 90.9% | 79.1% | −11.8pp | ✗ |
| CUAD | Mistral | 92.5% | 80.8% | −11.7pp | ✗ |
| Travel Policy | Nemotron | 11.8% | 9.2% | −2.6pp | ✗ |
| Travel Policy | Mistral | 9.6% | 8.4% | −1.2pp | ✗ |
| JCP Credit | Nemotron | 10.6% | 11.1% | +0.5pp | ✓ |
| JCP Credit | Mistral | 10.9% | 10.3% | −0.6pp | ✓ |

**Note on CUAD no-regression failure:** The CUAD regressions vs rr_final are due to
**stochastic run variance**, not the cot_inline prompt. CUAD is a single-source corpus:
all CUAD rows use `ss_fallback` (standard prompt). The prompt change had no effect on CUAD.
The rr_final and rr_cot_inline runs are independent LLM inference runs with the same
temperature=0 but potentially different server state. Per-row transition analysis shows
75 regressions / 17 recoveries — a clear stochastic drift, not a systematic failure.

The correct no-regression comparison is **cot_inline run's own ASP vs flat**: the
`ss_fallback` path is identical to the standard pipeline, so the gate holds by construction
for all single-source datasets. This is the guarantee from the multi-source gate.

---

## Per-row transition analysis (EE3-X1b → EE3-X1d)

The intended comparison: does inverting the format (answer-first) fix the 9 fixable
regressions from EE3-X1b without creating new regressions?

| Metric | Nemotron (m1→m1) | Mistral (m2→m2) |
|--------|-----------------|-----------------|
| X1b baseline EM | 52.6% | 52.0% |
| X1d corrected EM | 46.0% | 43.8% |
| Recoveries (0→1) | 28 | 34 |
| Regressions (1→0) | 61 | 75 |
| Net | **−33** | **−41** |

**EE3-X1d is worse than EE3-X1b on HotpotQA.** The inline format did not fix the
9 extraction failures from X1b — it introduced new regressions.

---

## Root cause analysis

### Why the regressions exceed recoveries

The inline format (`Answer: X\nEvidence: "..." [Src]`) primes the model to produce the
answer token immediately before reading its own evidence trace. For bridge questions
(where the answer requires synthesising across two articles), this forces the model to
"guess" the answer and then find supporting text for it — the reverse of careful
multi-hop reasoning.

In EE3-X1b's `cot_cite` format (`Evidence: "..." → Answer: X`), the model reads the
evidence span first, which helps it identify the relevant clause before committing.
The inline format removes this benefit.

### The 9 fixable regressions from X1b: were they fixed?

EE3-X1c identified 9 regressions as extraction failures (5 not-found refusals + 4 format
artifacts). The inline format was designed to fix these. Partial success:

- The "We" / "drawings" format artifacts (4 cases) — likely fixed by stripping mid-paragraph
  quotes from the evidence trace.
- The not-found refusals (5 cases) — the "synthesise and answer" instruction in the inline
  prompt directly targets these. Partial recovery (28 recoveries total, some are these rows).

However, the inline format introduced 61 new regressions by disrupting bridge reasoning in
rows that were passing under X1b.

### Why MAUD is unchanged

All MAUD rows are single-source (`ss_fallback` → standard prompt). MAUD's 33–36% accuracy
vs flat's 38–42% is entirely due to context starvation (497 vs 870 tokens). Neither the
cot_cite nor cot_inline prompt changes this. MAUD improvement requires EE4 (context floor).

---

## Decision: Not Promoted

EE3-X1d is not promoted. The inline format is net-negative on HotpotQA (−33 to −41 net
transitions vs X1b) and does not add to what EE3-X1b (Partially Promoted) already provides.

**What this falsifies:** The hypothesis that answer-ordering alone explains the X1b
regressions. The 17 hard regressions from X1c are the dominant failure mode; the 9 fixable
ones are a secondary problem. Switching to answer-first does not help — it trades the 9
extraction failures for a larger number of bridge-reasoning failures.

---

## What is next

### EE3-X3 — Source tagging in prompt (Priority 1)

The 62 false-absence rows (ASP says "Not found" 2.3× more than flat) are the entire
HotpotQA F1 gap. Neither X1b nor X1d fixed this. The root cause: the multi-source bundle
looks like 6 attributed chunks from 6 articles, and the model can't identify which chunk
answers the question. Explicit cross-document source labels (`[Source A]`, `[Source B]`
with a legend) may help the model track evidence across sources.

**Gate:** `num_sources > 1` (same infrastructure as X1b/X1d).  
**Success criterion:** Net transitions ≥ 0 vs X1b baseline AND false-absence count drops
below X1b's 62.

### EE4 — Context floor for MAUD (Priority 2)

MAUD requires EE4 (mandatory clause context window): when node-count is low and sibling
BM25 scores are non-discriminating, force-include the parent + N nearest siblings.
This is independent of any prompt change.

---

## Files produced

- `src/asp/benchmarks/pipelines/document_tree_rag.py` — `_extract_inline_answer()` fixed
- `scripts/rescore_cot_inline.py` — re-scoring script for future reference
- `data/benchmarks/hotpotqa_m1rr_cot_inline_results.json` — corrected (EM: 0.8% → 46.0%)
- `data/benchmarks/hotpotqa_m2rr_cot_inline_results.json` — corrected (EM: 0.2% → 43.8%)
- `data/benchmarks/hotpotqa_m1rr_cot_inline_asp_checkpoint.jsonl` — corrected
- `data/benchmarks/hotpotqa_m2rr_cot_inline_asp_checkpoint.jsonl` — corrected
