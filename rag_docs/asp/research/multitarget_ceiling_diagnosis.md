# Findings — The ObliQA Multi-Target Ceiling Is Not a Similarity Problem

**Date:** 2026-07-28
**Branch:** `experiment/doc-tree-retrieval-redesign`
**Status:** Diagnosis complete — motivates [EE6 charter](../../experiment-backlog/epic-ee6-requirement-coverage.md)
**Related:** [ee5_x1_findings.md](ee5_x1_findings.md), [hierarchical_datasets_findings.md](hierarchical_datasets_findings.md)

---

## Question

ObliQA recall is 76.1%, but single-target questions score 85.8% and multi-target
(gold = 2–6 passages) only **44.9%**. The whole ceiling is multi-target set
retrieval. A human regulatory annotator does *not* have this problem — so what
connects the scattered gold passages that our retriever is missing? Is it memory,
relationships, or something else?

## Method

Four independent diagnostics, each testing one candidate for "the connection,"
all on the 119 ObliQA multi-target questions (n=500 test split). Retrieval-only,
generation-independent. Probe script: `scripts/ee5_human_connection_probe.py`;
data: `data/benchmarks/ee5_human_connection_probe.json`,
`data/benchmarks/ee5_x1_diagnosis.json`.

## Results — every similarity-based connection is falsified

| Candidate connection | Measurement | Result | Verdict |
|---|---|---|---|
| Weak lexical seed scorer | EE5-X1 dense arm, n=500 | +0.30 pp | ❌ |
| Vocabulary mismatch (dense fixes) | dense rank of BM25-buried gold | 68% also buried, median rank 75 | ❌ |
| Passages cite each other (hypertext) | cross-reference scan on gold text | **5.9%** cite another gold | ❌ |
| Passages cluster in embedding space | gold↔gold vs gold↔random cosine | 0.755 vs 0.683 = **1.11×** | ❌ |
| Gold is distinctly query-similar | query↔gold vs query↔random | 0.729 vs 0.627 | weak |

**Key number:** gold passages are only **1.11×** more similar to *each other* than
to random passages — statistical noise in `bge-small` space, where all regulatory
text sits at ~0.68 baseline similarity. And gold is barely more similar to *each
other* (0.755) than to the *query* (0.729): the gold set is not a detectable
cluster by any means, and the passages are not bound to each other in the corpus.

## Interpretation — the connection is external

The signal that makes several scattered passages "the complete answer" is **not
recoverable from the corpus** — not lexically, semantically, via citations, or via
clustering. Every retrieval algorithm in the literature (BM25, dense, ColBERT,
RRF, GraphRAG, HyDE, RAPTOR) computes `f(query, passage)` and aggregates; they
differ in the kernel, not the objective. **A better kernel cannot recover a signal
that is provably absent from the space it operates on.** This is exactly why
EE5-X1 was a no-op.

The human annotator succeeds because they retrieve by a different objective. They
read the question as a **specification** and retrieve to **satisfy** it — e.g.
"documentation for a suspicious-activity report" decomposes into `{what to report,
to whom, what documentation, sufficiency standard, exceptions}`, and each gold
passage satisfies a *different slot*. The passages look unconnected to any
similarity measure because their only shared property is *"jointly satisfy an
external requirement template"* — a schema in the annotator's head, not in the
text. This is consistent with all four diagnostics.

## Consequence

The correct objective for structured-document retrieval is **requirement coverage,
not ranking**: retrieve the minimal passage set that jointly satisfies every
requirement slot the question opens, where the schema is *induced* from the
question and the document tree together. This is document-tree-native — the tree's
own `Chapter → Rule → Guidance → Exceptions` skeleton is a latent, corpus-provided
requirement schema that flat chunking destroys. It is the ASP thesis ("data shape
determines retrieval shape") in its strongest form, and no prior work formalizes
retrieval this way because in a flat corpus the schema is invisible.

The load-bearing novelty is **unsupervised schema induction** (set-cover is a known
solver once slots exist). Because that step may prove too noisy, the next action is
a cheap pre-registered feasibility probe — **EE6-X0** — that gates the full build.
See the [EE6 charter](../../experiment-backlog/epic-ee6-requirement-coverage.md).

## Status

Diagnosis complete and recorded (first-class negative results). No code promoted.
Next: EE6-X0 schema-induction feasibility probe.
