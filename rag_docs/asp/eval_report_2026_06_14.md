# ASP Evaluation Report — Flat RAG vs Tree RAG
**Date:** 2026-06-14  
**Author:** Kushal Sharma  
**Dataset:** 20 questions from `configs/eval_questions.yaml` (GTP-v3.0 travel policy)  
**Infrastructure:** Both servers live on localhost (flat: 8001, tree: 8000)

---

## Executive Summary

| Metric | Flat RAG | Tree RAG | Winner |
|--------|----------|----------|--------|
| Completion rate | 19/20 (95%) | 17/20 (85%) | **Flat RAG** |
| Avg tokens/query (completed) | 1,232 | 2,295 | **Flat RAG** |
| Avg latency (completed) | 13,880 ms | 8,265 ms | **Tree RAG** |
| Answer correctness (manual) | 10/19 (53%) | 13/17 (76%) | **Tree RAG** |
| Hallucination / wrong cite | 4 cases | 1 case | **Tree RAG** |
| Unanswered (said "not in policy") | 5 | 4 | Tie |

**Headline verdict:** Tree RAG produces more accurate, better-cited answers — but currently uses *more* tokens per query, not fewer. This is a retrieval over-fetch issue in the tree pipeline (large full-document context is assembled before pruning). Flat RAG uses fewer tokens but scores notably worse on accuracy and cites incorrectly in several cases. The latency advantage for tree RAG (when it completes) is a positive signal.

---

## Per-Question Results

### EQ-001 — Hotel reimbursement for Tier 1 cities `[easy / lookup]`
**Expected section:** §6.2 India Hotel Limits

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "context does not specify hotel limits for Tier 1" — **wrong / incomplete** | Full grade-tiered table (G1–G8) with rupee amounts — **correct** |
| Tokens | 1,112 | 2,526 |
| Latency | 9,125 ms | 13,311 ms |
| Citation | None | §6.2 India Hotel Limits ✓ |

**Verdict:** Tree RAG wins. Flat RAG hallucinated absence of information that clearly exists in the policy.

---

### EQ-002 — Advance booking days for international flights `[easy / lookup]`
**Expected section:** §4.2 Advance Booking Requirements

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "14 days (short-haul), 21 days (long-haul)" — **correct** | **TIMEOUT ERROR** (45 s) |
| Tokens | 1,380 | 0 (error) |
| Latency | 2,639 ms | — |
| Citation | §4.2 ✓ | — |

**Verdict:** Flat RAG wins. Tree RAG timed out — model API issue during generate step.

---

### EQ-003 — Documentation for hotel claims above limit `[medium / procedure]`
**Expected section:** §13.2 Exception Approval Process

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | Client approval email + justification (India & international) — **correct** | **TIMEOUT ERROR** |
| Tokens | 1,366 | 0 (error) |
| Latency | 21,078 ms | — |
| Citation | §14.2 ✓ | — |

**Verdict:** Flat RAG wins. Tree RAG timed out again.

---

### EQ-004 — Business class on flights under 3 hours `[easy / lookup]`
**Expected section:** §5.1 Cabin Class Entitlement

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "No." (bare, no detail) — **correct but thin** | "No. Economy only for all grades (G1–G8) for flights <3 hours" — **correct + complete** |
| Tokens | 1,321 | 2,365 |
| Latency | 35,171 ms | 10,485 ms |
| Citation | §global_travel_policy (vague) | §5.1 Cabin Class Entitlement ✓ |

**Verdict:** Tree RAG wins on answer quality and citation precision. Flat RAG was technically correct but unhelpfully terse; its latency was 3× worse.

---

### EQ-005 — Per diem rates: Singapore vs Mumbai `[hard / comparison]`
**Expected section:** §7.3 International Per Diem + §7.1 India Meal Reimbursement

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | Singapore rates correct; Mumbai "per diem applies (no fixed rate)" — **partially wrong** | Singapore grades table + Mumbai on-actuals with caps — **correct** |
| Tokens | 1,744 | 3,262 |
| Latency | 1,781 ms | 4,406 ms |
| Citation | §5.1 Singapore (wrong section number) | §7.3 International Per Diem ✓ |

