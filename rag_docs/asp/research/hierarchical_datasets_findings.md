# Findings — Hierarchical retrieval datasets (ObliQA, GovReport-QS)

**Date:** 2026-07-27
**Branch:** `experiment/doc-tree-retrieval-redesign`
**Run tag:** `rr_notokenfloor`
**Status:** Complete (all 4 runs n=500, 0 errored rows)
**Related:** [ADR 0001](../adr/0001-reject-min-context-floor-adopt-recall-datasets.md), [`ee4_x1_findings.md`](ee4_x1_findings.md), [`ee_struct_findings.md`](ee_struct_findings.md), [`doc_tree_rag_case_study_v2.md`](doc_tree_rag_case_study_v2.md) §6.6

---

## Why these datasets

MAUD and HotpotQA are **applicability boundaries**, not core targets, for
document-tree retrieval (see ADR 0001):

- **MAUD** — flat clause list, short sections. ASP only "won" with the
  `min_context_tokens=700` floor, and that win was the flat comparator
  regressing under the same knob, not ASP improving (ASP 36.2→36.2 Nem;
  37.8→36.4 Mis; flat 39.4→32.0; 41.6→35.2). EE-Struct separately showed
  structure-without-expansion is 8–13 pp *worse* than flat on MAUD.
- **HotpotQA** — short Wikipedia intros, world-knowledge-answerable; the −4 pp
  F1 gap is false-absence over-triggering with wrong-answer parity.

Neither exercises the mechanism ASP claims (deep hierarchy + parent/sibling
expansion over section-grounded gold). ObliQA and GovReport-QS do.

## Dataset selection criteria (reusable)

A dataset (or production document) is a document-tree target when it has:
deep ≥3-level hierarchy; answer-displacement (gold in sibling/parent of the
keyword hit); cross-references/defined terms; section-grounded gold; and
length. **Falsifiable test:** ASP wins with expansion on, degrades gracefully
with it off, and needs no dataset-specific knob.

## ObliQA (RegNLP / ADGM financial regulation)

- **Source:** GitHub `RegNLP/ObliQADataset` (Apache-2.0), cached in
  `data/raw/obliqa/`. QA: `ObliQA_{train,dev,test}.json`
  (`QuestionID`, `Question`, `Passages[]` = `{DocumentID, PassageID, Passage}`).
  Corpus: `StructuredRegulatoryDocuments/{DocumentID}.json`.
- **Corpus loaded:** 40 documents, 13,732 passages.
- **Tree:** genuine nested tree from dotted `PassageID` (e.g.
  `14.2.3.Guidance.10.`). Smoke test on Document 1: 567 nodes, depth histogram
  `{0:1, 1:16, 2:64, 3:161, 4:159, 5:166}`, 566 nodes with parents, 179 with
  children. Gold node correctly parented (`14.2.3.Guidance.10.` at depth 5 under
  `14.2.3.Guidance`).
- **Metric:** span/passage recall (`avg_span_recall_pct`, `full_recall_pct`) —
  gold is passage references, no answer string. EM/F1 ~0 by construction.
- **Loader:** `src/asp/benchmarks/utils/loaders/document_qa/obliqa.py`,
  `src/asp/benchmarks/datasets/obliqa.py`.

## GovReport-QS (HIBRIDS, ACL 2022)

- **Source:** HF `launch/gov_report_qs`, config `paragraph`, loaded via the
  Hub's auto-converted Parquet mirror (the legacy loading script is rejected by
  `datasets>=3`). CC BY 4.0.
- **Tree:** nested tree from `document_sections` `depth`. Retrieval corpus is the
  **full report**; gold is only the sections aligned to the question's summary
  paragraph (`summary_paragraph_index`) — a proper subset, so recall is
  non-trivial. Smoke test: gold sections a subset of total tree sections; gold
  span text present in the document blob.
- **Metric:** section recall via the same `span_recall` path.
- **Loader:** `src/asp/benchmarks/utils/loaders/document_qa/govreport_qs.py`,
  `src/asp/benchmarks/datasets/govreport_qs.py`.

## How to run (baselines — no floor)

```bash
# ObliQA — slot 1 (Nemotron), slot 2 (Llama-49B). GovReport-QS — slots 3, 4.
# Pre-warm indices once (avoids the Chroma create-collection race across
# concurrent same-dataset processes), then run 4 single-slot processes.
.venv/Scripts/python.exe -m src.asp.benchmarks.runner \
  --datasets obliqa --backends asp flat --limit 500 \
  --run-tag rr_notokenfloor --model-slots 1 --rate-delay 3.0 --no-report &
# ... repeat for --model-slots 2 (obliqa), and --datasets govreport_qs --model-slots 3 / 4
```

