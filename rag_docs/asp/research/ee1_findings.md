# EE1 — Findings & Decision

**Companion to:** [`docs/research/ee1_recall_audit.md`](ee1_recall_audit.md) (auto-generated raw numbers)
**Backlog:** [experiment-backlog/epic-ee1-recall-audit.md](../../experiment-backlog/epic-ee1-recall-audit.md)
**Date:** 2026-06-20
**Status:** EE1-X1, X2, X4 complete. X3 (BM25-vs-cross-encoder ablation) deferred — the headline finding below makes X3 lower priority.

---

## TL;DR

The retrieval audit produced a result more decisive than expected. Two findings:

1. **HotpotQA: ASP retrieves all 6 citations from the same single article in 500 of 500 rows.** Source diversity is exactly 1.0 across the entire benchmark. The fix from E10-S15 (prebuilt_nodes per paragraph, equal authority levels) did not change this — equal authorities did not produce cross-source seeding. **Recall@6 = 0.11**, but the LLM still scores F1 = 0.66 by leaning on world knowledge / distractor leak.
2. **MAUD: cannot diagnose locus from existing data.** The `gold` field is a candidate label like "All Cash" — it is not text that appears in the contract. A 60%-of-tokens substring proxy reads soft-recall as 0.6%, which is not a retrieval signal — it is a measurement-method failure. **MAUD requires re-instrumented retrieval traces or an LLM-judge to diagnose.**

The HotpotQA finding alone is enough to direct the redesign. The diagnosis is unambiguous: **the retriever is structurally incapable of cross-document seed selection.** This is not a tuning problem. It is the design.

---

## What the numbers say

### HotpotQA M2 (500 hard questions, distractor setting)

| Metric | ASP doc-tree | Flat hybrid |
|---|---|---|
| F1 | 0.662 | 0.690 |
| EM | 0.524 | 0.548 |
| Mean recall@6 (hard) | **0.110** | (citations not logged) |
| Mean distinct citation sources / row | **1.00** | (n/a) |
| Rows with both gold articles cited | **0 / 500** | (n/a) |
| Rows with one gold article cited | 110 / 500 (22%) | (n/a) |
| Rows with neither gold article cited | 390 / 500 (78%) | (n/a) |

The decomposition of failures (F1 < 0.5):

| Bucket | Count | % of failures |
|---|---|---|
| retrieval_miss (recall = 0) | 115 | 75.7% |
| retrieval_partial (recall = 0.5) | 37 | 24.3% |
| retrieval_hit_genmiss (recall = 1, answer wrong) | **0** | 0.0% |

There is not a single row out of 500 where ASP retrieved both supporting articles. **The "retrieval is fine, generation is failing" hypothesis is dead.** Every wrong answer is a retrieval failure of some degree.

The recall × F1 quadrant table:

|  | low F1 (<0.5) | high F1 (≥0.5) |
|---|---|---|
| high recall (≥0.5) | 37 | 73 |
| low recall (<0.5) | 115 | **275** |

Of the 348 high-F1 rows, **275 (79%) have low recall.** The model is producing correct-looking answers without seeing the supporting documents in three of four cases. This is the LLM filling in from training-time world knowledge or pattern-matching distractor articles for plausible answers — not retrieval-grounded answering. **F1 = 0.66 is a misleading number.** The retriever has effectively failed on most queries; the LLM is rescuing it with priors.

### MAUD M2 (500 questions)

| Metric | ASP doc-tree |
|---|---|
| Accuracy | 0.358 |
| 2-candidate accuracy | 0.328 (worse than 50% random) |
| Mean soft-recall (proxy) | 0.006 |
| Rows with single retrieved node | 265 / 500 (53%) |

**The soft-recall proxy is broken**, not the retriever. MAUD's `gold` field is a label inferred by annotators ("All Cash", "All Stock"). The text in the contract that supports that label talks about "consideration", "merger", "common stock conversion" — none of which contain "all cash" verbatim. A token-substring proxy on label strings is the wrong measurement.

