# ADR 0001 — Reject the global `min_context_tokens` floor; adopt recall on genuinely-structured datasets

- **Status:** Accepted
- **Date:** 2026-07-26
- **Deciders:** Kushal Sharma
- **Branch:** `experiment/doc-tree-retrieval-redesign`
- **Supersedes:** the "partially promoted" standing of EE4-X1 (`docs/research/ee4_x1_findings.md`)
- **Related:** [`ee4_x1_findings.md`](../research/ee4_x1_findings.md), [`ee_struct_findings.md`](../research/ee_struct_findings.md), [`doc_tree_rag_case_study_v2.md`](../research/doc_tree_rag_case_study_v2.md), experiment-backlog `epic-ee4-context-floor.md`

---

## Context

ASP's document-tree retrieval claims that **structure-aware traversal** —
localise a seed node by BM25, then expand along the tree (parent/ancestor walk,
sibling scan) and assemble an authority-ordered bundle — beats commodity flat
hybrid RAG on enterprise documents. The claim is only meaningful if the win is
attributable to the *retrieval mechanism*, not to an incidental knob.

During EE4 we introduced a `min_context_tokens` floor (EE4-X1, run tag
`rr_ctx700`): if the assembled bundle fell below 700 tokens, the assembler
force-expanded to parents/siblings until the floor was met. It was introduced to
fix MAUD, where ASP was retrieving ~497 context tokens and trailing flat.

Two things have since become clear, and this ADR records the decision they force.

### 1. The floor is a dataset-specific crutch, and the MAUD "win" is mostly flat regressing

Authoritative numbers, read directly from the result JSONs (not summaries):

| Dataset | Model | Metric | ASP no floor (`rr_final`) | Flat no floor | ASP + floor (`rr_ctx700`) | Flat + floor |
|---|---|---|---:|---:|---:|---:|
| MAUD | Nemotron (m3) | Accuracy% | 36.2 | 39.4 | 36.2 | 32.0 |
| MAUD | Mistral (m4)  | Accuracy% | 37.8 | 41.6 | 36.4 | 35.2 |
| CUAD | Nemotron (m5) | EM% | 86.8 | 70.0 | 81.0 | 65.4 |
| CUAD | Mistral (m6)  | EM% | 88.6 | 69.9 | 78.8 | 64.8 |

Reading the MAUD rows carefully dismantles the headline "+4.2 pp / +1.2 pp win":

- **ASP accuracy barely moved** with the floor (36.2 → 36.2 Nem; 37.8 → **36.4** Mis — it actually *dropped* 1.4 pp for Mistral).
- The apparent ASP-over-flat gap opens because **flat collapsed** (39.4 → 32.0 Nem; 41.6 → 35.2 Mis) — the same 700-token floor was applied to flat and hurt it more.
- So the "win" is an artifact of a shared knob degrading the comparator, not of ASP's mechanism recovering the answer. That is exactly the confound the charter's "measured against flat" rule exists to catch.

**On CUAD the floor is a no-op, and the ctx700 CUAD drop is NOT floor dilution.**
Per-row inspection of the `ctx700` vs `rr_final` CUAD checkpoints shows **identical**
context: avg 240 tok, median 231, max 522, **zero rows ≥ 700 tokens** in either run.
The floor never fired on CUAD — CUAD's parsed/fallback nodes carry no
`parent_node_id`/siblings, so the floor's BFS expansion queue is empty. The lower
`ctx700` CUAD EM (81.0 / 78.8 vs 86.8 / 88.6) is therefore **run-to-run inference
drift** (the `ctx700` run also shows a completely different latency profile —
~75–130 ms vs ~6,700–8,800 ms — indicating a different inference environment), **not**
context dilution. An earlier draft of this ADR wrongly attributed the CUAD drop to the
floor padding bundles with boilerplate; the per-row data does not support that and the
claim is retracted.

**The clean CUAD baseline is ~87–90 % EM, not ~79 %.** The "stable ~79 %" figure in the
prior handoff conflated three different things: floor runs (`ctx700`, drift), prompt
experiments (`cot_inline`, `soft_ms`, `source_tag` at 75–77 %), and the 3-backend
struct comparison (66.8–73.6 % on different slots). Every **standard-prompt, no-floor**
run — the exact config used for the new datasets — scores **86.8–90.2 % EM** (`rr`,
`rr_final`, `llama_base`, `cot_ms`). That is the correct no-regression anchor.

### 2. MAUD needed the floor because MAUD is the wrong document shape

`EE-Struct` (`ee_struct_findings.md`) already showed that on MAUD, structure
*without* expansion is **worse than flat by 8–13 pp** — because MAUD parses into
a flat clause list of very short sections. There is no genuine hierarchy for
tree traversal to exploit, so the only way to give the model enough context was
to bolt on a token floor. A dataset that requires a hyperparameter to look
competitive is telling us it is outside the shape ASP targets — a *boundary*,
not a core case. HotpotQA is the dual boundary: short Wikipedia intros
answerable from world knowledge, where ASP trails flat by ~4 pp F1 (the gap is
entirely false-absence over-triggering; wrong-answer rates match flat).

