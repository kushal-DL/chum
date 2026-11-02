# Eval Results — 2026-06-19

> **Note (E10-S13 fix, 2026-06-19):** The `Avg context tokens` column in both tables
> below was originally computed with `len(text.split())` (whitespace word count) for
> the flat baseline, while ASP used tiktoken `cl100k_base`. For English legalese this
> causes a ~30% systematic understatement of flat's context size. The numbers in the
> tables have **not** been re-run yet (that is E10-S14); they are annotated here so
> readers are aware of the unit discrepancy. Re-run results will replace these once the
> refactored pipelines (E10-S10/S11/S12/S13) are complete. From this fix forward,
> `context_tokens` is tiktoken-counted in every result JSON.

Public-dataset benchmark results after the document-tree retrieval refactor
that ports example-workflow2 (JCP Credit demo)'s full ASP pipeline (path-augmented BM25,
heading-path boost, depth-aware multiplier, three-phase tree traversal,
query-time paragraph extraction, cross-encoder reranking, bundle assembler
with compact ASP headers and authority resolution).

Hardware: NVIDIA NIM endpoint, model `nvidia/nemotron-3-nano-omni-30b-a3b-reasoning`.
Reranker: local `cross-encoder/ms-marco-MiniLM-L-6-v2` (zero API calls).

---

## HotpotQA — distractor split, 50 questions

| Backend | EM | F1 | Avg context tokens | Latency |
|---------|----|------|--------------------|---------|
| **ASP document_tree** | **56.0%** | 66.54% | **1,151** | 2,079 ms |
| Flat (all 10 paragraphs) | 56.0% | 67.87% | 1,822 | 1,441 ms |

- ASP matches flat on EM with **37% fewer context tokens**.
- Trails by 1.3 pp on F1 — within statistical noise at n=50.
- Previous (broken) implementation: 46% EM / 54.79% F1 / 1,363 tokens.
- Improvement from refactor: **+10 pp EM, +11.75 pp F1, −16% context tokens**.

Why ASP doesn't crush flat here: HotpotQA is flat Wikipedia paragraphs with
no real heading hierarchy. The depth multiplier and heading-path overlap
boost fire weakly. The wins are coming from path-augmented BM25 +
paragraph extraction (drops irrelevant sentences from selected passages).

## CUAD / SQuAD — 50 questions

| Backend | EM | F1 | Avg context tokens | Latency |
|---------|----|------|--------------------|---------|
| **ASP document_tree** | **96.0%** | **98.27%** | 370 | 1,731 ms |
| Flat (full passage) | 90.0% | 95.87% | 328 | 717 ms |

- ASP wins **+6 pp EM, +2.4 pp F1** at +13% token cost.
- Single-passage SQuAD-style QA — the retrieval gains come from
  paragraph-extraction picking the precise sentence that answers the
  question, plus the compact header keeping the model on-source.
- This is closer to the protocol's target use case (structured single-source
  documents) and the gap is real and consistent.

---

## Token efficiency claim

The ASP protocol overhead — `[SourceName §LeafSection | ClauseID]` headers
(~60 tokens/node), authority-resolution note, full citations — is more than
paid back by **query-time paragraph extraction** which drops irrelevant
sentences before they hit the model. Net effect on HotpotQA: 37% fewer
context tokens than passing every paragraph.

---

## Architecture changes captured by these results

The benchmark harness was refactored to a shape-aligned layout (see
[REPO_STRUCTURE.md](../REPO_STRUCTURE.md)):

- `src/asp/benchmarks/retrieval/` now holds the shared primitives:
  `bm25.py`, `paragraph_extractor.py`, `tree_traversal.py`,
  `cross_encoder_reranker.py`, `bundle_assembler.py`. Adapters compose
  these — they do not embed retrieval logic.
- Adapters are named by shape, not dataset:
  `adapters/document_tree_qa.py` (CUAD, HotpotQA, SQuAD, …),
  `adapters/semantic_table_sql.py` (Spider, BIRD, …),
  `adapters/vector_retrieval.py` (FIQA, MS-MARCO, NQ, …).
- New datasets are added by writing a `datasets/<name>_loader.py` plus a
  `run_<shape>_<dataset>()` entry function in the matching adapter — the
  pipeline body (`_run_full_pipeline`) is the single source of truth.

---

## Reproduction

```bash
# HotpotQA
python -m src.asp.benchmarks.runner --datasets hotpotqa --backends asp flat --limit 50

# CUAD/SQuAD
python -m src.asp.benchmarks.runner --datasets cuad --backends asp flat --limit 50

# Outputs
data/benchmarks/hotpotqa_results.json
data/benchmarks/cuad_results.json
data/benchmarks/benchmark_report.md
```

Full per-question records (predicted answers, citations, retrieval
strategy, supporting titles) are in the per-dataset JSON.