What we *can* see directly:

- 53% of MAUD rows retrieve exactly **one** node. The retriever is repeatedly selecting a single short clause and stopping. The pipeline's tree-traversal expansion is not firing on MAUD because the parser is producing flat structures (echo of the diagnosis in `docs/asp_implementation_comparison.md`).
- **The 32.8% accuracy on 2-candidate questions is real and unexplained by data alone.** It could be retrieval (wrong clause) or generation (right clause, miscounted). EE1 cannot distinguish without re-instrumented traces.

---

## What changed in my mental model

Before the audit:

- I thought the HotpotQA retriever was *suboptimally* diversified — pulling 4-of-6 citations from the dominant article rather than 3-and-3. Maybe per-document quotas or MMR would close the gap.
- I thought MAUD's failures were a mix of retrieval and generation, with retrieval probably the larger slice but uncertain.

After the audit:

- HotpotQA: the retriever has **zero** diversification. 1.00 mean distinct sources, no exceptions. The seed-quota fix is no longer "tune the diversity weight" — it is "introduce diversity at all." This is structurally different.
- HotpotQA: the LLM is masking 79% of recall failures with world knowledge. **The benchmark is being passed for the wrong reasons.** F1 = 0.66 is not retrieval competence — it is GPT-class priors filling in for a broken retriever. This means any improvement in recall will likely show up as a *small* F1 gain (because the LLM was already getting many right by hallucinating-plausibly), but the *quality* of the answers will improve in ways F1 can't measure (grounded vs ungrounded).
- MAUD's failure locus is unknown and unknowable from existing data. We need either (a) re-run with full retrieval-trace logging including the actual node content text, or (b) an LLM-judge that reads the cited content and the gold answer and decides "is the answer derivable?"

The single most important methodological consequence: **F1 on HotpotQA is no longer my primary success metric.** Recall@K is. A redesign that lifts ASP recall@6 from 0.11 to 0.50 might *not* lift F1 dramatically, because the LLM was already scoring F1 by other means. But it will produce *grounded* answers, which is what the ASP claim actually says.

---

## Decision: what to do next

The original plan from the charter was: **EE1 (audit) → EE2-EE5 (redesign directions) → ship.** The audit said "retrieval is the locus, EE2-EE5 are justified."

But the audit said something stronger than that for HotpotQA, and something weaker than expected for MAUD. The plan needs adjustment:

### For HotpotQA — proceed with EE2 directly

The `0 / 500` source-diversity finding is a smoking gun. Per the original Direction A (dispersion-aware seeding), but with a sharper formulation:

**Hypothesis EE2-X1:** Enforcing a per-`doc_slug` seed quota in the BM25 step lifts recall@6 from 0.11 toward the maximum achievable on the 2-source HotpotQA distractor setup.

**Test:** one-paragraph patch — when selecting top-N seeds from the BM25 score map, group by `doc_slug` and round-robin. Run M2 again on the resulting pipeline. Compare recall@6 and F1.

The original recommendation in the conversation was "this is hours of work and conceptually aligned with Beam Retrieval's multi-hypothesis principle." The audit confirms this is exactly the right next step — but it should be presented as **the floor** of EE2, not the ceiling. After we know the diversity-floor F1 number, the principled subtree-objective from the deep-research recommendation is what we build on top.

### For MAUD — open EE1-X5 (the retrieval-trace-and-judge story) before any redesign

MAUD redesign on current data is throwing darts. Before we touch the retriever for MAUD, we need to know whether the failures are retrieval or generation, and we need a measurement that doesn't proxy on label strings. Two options:

1. **EE1-X5a — Re-run MAUD with full retrieval trace logging.** Keep the same pipeline; add a flag to dump every retrieved node's full content text into a per-row trace file. Then run an LLM-judge (cheap model, batch) over the traces to score "given this content, is the gold answer derivable?" Yes/no per row.
2. **EE1-X5b — Synthetic gold-span recovery.** For each MAUD question, prompt an LLM-judge against the *full contract* (not the retrieved chunks) to identify the supporting span. Then check whether ASP's retrieved chunks contain that span. This gives us the recall-against-true-evidence number.

