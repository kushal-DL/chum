### Framing

This trace walks a single MAUD row end to end on real pipeline output. ASP picked the correct option; the structure-aware flat baseline on the same row returned a larger bundle and refused to answer. Numbers below were captured by `scripts/trace_maud_example.py` against the `rr_ctx700` run — the configuration where `min_context_tokens=700` is active and Phase B floor expansion fires when Phase A leaves the bundle under-filled. The point of the trace is to make the expand step, the arithmetic, and the failure mode of the flat baseline all inspectable on one row.

### Query and document

**Question ID:** `maud_contract_49_Accuracy of Target R&W Closing Condition_10-7_361`

**Question:** "Accuracy of Fundamental Target R&Ws — Types of R&W: Opinion of Financial Advisor"

**Candidates:**
- `[0]` Capitalization-Other, Authority, Approval, Enforceability, Organization, No MAE, Brokers' Fee
- `[1]` Capitalization-Other, Authority, Approval, Enforceability, Organization, No MAE, Takeover Statutes, Rights Agreement, **Opinion of Financial Advisor** ← GOLD

**Document:** 8,090 characters → 39 nodes after parsing into the document tree.

### BM25 + depth-adjusted scoring

Path-augmented BM25 (`score_nodes_bm25` in [bm25.py](src/asp/benchmarks/retrieval/bm25.py), lines 109–191) was run with `prefer_leaf=True`. Query tokens after stopword stripping: `{accuracy, fundamental, target, types, opinion, financial, advisor}`. Three of those (`opinion`, `financial`, `advisor`) appear in the §3.19 heading path; the other four do not appear anywhere in the document body.

**Parameters:** k₁=1.5, b=0.75, heading_boost=0.5/token, heading_boost_min_overlap=2, depth_mult (prefer_leaf=True): depth≥2 ×1.2 / depth<2 ×0.9.

| rank | depth | base BM25 | +heading boost | ×depth mult | final | heading path |
|------|-------|-----------|----------------|-------------|-------|--------------|
| 1 | 1 | 15.235 | +1.50 | ×0.90 | **15.062** | §3.19 Opinion of Financial Advisor |
| 2 | 1 | 15.235 | +1.50 | ×0.90 | **15.062** | §3.19 Opinion of Financial Advisor (dup) |
| 3 | — | 0.000 | — | — | 0.000 | (37 other nodes, no query-token overlap) |
| 4 | — | 0.000 | — | — | 0.000 | — |
| 5 | — | 0.000 | — | — | 0.000 | — |

Honest disclosure: 37 of 39 nodes scored exactly 0.0. The discriminating signal in this row is a single high-IDF path-token match on "Opinion of Financial Advisor". The depth multiplier (0.9 for depth=1 with `prefer_leaf=True`) marginally **penalised** the §3.19 hits — pulling them from 16.735 down to 15.062 — but the gap to the next non-zero candidate was the full 15.062 (next-best score = 0.000), so no realistic multiplier in [0.9, 1.2] could change the ranking. This is precisely the test Laxmikant flagged: in this trace the depth multiplier did not flip anything.

### Seed and rerank

- **Round-robin seed selection** (`cap=20`, per-source floor `=4`): 2 seeds returned — both §3.19 nodes, the only nodes with non-zero score.
- **Tree-traversal expansion** (pre-rerank): 2 seeds → 2 candidates. Siblings and parent fell below the score threshold, so no additional candidates were added at this stage.
- **Cross-encoder rerank** (top-6 cap): only 2 candidates available; both kept.

### Bundle assembly and floor expansion

Assembly runs in [bundle_assembler.py](src/asp/benchmarks/retrieval/bundle_assembler.py); the floor-expansion logic is `_expand_to_floor()` at lines 83–149. Header overhead is fixed at 100 tokens per included node, so per-node cost is `extracted_paragraph_tokens + 100`.

**Phase A — sequential add up to `token_budget=5000`:**

| step | node | extracted tok | + header | running |
|------|------|---------------|----------|---------|
| 1 | §3.19 Opinion of Financial Advisor (A) | 10 | +100 | 110 |
| 2 | §3.19 Opinion of Financial Advisor (B) | 10 | +100 | 220 |

Phase A exits with `running=220 < floor=700`. Phase B fires.

**Phase B — `_expand_to_floor()` BFS:**

| step | action | cost | running |
|------|--------|------|---------|
| 1 | add §3 Representations and Warranties of the Target Company (container; parent of §3.19) — extracted=0, +header=100 | +100 | 220 → 320 |
| 2 | enqueue siblings of §3.19 into BFS queue | 0 | 320 |
| 3 | add §7.3 Conditions to the Obligations… | +166 | 320 → 486 |
| 4 | add §7.3 Conditions… (sub-paragraph) | +119 | 486 → 605 |
| 5 | add §3.2 Capitalization | +108 | 605 → 713 ← floor met, stop |

To be unambiguous: "parent" at step 1 means the §3 container that owns §3.19, not the document root. The container itself contributes zero extracted text — only its 100-token header — which is why the cost is a clean +100.

### Final bundle and decision

- **ASP bundle:** 6 nodes, 713 tokens. Model picked option `[1]` — **correct**.
- **Flat structure-aware baseline** ([flat_structured_rag.py](src/asp/benchmarks/pipelines/flat_structured_rag.py)) on the same row: 1,284-token bundle, model answered `"None"` (refusal).

What crossed the floor — §7.3 Conditions to the Obligations and §3.2 Capitalization — turned out to be the load-bearing context. Without expansion the model saw only two 10-token §3.19 fragments and could not discriminate between option `[0]`'s enumeration (ending at "Brokers' Fee") and option `[1]`'s enumeration (ending at "Opinion of Financial Advisor"). The siblings supplied the enumeration context that made the categorical distinction visible.

### What this trace tells us

The depth multiplier did **not** change the ranking in this row. That is the right thing to admit: one example does not generalise, and the multiplier's effect is conditional. With `prefer_leaf=True`, depth<2 gets ×0.9 and depth≥2 gets ×1.2, so a deeper node only overtakes a shallower one when its base score is ≥ 0.75× the shallower node's base score (since `1.2 × base_deep > 0.9 × base_shallow ⇔ base_deep > 0.75 × base_shallow`). A targeted experiment over rows with contested rankings — pairs whose base scores sit inside that 75% window — is the right way to measure the multiplier's effect, and is currently unmeasured.

Expansion is deterministic given the parse and the question: same document, same query, same bundle. The Phase B stop arithmetic also behaved cleanly at the boundary — step 5 added §3.2 and that addition brought running to 713 ≥ 700, so BFS exited; the next queued candidate (the sixth) was never evaluated. The honest summary is that on this row most of the work was done by one high-IDF path-token match plus a clean floor expansion; the depth multiplier was inert.