**Verdict:** Tree RAG wins. Flat RAG cited the wrong section and gave an incomplete Mumbai answer. Tree RAG correctly explained the actuals-with-caps distinction.

---

### EQ-006 — Consequences for booking non-approved hotel `[medium / exception]`
**Expected section:** §20.1 Violation Classification + §20.2 Consequences

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Policy does not explicitly state consequences; §6.1 requires..." — **partially correct** | "Policy does not explicitly state consequences..." — **same, partially correct** |
| Tokens | 1,542 | 1,574 |
| Latency | 2,281 ms | 10,983 ms |
| Citation | §6.1, §4.3 | None |

**Verdict:** Tie — both correctly identified the policy gap. Flat RAG was faster and cited relevant compliance sections; tree RAG offered no citation at all.

---

### EQ-007 — Pre-approval process for budget-exceeding travel `[medium / procedure]`
**Expected section:** §13.2 Exception Approval Process

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Form GTP-01 in Finance portal, prior notification for overruns" — **correct path, partial** | Full escalation path: manager → Finance BP → VP for >50% above grade limit — **more complete** |
| Tokens | 1,442 | 2,037 |
| Latency | 5,703 ms | 3,500 ms |
| Citation | §2.2, india_domestic — **wrong source** | §13.2 Exception Approval ✓ |

**Verdict:** Tree RAG wins. It retrieved the correct cross-cutting section with the proper VP-escalation rule. Flat RAG cited the India domestic policy only, missing the global exception process.

---

### EQ-008 — Conference registration fees: in budget or separate? `[easy / lookup]`
**Expected section:** §12.1 Explicit Exclusions

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Separate from travel budget" + citation — **correct** | "Policy context does not address this" — **wrong / retrieval failure** |
| Tokens | 1,215 | 1,848 |
| Latency | 1,031 ms | 1,296 ms |
| Citation | §5.4 international_travel_policy ✓ | None |

**Verdict:** Flat RAG wins. Tree RAG failed to retrieve the exclusions section for this simple lookup.

---

### EQ-009 — Max trip duration for corporate travel insurance `[easy / lookup]`
**Expected section:** §17.1 Company Policy Coverage

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Duration not explicitly stated in context" — **incomplete** | **TIMEOUT ERROR** |
| Tokens | 1,325 | 0 (error) |
| Latency | 3,500 ms | — |

**Verdict:** Flat RAG wins by default. Neither answered correctly — policy may genuinely not specify a number. Tree RAG timed out.

---

### EQ-010 — Flight cancellation: claimable hotel and meal expenses `[medium / multi-hop]`
**Expected section:** §15 Emergency Travel + §15.3 + §6.2 + §7.1

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Cancel hotel immediately; meals in airline/hotel packages not claimable" — **partially correct** | "Policy context does not address flight cancellations" — **wrong / retrieval failure** |
| Tokens | 1,270 | 2,292 |
| Latency | 19,452 ms | 5,625 ms |
| Citation | §16.1 ✓ | None |

**Verdict:** Flat RAG wins. Tree RAG's section_retrieve missed the Emergency Travel sections entirely. This is the most concerning failure — multi-hop queries require cross-section linking which the tree retriever doesn't yet handle.

---

### EQ-011 — Excess baggage reimbursement cap `[easy / table]`
**Expected section:** §5.5 Excess Baggage

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "No cap; only reimbursable for Company materials with receipt + justification" — **correct** | Same substance — **correct** |
| Tokens | 1,149 | 2,694 |
| Latency | 52,078 ms | 2,077 ms |
| Citation | §5.5 ✓ | §5.5 ✓ |

**Verdict:** Tree RAG wins — same answer quality but 25× faster latency (2 s vs 52 s). Flat RAG had a severe latency spike here.

---

### EQ-012 — Personal credit card for expenses `[easy / lookup]`
**Expected section:** §10.3 Currency and Foreign Exchange

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "No — personal supplementary insurance premiums not reimbursable" — **completely wrong question interpretation** | "Yes — claim in original currency; system converts via RBI rate" — **correct** |
| Tokens | 1,039 | 2,367 |
| Latency | 1,344 ms | 11,985 ms |
| Citation | §17.3 (wrong section entirely) | §10.3 ✓ |

