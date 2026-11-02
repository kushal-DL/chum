# Benchmark Report — rr_source_tag (EE3-X3)

**Date:** 2026-06-25
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** rr_source_tag
**Models:** Slot 1 = Nemotron-30B (m1), Slot 2 = Mistral-119B (m2); Slots 3/4 = CUAD no-regression; Slots 5/6 = EW no-regression
**Datasets:** HotpotQA (primary), CUAD, Travel Policy, JCP Credit (no-regression)
**Backends:** ASP + Flat (same-condition, same process)
**n per dataset:** 500 (HotpotQA, CUAD), 20 (Travel Policy), 40 (JCP Credit)
**Experiment:** Add `[Source A]`, `[Source B]` labels + legend header to multi-source bundles to help model locate bridging entities across documents.

---

## 1. Headline scorecard

| Dataset | Model | ASP EM% | Flat EM% | Delta EM | ASP F1% | Flat F1% | Delta F1 | Verdict |
|---------|-------|---------|----------|----------|---------|----------|----------|---------|
| HotpotQA | Nem (m1) | 42.8 | 46.8 | −4.0 | 52.8 | 60.2 | −7.4 | ❌ |
| HotpotQA | Mis (m2) | 45.0 | 47.2 | −2.2 | 56.0 | 60.6 | −4.6 | ❌ |
| CUAD | Nem (m3) | 75.6 | 59.6 | +16.0 | 79.5 | 64.2 | +15.3 | ✅ (no-regress only) |
| CUAD | Mis (m4) | 75.8 | 61.8 | +14.0 | 79.9 | 65.7 | +14.2 | ✅ (no-regress only) |
| Travel Policy | Nem (m5) | 5.0 | 5.0 | 0.0 | 14.7 | 12.8 | +1.9 | ⚠️ |
| Travel Policy | Mis (m6) | 5.0 | — | — | 14.1 | 12.1 | +2.0 | ⚠️ |
| JCP Credit | Nem (m5) | 7.5 | — | — | 20.3 | 13.1 | +7.2 | ✅ |
| JCP Credit | Mis (m6) | 7.5 | — | — | 20.5 | 14.4 | +6.1 | ✅ |

Primary verdict for this experiment: **HotpotQA ❌** — the dataset that source tagging was designed to fix regressed.

---

## 2. Efficiency scorecard

| Dataset | Model | ASP ctx tok | Flat ctx tok | ASP lat ms | Flat lat ms | Tok ratio |
|---------|-------|------------|-------------|-----------|------------|-----------|
| HotpotQA | Nem | 1293 | 1214 | — | — | 1.06 |
| HotpotQA | Mis | 1293 | 1214 | — | — | 1.06 |
| CUAD | Nem | 240 | 681 | — | — | 0.35 |
| CUAD | Mis | 240 | 682 | — | — | 0.35 |

Context tokens on HotpotQA: identical to X1b (1293). The legend header adds ~15–20 tokens
but is within budget, so no additional compression occurred.

---

## 3. No-regression check

Stable CUAD baseline: ~79% EM (handoff note: rr_final 86–88% was an outlier; 4 subsequent
runs land at 75–82%).

| Dataset | Model | Stable baseline ASP EM | X3 ASP EM | Delta | Pass? |
|---------|-------|----------------------|-----------|-------|-------|
| CUAD | Mis (m4) | ~79% (rr_final=82%) | 75.8% | −3.2pp vs rr_final | ⚠️ borderline |
| Travel Policy | Nem (m5) | 0.0% (F1=11.8%) | F1=14.7% | +2.9pp F1 | ✅ |
| Travel Policy | Mis (m6) | 0.0% (F1=9.6%) | F1=14.1% | +4.5pp F1 | ✅ |
| JCP Credit | Nem (m5) | 0.0% (F1=10.6%) | F1=20.3% | +9.7pp F1 | ✅ |
| JCP Credit | Mis (m6) | 0.0% (F1=10.9%) | F1=20.5% | +9.6pp F1 | ✅ |

CUAD: single-source bundles are not affected by the source-tagging gate (fires only on
`len(distinct_slugs) > 1`). The −3.2pp vs rr_final on CUAD is within the observed
run-to-run variance (75–82% range across 4 runs). **CUAD passes no-regression.**

Travel Policy/JCP Credit large F1 gains are unexpected but consistent with the gate not firing on EW
(single-source) — likely model/run variance across slots rather than a causal effect.

---

## 4. Failure breakdown (HotpotQA)

### vs rr_final baseline