EE1-X5a is faster (~half day, reuses existing traces). EE1-X5b is cleaner (~1 day, more rigorous). I'd run X5a first and only escalate to X5b if X5a's verdict is ambiguous.

**Order of operations:**
1. EE2-X1 (per-`doc_slug` quota patch) — same day implementation, ~2-hour benchmark run.
2. EE1-X5a (MAUD trace + LLM-judge) — half day implementation, ~1-hour run.
3. Read both. Decide whether MAUD needs a separate EE epic (likely) or whether the diversity fix from EE2 also helps MAUD because some MAUD failures are also "wrong clause out of many candidates" rather than "right clause but bad reasoning."

This is still single-track (per the user's decision). EE2-X1 and EE1-X5a are sequential, but they are the same kind of low-risk audit step — measure first, build later.

---

## What this means for the principled redesign

The deep-research recommendation #5 was *MemWalker-style agentic tree-walking with revert action*. After this audit, I think MemWalker is more interesting than I gave it credit for, and **for a reason the audit just exposed**: ASP's current retriever has no notion that "the top-K candidate set is degenerate" — that all 6 picks are from the same source. A retriever that *measures* its own candidate-set quality, then revises (revert + descend elsewhere) when the measurement is poor, is exactly the missing capability.

I won't commit to MemWalker yet. EE2-X1 is the right next step. But the audit has slightly raised my prior on agentic / iterative directions and lowered it on pure-static-scoring directions like "score the tree, not the nodes." Static scoring assumes you can find the right candidates in one pass. The audit shows ASP's static scoring is a one-pass commitment to a degenerate set. Iterative methods can recover from that; static methods, however well-tuned, cannot.

This will get re-examined after EE2-X1 lands.

---

## What we will NOT do based on this audit

- **We will not switch off F1 as a reported metric.** Other people will judge ASP by F1; we report it. But it is no longer our internal compass.
- **We will not "fix" the LLM masking.** Some teams would interpret the 79% high-F1-low-recall finding as "we should change the prompt to refuse when retrieval is empty." We will not. The LLM behaviour is a measurement artifact, not a defect — fixing it would distort our own diagnostic signal. Recall is the diagnostic; F1 is the report.
- **We will not extend the soft-recall proxy on MAUD with more clever string-matching.** The proxy is wrong in kind, not wrong in tuning. EE1-X5 gives us a real measurement.

---

## Open questions to resolve in EE1-X3 / EE1-X5

- Is the cross-encoder reranker dropping correct candidates, or is BM25 never producing them? (X3 — needs re-run with BM25 top-20 logged separately from cross-encoder top-6.)
- On MAUD, is the 53%-of-rows-with-1-retrieved-node a parser failure (the contract isn't getting structured into a tree) or a retrieval failure (the BM25 only finds one node above threshold)?
- Does the diversity-quota patch (EE2-X1) produce non-degenerate citation sets even on questions where flat hybrid does well? Or does it trade single-source-but-correct for diverse-but-wrong?

---

## Cross-references

- Charter: [doc_tree_retrieval_redesign.md](doc_tree_retrieval_redesign.md)
- Auto-generated audit data: [ee1_recall_audit.md](ee1_recall_audit.md)
- Backlog: [../../experiment-backlog/epic-ee1-recall-audit.md](../../experiment-backlog/epic-ee1-recall-audit.md)
- Bundle assembler (where citations are rendered): [../../src/asp/benchmarks/retrieval/bundle_assembler.py](../../src/asp/benchmarks/retrieval/bundle_assembler.py)
- BM25 + tree-traversal (where the seed-collapse happens): [../../src/asp/benchmarks/retrieval/bm25.py](../../src/asp/benchmarks/retrieval/bm25.py), [../../src/asp/benchmarks/retrieval/tree_traversal.py](../../src/asp/benchmarks/retrieval/tree_traversal.py)