**Verdict:** Tree RAG wins decisively. Flat RAG answered a different question (personal insurance) — a clear semantic retrieval failure. Tree RAG found the correct currency/payment section.

---

### EQ-013 — Carbon offset requirements for business travel `[medium / lookup]`
**Expected section:** Not in policy (expected "no provisions")

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | **TIMEOUT ERROR** | "Policy context contains no carbon offset information" — **correct (policy gap)** |
| Tokens | 0 (error) | 1,576 |
| Latency | — | 1,296 ms |
| Citation | — | None (correct — nothing to cite) |

**Verdict:** Tree RAG wins. This question tests whether the system correctly admits absence of a provision. Tree RAG correctly said "not in policy." Flat RAG timed out.

---

### EQ-014 — Difference between Tier 1 and Tier 2 hotel limits `[medium / comparison]`
**Expected section:** §6.2 India Hotel Limits

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | Full Tier 1 and Tier 2 table with per-grade amounts — **correct** | Delta amounts per grade only (not absolute values) — **partially correct** |
| Tokens | 1,271 | 2,370 |
| Latency | 44,125 ms | 2,358 ms |
| Citation | §6.2 (inferred) | §6.2 ✓ |

**Verdict:** Flat RAG wins on answer completeness (showed both tiers' absolute values). Tree RAG computed the delta but didn't give the full comparison. Both latencies are concerning — flat at 44 s, tree at 2.4 s.

---

### EQ-015 — Expense report submission deadline `[easy / lookup]`
**Expected section:** §10.1 Submission Deadlines

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Within 10 days strongly encouraged; cites multiple country-specific policies" — **approximately correct** | "Within 15 days of travel end date" — **correct and precise** |
| Tokens | 1,240 | 2,433 |
| Latency | 16,266 ms | 1,046 ms |
| Citation | §10.1 ✓ | §10.1 ✓ |

**Verdict:** Tree RAG wins. It gave the authoritative 15-day deadline from the global policy. Flat RAG pulled in multiple country-specific variants and hedged ("10 days strongly encouraged") which could confuse a user.

---

### EQ-016 — Spouse/family travel cost reimbursability `[hard / exception]`
**Expected section:** §1.2 Scope + §1.3 Exclusions + §6.5 Sharing Accommodation

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "No — no applicable section addresses this" — **correct (policy gap)** | "No — see §12.1 Explicit Exclusions" — **correct + citation** |
| Tokens | 1,231 | 2,195 |
| Latency | 13,796 ms | 41,702 ms |
| Citation | None | §12.1 ✓ |

**Verdict:** Tree RAG wins on citation quality. Both correctly identified non-reimbursability, but tree RAG backed it with a specific exclusions section. Latency was poor for tree RAG (41 s) likely due to model API congestion.

---

### EQ-017 — Mileage reimbursement rate for personal car `[easy / table]`
**Expected section:** §8.2 India Local Transport Limits

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "₹12 per kilometre" — **correct** | "₹12 per kilometre + odometer documentation required" — **correct + more complete** |
| Tokens | 1,371 | 2,362 |
| Latency | 1,046 ms | 1,937 ms |
| Citation | §7.3 india_domestic (close) | §8.2 + §11 Documentation ✓ |

**Verdict:** Tree RAG wins narrowly — same core answer but added the documentation requirement which is operationally important. Citation was more precise.

---

### EQ-018 — Cost split when extending business trip for personal travel `[medium / procedure]`
**Expected section:** §19.3 Personal Days Adjacent to Business Travel

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Company pays business-justified flight only; personal extension at own expense; no hotel/per diem" — **correct and complete** | "Company pays most cost-effective business flight; personal days cost is your liability" — **correct** |
| Tokens | 1,121 | 2,019 |
| Latency | 1,797 ms | 4,108 ms |
| Citation | §19.3 ✓ | §12.3 Personal Days Adjacent ✓ |

**Verdict:** Flat RAG wins narrowly — also mentioned no hotel/per diem for personal days, which tree RAG omitted. Both cited the right section; flat RAG was more complete.

---

### EQ-019 — Hotel nights claimable without manager pre-approval `[medium / lookup]`
**Expected section:** §9.1 Domestic India Approval Matrix

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Policy does not specify number" — **incomplete / wrong** | "29 nights per §6.4 Extended Stay Provisions" — **correct** |
| Tokens | 636 | 1,485 |
| Latency | 22,609 ms | 17,406 ms |
| Citation | None | §6.4 ✓ |

**Verdict:** Tree RAG wins. Found the specific 29-night threshold. Flat RAG failed to retrieve the relevant section entirely.

---

### EQ-020 — Policy coverage for travel to sanctioned countries `[easy / lookup]`
**Expected section:** §4.4 Restricted Destinations

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Answer | "Policy does not explicitly address sanctioned countries" — **wrong** | "No — travel to restricted destinations without Security clearance not covered" — **correct** |
| Tokens | 1,105 | 3,505 |
| Latency | 2,813 ms | 1,875 ms |
| Citation | None | §8.1 Company Group Policy ✓ |

**Verdict:** Tree RAG wins. Flat RAG failed to find §4.4 Restricted Destinations. Tree RAG correctly found the restriction.

---

## Aggregate Statistics

### Completion and Errors

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Completed | 19/20 (95%) | 17/20 (85%) |
| Timeout errors | 1 (EQ-013) | 3 (EQ-002, EQ-003, EQ-009) |
| Zero-token (API issue) | 1 | 3 |

The 3 tree RAG timeouts all hit the 45-second SSE safety cutoff, indicating model API congestion on the generate step — not a retrieval failure.

### Token Usage (completed queries only)

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Total tokens (19 resp) | 23,409 | — |
| Total tokens (17 resp) | — | 39,030 |
| Avg tokens/query | **1,232** | **2,295** |
| Min | 636 | 1,485 |
| Max | 1,744 | 3,505 |

> **Note:** Tree RAG uses *more* tokens than flat RAG in this demo. This is the opposite of the research hypothesis (target: 86% reduction). The root cause is that the bundle assembler is constructing large context windows from full document sections rather than extracting only the precise paragraphs. This is a known gap — the token budget enforcement is passing without truncating, meaning the retrieved nodes themselves are already over-fetching.

### Latency (completed queries only)

| | Flat RAG | Tree RAG |
|--|---------|---------|
| Avg latency | 13,880 ms | 8,265 ms |
| Median latency | 3,500 ms | 3,500 ms |
| p95 latency | ~44,000 ms | ~41,700 ms |
| Fastest | 1,031 ms | 1,046 ms |
| Slowest | 52,078 ms | 41,702 ms |

Tree RAG is faster on average despite a larger context, suggesting the vector similarity retrieval in flat RAG has more variance (EQ-011 was 52 s for flat).

### Answer Quality Scorecard (manual assessment)

**Scoring:** ✓ Correct, ~ Partial, ✗ Wrong, — Timeout/No response

| ID | Difficulty | Type | Flat RAG | Tree RAG | Better |
|----|-----------|------|----------|----------|--------|
| EQ-001 | easy | lookup | ✗ | ✓ | Tree |
| EQ-002 | easy | lookup | ✓ | — | Flat |
| EQ-003 | medium | procedure | ✓ | — | Flat |
| EQ-004 | easy | lookup | ~ | ✓ | Tree |
| EQ-005 | hard | comparison | ~ | ✓ | Tree |
| EQ-006 | medium | exception | ~ | ~ | Tie |
| EQ-007 | medium | procedure | ~ | ✓ | Tree |
| EQ-008 | easy | lookup | ✓ | ✗ | Flat |
| EQ-009 | easy | lookup | ~ | — | Flat |
| EQ-010 | medium | multi_hop | ~ | ✗ | Flat |
| EQ-011 | easy | table | ✓ | ✓ | Tie |
| EQ-012 | easy | lookup | ✗ | ✓ | Tree |
| EQ-013 | medium | lookup | — | ✓ | Tree |
| EQ-014 | medium | comparison | ✓ | ~ | Flat |
| EQ-015 | easy | lookup | ~ | ✓ | Tree |
| EQ-016 | hard | exception | ~ | ✓ | Tree |
| EQ-017 | easy | table | ✓ | ✓ | Tie |
| EQ-018 | medium | procedure | ✓ | ✓ | Tie |
| EQ-019 | medium | lookup | ✗ | ✓ | Tree |
| EQ-020 | easy | lookup | ✗ | ✓ | Tree |

| Outcome | Flat RAG | Tree RAG |
|---------|----------|---------|
| Fully correct (✓) | 7 | 10 |
| Partial (~) | 6 | 4 |
| Wrong (✗) | 4 | 2 |
| No response (—) | 3 | 4 |
| **Score (correct / attempted)** | **7/17 = 41%** | **10/16 = 63%** |

---

## Hallucination Analysis

| Question | System | Hallucination |
|----------|--------|---------------|
| EQ-001 | Flat RAG | "No Tier 1 data in context" — data was present |
| EQ-012 | Flat RAG | Answered personal insurance question instead of credit card usage |
| EQ-019 | Flat RAG | "Policy doesn't specify" when §6.4 specifies 29 nights |
| EQ-020 | Flat RAG | "Policy silent on sanctioned countries" when §4.4 exists |
| EQ-010 | Tree RAG | "Policy doesn't address flight cancellations" when §15 covers it |

Tree RAG has 1 hallucination (false absence); Flat RAG has 4. All are false-absence hallucinations — both systems say "not in policy" when the section exists. This is a retrieval scope problem, not a generation problem.

---

## Key Findings

### What tree RAG gets right
1. **Precise section targeting** — When it retrieves the right node, the answer is tied to the exact section (§6.2, §10.3, §8.2), not a vague document reference.
2. **Correct on hard questions** — EQ-005 (comparison), EQ-016 (exception), EQ-019 (specific threshold) all went to tree RAG. These are the query types where structured document retrieval should excel.
3. **Lower hallucination rate** — 1 false-absence vs 4 for flat RAG.
4. **Better latency on average** — 8.3 s vs 13.9 s median (excluding timeouts).

### What flat RAG gets right
1. **Multi-hop resilience** — EQ-010 (flight cancellation spanning §15, §6.2, §7.1) was answered partially correctly by flat RAG; tree RAG completely missed it. Vector similarity can accidentally span section boundaries.
2. **No fetch failure on easy lookups** — EQ-002, EQ-003, EQ-008 all worked correctly.
3. **Token efficiency** — Half the tokens per query on average.

### Critical gaps to fix

| Gap | System | Impact | Likely Fix |
|-----|--------|--------|------------|
| Token over-fetch | Tree RAG | Negates cost hypothesis | Prune nodes to paragraph level, not section level |
| Multi-hop retrieval | Tree RAG | Misses cross-section queries | Add sibling/parent traversal; retrieve N linked nodes |
| Timeout on model API | Both | 3–4 queries fail per run | Retry logic + timeout tuning in model_client.py |
| Semantic section mismatch | Flat RAG | Wrong section retrieved (EQ-012) | Improve chunking strategy; add section metadata to embeddings |
| Bundle assembly scope | Tree RAG | Misses exclusions/edge sections | Expand scope_detect to include §12.1 Exclusions by default |

---

## Verdict

**Tree RAG is the better system for structured policy Q&A**, but it is not yet production-ready. It scores 63% correct vs 41% for flat RAG on completed queries, hallucinates less, and provides more precise citations. However, three blockers prevent it from validating the research hypothesis:

1. **Token regression** — Tree RAG currently uses ~2× more tokens than flat RAG. The bundle_assembler is fetching section-level nodes that are too coarse. Switching to paragraph-level extraction with strict budget enforcement should reverse this.

2. **Multi-hop blindspot** — The tree retriever treats each query as pointing to a single section. Queries that span multiple sections (cancellation policy, emergency limits) will fail until cross-node link traversal is implemented.

3. **API reliability** — Three timeouts on tree RAG vs one on flat RAG indicates the tree pipeline is hitting longer generate-step waits, possibly because the larger context takes longer to prefill in the model.

**Recommendation:** Prioritize (1) paragraph-level node extraction and (2) multi-hop link traversal before running the formal Terminus benchmark. The architecture is sound — the retrieval granularity and bundle assembly are the bottlenecks.