## Decision

**1. Do not ship the global `min_context_tokens` floor.** It stays available as
an experimental flag (default `0` = off) for ablation studies, but no headline
ASP result may depend on it, and it must never be enabled globally in a
production or paper configuration. EE4-X1 is reclassified from "partially
promoted" to **not promoted** as a production mechanism. The floor is a
**MAUD-specific crutch**: it is a no-op on CUAD (no parents/siblings to expand),
and on MAUD it only opens an ASP-over-flat gap by degrading the flat comparator.

**2. Prove the document-tree claim on datasets that are genuinely long and
hierarchical**, where traversal earns its keep without any crutch. We add:
- **ObliQA** (RegNLP / ADGM financial regulation) — 40 rulebooks, deep dotted
  `PassageID` hierarchy, section-grounded gold passage sets.
- **GovReport-QS** (HIBRIDS) — long GAO/CRS reports with multi-level section
  depth and a question→section alignment.

**3. Score these datasets by recall, not EM/F1.** Both are retrieval tasks with
no short gold answer string (ObliQA has none; GovReport-QS's gold is an
abstractive summary that never appears verbatim). We measure **span/section
recall** — the fraction of gold passages/sections whose text is present in the
assembled context — via the existing generation-independent
`metrics/span_recall.py`, aggregated same-condition for ASP and flat. EM/F1 are
still emitted but are expected to be ~0 and are not the primary metric. This
mirrors the EE1 decision that recall, not F1, is the primary retrieval metric
(F1 masks retrieval failures behind LLM world knowledge).

## Criteria for a "document-tree" dataset (the reusable output)

A dataset (and, in production, a document) is a valid document-tree target when:

1. **Deep hierarchy** — genuine ≥3-level nesting, not a flat clause list.
2. **Answer displacement** — the answer sometimes lives in a sibling/parent of
   the keyword-matched section (this is what traversal fixes and flat cannot).
3. **Cross-references / defined terms** — so cross-ref resolution earns its keep.
4. **Section-grounded gold** — retrieval quality is measurable, not shortcut by
   world knowledge.
5. **Length** — long enough that flat top-k genuinely misses context.

**Falsifiable test:** ASP should win with expansion *on*, degrade gracefully
with expansion *off*, and need **no dataset-specific hyperparameter**. MAUD fails
criteria 1–2 (flat, short); HotpotQA fails 1, 3, 4. ObliQA and GovReport-QS pass.

> **Prediction outcome (2026-07-27): the "advantage concentrates on multi-passage" sub-hypothesis was FALSIFIED.**
> This ADR predicted ASP's recall advantage would concentrate on the multi-passage /
> multi-section subset (where gold straddles siblings/parents). The subset analysis shows
> the opposite: on ObliQA ASP leads by **+42 pp on single-passage** questions vs **+26 pp
> on multi-passage** (multi-passage is harder for *both* systems — ASP recall falls
> 85.8%→44.9% — which compresses the delta). GovReport-QS shows the same shape
> (+98 pp single-section vs +70 pp multi-section). ASP still wins **every** subset, and the
> headline dataset-level win stands; but the specific mechanism story is corrected: ASP's
> edge comes from retrieving the *right, intact* unit even on single-target questions, not
> from a multi-hop-specific traversal benefit. Recorded rather than buried.

## Consequences

**Positive**
- The paper claim rests on the mechanism, not a knob. No result is hostage to a
  700-token constant.
- CUAD's clean +15–18 pp EM win stands on the standard, no-floor config
  (~87–90 % EM absolute); it was never actually affected by the floor.
- The dataset-selection criteria above become a reusable contribution (a
  routing rule for the shape router: *when* to send a query to document-tree).

**Negative / costs**
- MAUD loses its only ASP-positive configuration. We reframe MAUD (and HotpotQA)
  as documented **applicability boundaries**, not wins. This is more honest and,
  we judge, more publishable than a knob-dependent MAUD number.
- We take on two new dataset integrations and a recall-first evaluation path.

**Neutral**
- `min_context_tokens` remains in the codebase as an off-by-default ablation
  flag; the score-variance-gated variant (EE4-X2) is **shelved** — there is no
  longer a production floor for it to gate.

## Baselines to (re)establish under this decision

All runs use the best current document-tree configuration (round-robin seed
selection, EE2-X1) with **`--min-context-tokens 0`** (no floor). Run tag
`rr_notokenfloor`. Same-condition ASP vs flat, n=500, slots 1/2 = ObliQA,
3/4 = GovReport-QS (see the handoff doc for exact commands). CUAD/MAUD/HotpotQA
no-floor baselines are already on record in `rr_final` (numbers above).
