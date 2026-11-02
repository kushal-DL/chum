# Findings — MASH-QA as an ASP document-tree benchmark (negative result)

**Date:** 2026-07-10
**Branch:** experiment/doc-tree-retrieval-redesign
**Story:** E10-S16
**Decision:** MASH-QA implemented and verified as loadable, then **dropped as a headline benchmark**. Loader + span-recall metric retained as infrastructure.

---

## Why MASH-QA was investigated

A construct-validity review (2026-07-10) concluded ASP's document-tree evaluation was too narrow: CUAD (legal contracts) is the only clean doc-tree fit, HotpotQA is an out-of-shape control (cross-document multi-hop = knowledge-graph shape), and MAUD is a weak multiple-choice signal. We wanted domain diversity — ideally healthcare/insurance where accuracy is paramount.

A verified dataset scout ranked **MASH-QA** (Multiple Answer Spans Healthcare QA, WebMD consumer-health articles, Apache-2.0) as the best clean healthcare doc-tree fit on paper: one long document per question, multi-span extractive answers that double as gold evidence, permissive license.

## What we built (and kept)

- `utils/loaders/document_qa/mashqa.py` — downloads the span-preserving Google-Drive archive via `gdown` (MASH-QA is not on the HF hub in span-preserving form), caches to `data/raw/mashqa/`, parses the `*_full.json` split.
- `datasets/mashqa.py` — projects to the uniform `DatasetItem`.
- `metrics/span_recall.py` — **retrieval-recall over gold evidence spans**: the fraction of gold spans present in the assembled context, generation-independent. This directly measures the thing ASP claims to improve and is reusable by any future dataset that ships gold spans. **This is the durable win from this work.**
- Registered in `runner.py` / `datasets/__init__.py`; `gdown` added to `pyproject.toml`.

Verification passed: 3,493 test items parse; documents are genuinely long (median 4,517 chars, up to ~18k); multi-span (median 3 spans, up to 26); every gold span is a verbatim substring of its document (span-recall = 1.0 vs the full document across 300 sampled rows); a limit-5 ASP+flat smoke run completed and both answer-F1 and span-recall populated in the results JSON.

## The disqualifying finding

**MASH-QA's `context` is a single flat prose blob with no heading structure.**

- **0 of 521** unique article contexts contain any newlines or heading markers — the WebMD articles were flattened to running prose during dataset construction.
- ASP's parser/dispatcher therefore returns an empty node list and **falls back to a single paragraph node** (`[DocTree] dispatcher failed: node list is empty → fallback`; `nodes=1→1→1` in the smoke log). Tree traversal, parent/sibling expansion, and authority-ordered assembly — the load-bearing ASP mechanisms — never fire.
- Consequence on the limit-5 smoke run: ASP degraded to ≈flat (**F1 26.02 vs flat 26.19**; span-recall 100% for both because the whole short article fits in the bundle either way).

Running a full 500-item head-to-head as-is would produce a **misleading null result** (ASP ≈ flat) that says nothing about tree retrieval — the same category of error as HotpotQA, but caused by *missing* structure rather than *wrong* structure. Per the programme's "negative results are first-class artifacts" rule, we record it rather than bury it.

Note: the structure is partially *recoverable* — 43% of articles retain inline subheadings as short colon-phrases (e.g. "Hemodialysis:", "Peritoneal dialysis:"), and every record ships a `sent_list`. A general structure-recovery step in the parser (promote inline colon-subheadings in flat prose to sections) would let ASP exercise the tree on this and any other PDF-extracted enterprise document that lost its heading markup. That was considered and **deferred** (see Decision) — it is a parser-enhancement track, not a dataset add.

## Decision

- **Drop MASH-QA as a headline ASP-vs-flat benchmark.** It does not test the mechanism as distributed.
- **Keep the code.** The loader works and the `span_recall` metric is reusable infrastructure for any future gold-evidence dataset.
- **Do not run** the full 500-item benchmark.

## Implications for dataset selection

The clean-fit public healthcare QA dataset turned out to be flattened prose. This tightens the inclusion filter: a dataset must not only *have* long structured source documents, it must **preserve that structure in the distributed text** (newlines / headings / markup), not just in the original source. Add a pre-flight structure check (newline/heading density, parser node count > 1) to any future candidate before committing to a run.

## Next options (not actioned here)

1. **General structure-recovery parser enhancement** (promote inline colon-subheadings; also unlocks MASH-QA) — a principled, dataset-agnostic parser story, gated behind a validation slice.
2. **PrivacyQA / ContractNLI** — verified secondary doc-tree adds with gold evidence spans; check their distributed structure with the new pre-flight check before committing.
3. **In-house structured healthcare/insurance eval** over public source docs that retain structure (SBC health-plan summaries, FDA drug labels, HICRIC coverage rules).

The retrieval-side experiment queue is unchanged: **EE4-X2** (score-variance gate for the context floor) remains the next step.
