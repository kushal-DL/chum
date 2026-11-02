### EE4-X2 — Score-variance gate (next experiment)

Laxmikant's diagnosis is sharp and correct: the EE4-X1 floor fires when the assembled bundle is below a fixed token floor (`running_tokens < min_context_tokens`), which is a proxy for retrieval ambiguity, not a measurement of it. On MAUD the proxy happens to align — short bundles really do indicate the ranker is undecided — but on CUAD a short bundle is the signal of a *confident* hit, and the floor misfires there for exactly that reason. EE4-X2 was already the next experiment in the queue when his email arrived; the convergence is a useful confirmation that the diagnosis is the right one.

### Mechanism

Replace the `running_tokens < min_context_tokens` trigger in [`_expand_to_floor()`](src/asp/benchmarks/retrieval/bundle_assembler.py) with a gate computed directly from the BM25 score distribution produced by [`score_nodes_bm25`](src/asp/benchmarks/retrieval/bm25.py). The signal is a **top-1 margin** — how cleanly the best candidate beats the runner-up:

```
top_K          = top retrieval candidates (e.g. K = 6)
s1, s2, …, sK  = BM25 scores, sorted descending
top1_margin    = (s1 - s2) / s1        if s1 > 0
               = 0                     otherwise   (guard for s1 == 0)
trigger expansion iff top1_margin < τ
```

Margin is high when one node dominates (confident hit) and low when the top scores are bunched (ambiguous retrieval), which makes the predicted CUAD/MAUD directions follow directly from the formula rather than being asserted. The guard for `s1 == 0` handles the degenerate case where BM25 returns no overlap at all; the gate defaults to firing, matching today's "expand when uncertain" behaviour.

The threshold `τ` is auto-calibrated per corpus from a 50-query validation slice — the 25th-percentile margin observed on that slice — so no human tuning is required and the gate logic itself is identical across datasets. A one-off calibration pass per corpus computes τ from this validation slice; this is a build-time cost, not a per-query cost, and it must be re-run when the corpus changes.

### Predicted behaviour

- **MAUD (typical case).** Every clause mentions "consideration", "merger", "payment", "closing". The top-6 BM25 scores cluster tightly because the query terms hit broadly across the document, so `s1` and `s2` are close and `top1_margin` is small → gate fires → `_expand_to_floor()` adds parent / sibling context. The bundle ends up roughly where EE4-X1 placed it, so the MAUD result reported in §6.2 should be preserved.
- **CUAD (the misfire case).** The discriminating clause typically owns the query's rare terms ("governing law", "assignment", "non-compete"). Top-1 has a clean BM25 lead over its siblings, `top1_margin` is large → gate does **not** fire → the bundle stays at its retrieved ~240 tokens, no dilution, no expansion cost.
- **No new per-query infrastructure.** The margin signal is read from the ranker the pipeline already runs each query; no second index, no auxiliary LLM call, no extra latency beyond two comparisons over the top-K scores.

### Status

Not yet run. The run tag will be `rr_ctx_var`, executed against the same five-dataset, two-model matrix used for EE-Struct, and is expected to land before the funded research engagement begins — so the MAUD-vs-CUAD asymmetry can be reported as *resolved by gate redesign* rather than as a standing open question.