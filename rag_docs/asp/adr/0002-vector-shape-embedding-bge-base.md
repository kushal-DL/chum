# ADR 0002 — Vector shape adopts bge-base-en-v1.5 as the embedding of record; disclose the flat-floor regression it caused

- **Status:** Accepted
- **Date:** 2026-08-11
- **Deciders:** Kushal Sharma
- **Branch:** `experiment/unstructured-rag`
- **Related:** [`vector_spectra_probe_report.md`](../research/vector_spectra_probe_report.md), `product-backlog/vector/epic-16-vector-benchmark.md`, `src/asp/retrieval/vector/vector_retriever.py`

---

## Context

The unstructured-text (vector) shape's methods (COVERT, APEX, SPECTRA) and the
`flat_dense` floor are all evaluated on the **same** embedding, same process. The
early COVERT/APEX runs used `BAAI/bge-small-en-v1.5` (384-dim). Per the user's
standing instruction ("use bge-base embedding for every method"), the SPECTRA runs
switched to `BAAI/bge-base-en-v1.5` (768-dim).

This swap was made **without an ADR**, and an adversarial audit (2026-08-11) flagged
that it silently changed the comparator floor. The default embedding in
`vector_retriever.py` is still `bge-small`; the bge-base runs pass the model
explicitly. This ADR records the decision and — critically — the effect on the
reported deltas, so no later reader mistakes an embedding-driven floor change for a
method win.

## Decision

**Adopt `BAAI/bge-base-en-v1.5` (768-dim) as the embedding of record for all vector-shape
methods and the flat_dense floor**, for every dataset, both arms, same process. Rationale:

- The user directed bge-base for every method; consistency across arms is what matters
  for a same-condition comparison, and both arms use it.
- bge-base is a stronger, still-permissively-licensed, CPU-runnable local model ($0/query),
  consistent with the data-sovereignty constraint.

**Also required by this decision** (so the swap is not a hidden confound):

1. `vector_retriever.py`'s default model constant should be updated to bge-base (currently
   `bge-small` at the module default), OR every benchmark invocation must pass
   `--model BAAI/bge-base-en-v1.5` explicitly. Until the default is changed, the explicit
   flag is mandatory and is present in the report's Reproduce block.
2. All deltas must be stated against the **bge-base** flat floor, never mixed across models.

## The regression this swap caused (disclosed, not hidden)

The bge-small→bge-base swap **lowered the flat_dense floor on ArguAna R@10 from 84 → 80**.
Because SPECTRA is reported at 88, the swap **inflated the ArguAna delta** from what it would
have been against the higher (bge-small) floor:

| Embedding | ArguAna flat R@10 | ArguAna SPECTRA R@10 | Δ reported |
|---|---:|---:|---:|
| bge-small (prior) | 84 | — | — |
| bge-base (current) | 80 | 88 | +8.0 |

A +8.0pp delta measured against a floor that itself dropped 4pp on the model swap is **not**
a clean +8.0pp method win. The honest reading: part of the apparent headroom is the floor
regressing under the new embedding, not SPECTRA recovering docs. This is the same class of
confound ADR 0001 caught on MAUD (a shared knob degrading the comparator). The vector report
(§6, §8) now states the delta as bge-base-vs-bge-base and flags the floor movement.

## Consequences

- Every vector-shape number in the SPECTRA report is bge-base/bge-base — internally consistent.
- Cross-run comparison with the pre-swap COVERT/APEX bge-small numbers is **invalid** and must
  not be made without re-running on bge-base.
- The ArguAna +8.0pp headline must not be cited as a pure method win; the floor regression is
  part of the story.
- FiQA (the pending, decisive dataset) must be built and run on bge-base for the comparison to
  be valid — the cached bge-small FiQA index (`fiqa_beb5deaf59a7.npz`) is **not** usable for
  the SPECTRA-vs-flat bge-base claim.