Slot pools (from `.env`): 1/3/5 = Nemotron-30B (40 rpm shared), 2/4/6 =
Llama-3.3-Nemotron-Super-49B (40 rpm shared). Running 4 in parallel = 2 slots
per pool → `--rate-delay 3.0` (20 rpm each) keeps each pool at 40 rpm.

**Chroma race note:** collections are keyed by dataset name, not slot. Two
concurrent processes on the same dataset collide on `create_collection`. Fix:
pre-build both indices once (`HybridIndex(name).build(items)`), then all slot
processes find the collection and skip ingestion.

## Results

Same-condition ASP vs flat, n=500, `--min-context-tokens 0`, 0 errored rows. Recall is
computed on the assembled retrieval context (generation-independent) — Nemotron and
Llama-49B numbers match to the decimal, confirming a retrieval-mechanism property, not a
model artifact.

**ObliQA** (short gold passages, median 79 words → whole-span recall is fair):

| Model | ASP recall | Flat recall | Δ | ASP tok | Flat tok | ASP lat | Flat lat |
|---|---:|---:|---:|---:|---:|---:|---:|
| Nemotron-30B | **76.1%** | 37.9% | **+38.2** | 1,222 | 1,707 | 0.9 s | 5.2 s |
| Llama-49B | **76.1%** | 37.9% | **+38.2** | 1,217 | 1,706 | 0.9 s | 5.5 s |

ASP: +38 pp recall, ~28% fewer tokens, ~6× faster. Clean win — better and cheaper.

**GovReport-QS** (long gold sections, median 463 words → whole-span substring matching is
defeated by chunk fragmentation; **token-recall is the fair primary metric**):

| Model | ASP token-recall | Flat token-recall | Δ | ASP whole-span | Flat whole-span | ASP tok | Flat tok |
|---|---:|---:|---:|---:|---:|---:|---:|
| Nemotron-30B | **89.8%** | 34.7% | **+55.1** | 74.8% | 1.8% | 4,063 | 1,585 |
| Llama-49B | **89.4%** | 35.1% | **+54.3** | 74.8% | 1.8% | 4,033 | 1,579 |

ASP: +55 pp token-recall. ASP retrieves intact sections (75% whole-span); flat retrieves
fragments covering ~35% of gold content (1.8% whole-span). ASP spends more tokens
(~4,000 vs ~1,600) to pull whole sections — a recall-for-tokens trade, reported as such.

**Metric-artifact correction:** flat's GovReport-QS recall was initially reported as ~2%
(whole-span). Verification (`_normalise` substring test + token-overlap) showed the gold
sections are too long to match as one contiguous substring in flat's ~256-token chunks;
on the fair token-recall metric flat recovers ~35%, not 2%. The ASP advantage survives
(+55 pp token vs +73 pp whole-span) because ASP also scores far higher on token-recall.
Metric `token_recall` added to `metrics/span_recall.py`; pipelines now emit both.

**EM/F1 (emitted for continuity, not primary):** EM = 0% both datasets (gold is
passage/section references, not answer strings). F1 low but ASP-favouring (ObliQA 18.4%
vs 13.9%; GovReport-QS 9.2% vs 6.0%).

## Subset analysis — multi-target prediction FALSIFIED

ADR 0001 predicted ASP's advantage would concentrate on the multi-passage/multi-section
subset. It does not — the advantage is *larger* on single-target questions:

| Dataset | Subset | n | ASP | Flat | Δ |
|---|---|---:|---:|---:|---:|
| ObliQA | single-passage | 381 | 85.8% | 43.8% | **+42.0** |
| ObliQA | multi-passage (>1) | 119 | 44.9% | 18.7% | +26.2 |
| GovReport-QS | single-section | 55 | 98.2% | 0.0% | +98.2 |
| GovReport-QS | multi-section (>1) | 445 | 71.9% | 2.1% | +69.9 |

Multi-target questions are harder for **both** systems (recovering *all* of 2–6 scattered
gold units is hard — ASP recall falls 85.8%→44.9% on ObliQA), which compresses the
absolute delta. ASP wins every subset, but the mechanism story is corrected: the edge
comes from retrieving the **right, intact unit** — even on single-target questions — not
from a multi-hop-specific traversal benefit. A negative result, recorded not buried.

## Decision / status

Both datasets **promoted** as document-tree benchmarks. Headline: ASP +38 pp recall on
ObliQA (cheaper + faster); +55 pp token-recall on GovReport-QS (recall-for-tokens trade).
Results reproduce across Nemotron-30B and Llama-49B → retrieval-mechanism property. No
dataset-specific knob (no floor) — the ADR 0001 bar is met.