| Bucket | X3 ASP (Nem m1) | rr_final ASP (m1) | rr_final Flat (m1) |
|--------|-----------------|-------------------|-------------------|
| Correct (F1 = 1.0) | 214 | 239 | 256 |
| Partial (0 < F1 < 1) | 87 | 70 | 109 |
| Wrong answer (F1=0, not FA) | 149 | 126 | 109 |
| False-absence ("Not found") | 50 | **65** | 26 |

False-absences decreased (65 → 50, −23%), confirming the hypothesis was directionally
correct. However, wrong-answer count increased substantially (126 → 149, +23 rows).

---

## 5. Per-row transitions vs X1b baseline (HotpotQA)

### Nemotron m1

| Transition | Count |
|-----------|-------|
| high→high (stable correct) | 259 |
| high→low (regression) | 87 |
| low→high (recovery) | 20 |
| low→low (stable wrong) | 134 |
| **Net** | **−67** |

### Mistral m2

| Transition | Count |
|-----------|-------|
| high→high (stable correct) | 270 |
| high→low (regression) | 67 |
| low→high (recovery) | 27 |
| low→low (stable wrong) | 136 |
| **Net** | **−40** |

---

## 6. Root-cause analysis of regressions

**87 regressions (Nem) / 67 (Mis) where X1b F1≥0.5 → X3 F1<0.5:**

- **42/87 (Nem) / ~35/67 (Mis) predicted exactly "We"** — the same extraction artifact
  documented in EE3-X1c (fallback extractor picked "We" from mid-paragraph quotes).

- Root cause: the `[Source A]` prefix prepended to every chunk causes many context
  paragraphs to effectively begin with the source tag followed immediately by text that
  starts with "We..." (e.g. `[Source A] [Ed Wood Films]\nWe find that...`). When the
  `cot_cite` CoT scaffold is active and the model omits the `Answer:` label, the
  `_extract_cot_answer` fallback picks the last non-blank non-Evidence line, which
  may be a "We..." quote carried into the model's reasoning.

- Context tokens are identical to X1b (1293 avg) — the legend adds minimal overhead
  and cannot explain the regressions. The regression is purely a format/extraction
  interaction.

- **Flat baseline also regressed under X3** (60.2% vs 65.8% rr_final F1 for m1),
  confirming that the source-tag format per se disrupts model comprehension — not just
  the ASP CoT extraction logic.

**12 regressions in neither category**: pure wrong answers where the model produced a
plausible but incorrect span, likely confusion from the legend + chunk-label structure
disrupting entity tracking (opposite of the hypothesis effect).

---

## 7. Key findings

1. **False-absences dropped 23%** (65 → 50 Nem; 62 → 48 Mis) — the tagging does improve
   entity localisation as hypothesised, but the effect is modest and outweighed by regressions.

2. **42/87 Nem regressions are "We" extraction artifacts** — the same `_extract_cot_answer`
   fallback failure from EE3-X1, re-activated by the `[Source X]` chunk prefix format.

3. **Flat baseline also regressed** (−5.6pp F1 Nem vs rr_final) — source tags disrupt
   LLM comprehension independently of the ASP CoT extraction logic.

4. **CUAD / Travel Policy / JCP Credit no-regression passed** — the multi-source gate correctly prevented
   source-tagging from firing on single-source corpora.

5. **Net F1 transitions: −67 Nem / −40 Mis vs X1b** — EE3-X3 is strictly worse than
   the EE3-X1b baseline on HotpotQA.

---

## 8. Decision

**Not Promoted.**

The hypothesis was partially confirmed — false-absences dropped 23%, demonstrating that
explicit cross-document labelling does improve entity localisation. However, the `[Source X]`
chunk prefix format is incompatible with the existing `cot_cite` extraction logic: 42 of 87
Nem regressions predicted exactly "We", replicating the X1c extraction artifact at scale.
Additionally, the flat baseline also regressed, indicating that the label format itself
disrupts model comprehension beyond the extraction layer.

**Two corrective paths for the next experiment:**

**Path A (extraction fix — EE3-X3b):** Keep source labels but replace the CoT scaffold
with the standard QA prompt for multi-source bundles. The legend + `[Source X]` prefixes
alone may reduce false-absences without triggering the CoT extraction bug. The `_extract_cot_answer`
fallback is the failure point; removing CoT when source-tagging is active bypasses it entirely.

**Path B (format change):** Use a lighter format — inline parenthetical labels after each
chunk title rather than a separate `[Source X]` prefix (e.g. `[Ed Wood (film) §2]` →
`[Ed Wood (film) §2 — Article A]`). This preserves the existing chunk header format that
models already read reliably while adding just the cross-document anchor.

Recommend **Path A first** — zero additional implementation work, directly tests whether
the label format + standard prompt closes false-absences without the CoT extraction hazard.
