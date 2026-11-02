# Document-Tree Retrieval Redesign — Research Charter

**Branch:** `experiment/doc-tree-retrieval-redesign`
**Author:** Kushal Sharma
**Opened:** 2026-06-20
**Status:** Charter — no code yet. Reasoning before implementation.

---

## 1. Why this branch exists

The Phase-1 ASP document-tree retriever (path-augmented BM25 → tree-traversal expansion → cross-encoder rerank → paragraph extraction → authority-sorted bundle) has been benchmarked. Two failure modes are now beyond doubt and are *not* due to bugs that a small patch will fix:

1. **Cross-document seed collapse.** On HotpotQA-500 (multi-hop bridge questions, distractor setting) ASP is **3 F1 points behind a flat hybrid (BM25 + vector RRF + cross-encoder)** baseline (0.662 vs 0.690). On the 44 cases where ASP fails badly and flat wins, **100% are retrieval failures — every one is a case where ASP retrieved zero or one of the two required articles.** Tree traversal (sibling-scan + ancestor-walk) is reinforcing whichever article won the BM25 first round and starving the second.

2. **Underspecified-query collapse.** On MAUD-500 (legal multiple-choice) ASP holds at 35.8%. On **two-candidate** questions the accuracy is **32.8% — worse than 50% random.** When the question itself lacks discriminating tokens after stopword removal (e.g. "Type of Consideration"), BM25 pulls the loudest token-frequency-matching section, which is reliably the wrong clause.

The deep-research pass (5 angles, 21 sources, 25 adversarially-verified claims) confirmed two things relevant to this charter:

- **"Tree structure beats flat retrieval on multi-hop"** — refuted 0-3. RAPTOR's headline claim that recursive-summary trees outperform flat retrieval on multi-step reasoning did not survive verification. The user's empirical observation is consistent with what survived: the tree's locality bias is a real phenomenon, not an ASP bug.
- **No verified published recipe exceeds 50% on MAUD as of EMNLP 2023.** The legal-MC retrieval problem is open.

The off-the-shelf 2024-era fixes (Beam Retrieval, IRCoT, HyDE/Query2doc, Doc2Query, MemWalker) would each plug in as a graft. They are not the wrong answer. But each grafts a separately-published technique onto the existing pipeline, and the result would be an ASP that *imitates* a survey of 2024 RAG. **We want to reason from first principles about what the correct shape of structured-document retrieval is, before deciding which off-the-shelf piece (if any) we adopt.** The core ASP claim is that data shape determines retrieval shape — that claim is paper-grade only if our retriever derives from it, not from grafted convenience.

This branch is where that reasoning happens. Code on this branch is exploratory. Anything that proves out merges back to `develop` as a focused PR. Anything that doesn't, doesn't.

---

## 2. Out-of-scope (hard constraints)

The redesign **must not** be dataset-specific. Specifically prohibited:

- ❌ Special-cased logic for HotpotQA bridge questions (e.g. "if dataset == hotpotqa, retrieve two articles")
- ❌ Special-cased logic for MAUD multiple-choice (e.g. "if MC, expand query with candidates")
- ❌ Hardcoded entity lists, pattern matchers, or heuristics tuned on one benchmark
- ❌ Threshold tuning by inspecting benchmark answers (test-set leakage)

The redesign **must**:

- ✅ Improve on at least one of {HotpotQA bridge, MAUD MC} *as a side-effect of a principled change* — never as the change itself
- ✅ Not regress {Travel Policy travel, JCP Credit JCP credit, CUAD} which are passing — the redesign cannot trade structured-document accuracy for multi-hop accuracy
- ✅ Be measured against the existing flat-hybrid baseline; if the redesign cannot beat flat hybrid on *any* shape we believe trees should win, the redesign is wrong

The principle: **the improvement must come from a property of the retriever that we can state in one sentence**, not from accidental fit to a benchmark.

---

## 3. First-principles framing

What is retrieval actually solving? Decompose the failure modes into properties the retriever lacks:

### 3.1 Coverage vs concentration

BM25 + tree-traversal jointly *concentrate* mass on the highest-scoring region of the index. For a single-fact query over a structured document this is correct: the answer lives in one clause, retrieving the one clause is the goal. For a query whose evidence is *distributed* across multiple regions (cross-article bridge, cross-clause comparison, multi-section conjunction), concentration is the failure mode itself — pulling 6 chunks from one place is *exactly wrong*.

The retriever currently cannot tell these two regimes apart. **Property missing: query-conditional concentration vs spread.**

### 3.2 Lexical match vs intent match

"Type of Consideration" has, after normalization, one informative token: *consideration*. BM25 ranks every clause containing that token; the question intent ("which kind of payment did the buyer use to acquire the target — cash, stock, or mixed") is invisible to a token-frequency scorer. Cross-encoder rerank can in principle recover from this, but only if the right clause is in the candidate set; if BM25 returned twenty boilerplate "Conversion of Capital Stock" sections, the reranker has nothing better to choose from.

**Property missing: a representation in which the question's *intent* (what concept is being asked about) lives at the same level as the clause's *content*.**

### 3.3 Tree topology as a feature, not a constraint

The current pipeline treats the tree as a *navigation* aid (sibling scan, ancestor walk) and as a *grouping* aid (per-doc slug, authority levels). It does not use the tree as a *signal* — tree position is not a feature of the ranking function except via the depth multiplier and path-token boost.

What the tree could in principle express but currently doesn't:

- **Dispersion**: how spread are the high-scoring nodes across the tree? A query whose top-K nodes cluster in one subtree is a localized query; a query whose top-K spans multiple subtrees is a distributed query, and the retrieval policy should differ.
- **Discriminability**: how distinct is each candidate node from its siblings *with respect to the query*? A node whose siblings score similarly to it is a poor discriminator — there's nothing in this query that picks this node out from its neighbours.
- **Path entropy**: how much information does a node's heading-path add over the document root? A clause buried under a generic "Definitions" path is shape-different from one under "Article 7 — Conditions to Closing"; both currently get the same path-token treatment.

**Property missing: tree-aware features that quantify how the candidate node relates to the rest of the tree, not just how its tokens match the query.**

### 3.4 Single-pass retrieval as a planning failure

A single-pass retriever commits to its candidate set before reading any of it. The existing pipeline's only feedback loop is the cross-encoder, which scores candidates already chosen by BM25 — it cannot ask BM25 for *different* candidates. For a query whose true evidence requires reading hop-1 to know what hop-2 to make, single-pass is structurally underspecified.

**Property missing: the ability to revise the retrieval plan after seeing partial evidence.** This is the abstract form of IRCoT, MemWalker's revert action, and Beam Retrieval's hypothesis tracking. The principled question is: *what is the smallest amount of plan-revision a retriever needs to handle queries whose evidence is conditional on other evidence?*

---

## 4. Candidate design directions

These are *directions*, not techniques. Each is a hypothesis about which missing property is most load-bearing. None is committed. Each gets a sketch and an evaluation hook.

### Direction A — Dispersion-aware retrieval

**Hypothesis:** the failure mode is that the retriever has no internal representation of "this query's evidence is distributed." Build one.

**Sketch:**
1. Score all tree nodes with the existing path-augmented BM25.
2. Compute a *dispersion statistic* over the top-N scores — e.g. the entropy of the distribution of top-N nodes over `doc_slug`, or the spread of their tree-positions. High dispersion ⇒ query wants coverage; low dispersion ⇒ query wants depth.
3. Adjust the seed-selection policy as a function of dispersion: high-dispersion queries enforce per-`doc_slug` quotas (or run MMR with diversity weight tied to the dispersion statistic); low-dispersion queries keep the existing concentrate-and-traverse behaviour.
4. The traversal expansion (sibling scan, ancestor walk) is then *also* gated on dispersion — high-dispersion queries skip sibling scan because amplifying one subtree is wrong for them.

**What's principled about this:** the retriever now has a measurement of the regime it's in, and the policy is a *function* of that measurement, not a fixed bias. It's also dataset-agnostic — dispersion is a property of (query, corpus), not of the benchmark.

**Risk:** the dispersion statistic might be too noisy at small N to be a reliable switch. Need to quantify on Travel Policy/JCP Credit/CUAD/HotpotQA simultaneously and check that the regime classification aligns with what we'd hand-label.

### Direction B — Tree-position as a structural feature

**Hypothesis:** the right ranking score is `f(token_match, tree_position, query_shape)` rather than `f(token_match) × g(depth)`. Build the joint feature.

**Sketch:**
1. For each candidate node, compute features that describe its tree position relative to the query: *path entropy* (information content of the heading path given the corpus), *sibling discriminability* (how much higher does this node score than its siblings on this query), *parent-context overlap* (does the parent's content add evidence the child lacks).
2. Combine these with the BM25 score via a small, *fitted-on-held-out-not-test* linear model — or, more cleanly, derive a closed-form combination from a stated assumption (e.g. "we want nodes that maximize information gain over their tree neighbourhood").
3. The cross-encoder then reranks the resulting set; the goal is to give it a better candidate set, not to replace it.

**What's principled about this:** treats the tree as the coordinate system the document mission statement (`docs/document_tree_mission.md`) said it should be, instead of a navigation aid bolted onto BM25.

**Risk:** the features have to be cheap (we are still BM25-fast) and the combination can't introduce a free hyperparameter we can only tune by peeking at the test set.

### Direction C — Two-pass with a stop condition

**Hypothesis:** the right number of retrieval passes is not 1 and not "until the LLM is satisfied" — it's "until a measurable property of the candidate set stops changing." Build the property.

**Sketch:**
1. First pass retrieves a candidate set. Compute a *coverage statistic* over it — e.g. the union of entities mentioned in the candidate set versus the entities mentioned in the query.
2. If coverage is below a threshold *defined as a function of the query, not a constant*, formulate a second-pass query from the entities present in the candidate set but absent from the query. Retrieve again, merge.
3. Stop when an additional pass adds < δ new entities, or after N passes.

**What's principled about this:** entity coverage is a measurable property; the stop condition is a function of the data, not a hardcoded `N=2`. Generalises IRCoT's idea (use hop-1 to inform hop-2) without requiring the LLM to do the planning.

**Risk:** "entity" is a fuzzy concept; an off-the-shelf NER will be wrong for legal/financial domains. Need to define it at the granularity the corpus uses (defined-terms in contracts, article titles in HotpotQA-style corpora) — and that definition has to be derivable from the document tree itself, not supplied per dataset.

### Direction D — Score the tree, not the nodes

**Hypothesis:** a single node is the wrong unit. The right unit is a *minimal subtree* that contains the query's evidence. Score subtrees directly.

**Sketch:**
1. For each candidate seed node, define its *evidence subtree* as the minimal connected subgraph that includes the seed plus all nodes whose content references entities in the query (via cross-reference, defined-terms, or path-overlap).
2. Score the subtree by an aggregate function of its nodes that *rewards completeness*: a subtree that covers more query entities scores higher than a single high-scoring node.
3. Final candidates are subtrees, not nodes. Bundle assembly serializes the subtree depth-first into the LLM prompt.

**What's principled about this:** a query's evidence is rarely a single point; treating retrieval as "find the right subtree" is closer to how humans actually use a structured document, and it makes "this query needs evidence from two places" a first-class case rather than an edge case.

**Risk:** subtree enumeration is combinatorial; need a strong pruning rule. Also: HotpotQA isn't a single tree, it's a forest of two articles — does "subtree" generalize, or does the unit need to be "minimal evidence cover" across the forest?

### Direction E — Calibrate the LLM's "Not found" boundary

**Hypothesis:** 11% of HotpotQA queries get "Not found in context." from ASP. The retriever may sometimes have the right evidence but the LLM rejects it as not literally answering the question. The retriever's job isn't done when it produces a candidate set; it's done when the candidate set is *legible* to the model under its current prompt.

**Sketch:**
1. Measure the recall of the existing candidate set on a held-out subset where we know the gold supporting passages — what fraction of gold passages are in our top-K?
2. If recall is high but EM is low, the problem is generation/legibility, not retrieval — and the redesign should focus on bundle assembly (ordering, header style, what the LLM sees first).
3. If recall is low, the problem is retrieval, and Directions A-D are the right place to spend.

**What's principled about this:** measures the actual locus of failure before designing for it. Not a competing design — a *prerequisite* before committing to A, B, C, or D.

---

## 5. Evaluation discipline

The redesign succeeds if and only if these conditions all hold against the *current* M2 baseline (Mistral, 500 questions per dataset, 1500 token budget):

| Dataset | Current ASP | Current Flat | Redesign target |
|---|---|---|---|
| HotpotQA F1 | 0.662 | 0.690 | **≥ 0.690** (closes the gap) |
| MAUD overall | 0.358 | n/a | **≥ 0.45** (clear, not borderline) |
| MAUD 2-candidate | 0.328 | n/a | **≥ 0.50** (above random) |
| CUAD F1 | (read from baseline) | (read) | **no regression** (within 1 pp) |
| Travel Policy | passing | n/a | **no regression** |
| JCP Credit | passing | n/a | **no regression** |
| Latency p50 | 2,470 ms | 2,698 ms | **≤ 2× current** |
| Token cost / query | 1,232 | 1,668 | **≤ flat hybrid** |

The redesign is *also* held to a methodology bar:

- Every change is justified by a stated principle in section 3 or 4 — no "I tried it and it worked" without a sentence about what property the change adds.
- No hyperparameter is set by looking at the test split. Tuning happens on a held-out slice carved out of the validation set; if there isn't enough validation data, we use a smaller test slice and report the smaller-N caveat.
- Failure-mode breakdowns (the kind of analysis we did on M2) get re-run on the redesign before claiming a win. A 3-pp F1 gain is not a result; a 3-pp F1 gain *with the failure-mode shift documented* is a result.
- The flat hybrid baseline is run alongside in every evaluation. We never report ASP numbers in isolation.

---

## 6. What this branch is *not*

- Not a rewrite of the document tree builder. The tree we have is fine. The retriever is what's under examination.
- Not a swap of BM25 for dense embeddings. That's a separable axis we can run as an ablation, but it isn't the redesign — the redesign is about *what the retriever measures and how it combines those measurements*. The lexical/dense axis is orthogonal.
- Not an LLM-agentic loop. We can adopt one if it's the answer, but the principle is to find the smallest deterministic mechanism that closes the gap; agentic loops are the fallback if no deterministic mechanism works.
- Not a paper draft. This document feeds a future paper section, but the goal is the working retriever, not the prose.

---

## 7. Next steps (in order)

1. **Recall audit (Direction E prerequisite).** Compute, on the existing M2 ASP results: per-question, did the top-K candidate set contain *both* gold supporting passages? Aggregate by question type. Output: a single number — *recall@K* — per dataset. This decides whether the redesign attacks retrieval (low recall) or bundle/generation (high recall).
2. **Dispersion statistic prototype (Direction A).** Implement the dispersion measurement on the existing BM25 score map and log it on every M2 run. No policy change yet — just measure. Confirm the statistic separates HotpotQA bridge from CUAD-style single-clause queries before using it as a switch.
3. **Per-`doc_slug` quota A/B.** Smallest possible test of Direction A: enforce a per-document seed cap on HotpotQA, leave everything else identical, re-run, compare. This is a one-paragraph change and an honest test of whether the diversity hypothesis explains the failure mode.
4. **Decide on Directions B, C, D** based on (1) and (3). Don't start implementing them until the recall audit and the dispersion measurement are in.

Each step ends with a writeup in this branch's `docs/research/`. No step is "done" until the writeup exists.

---

## 8. Cross-references

- Mission statement (foundational): `docs/document_tree_mission.md`
- Implementation reference: `docs/document_tree_parser_design.md`, `src/asp/retrieval/document_tree/retriever.py`, `src/asp/benchmarks/pipelines/document_tree_rag.py`
- Most recent benchmark gap analysis: `docs/document_tree_gap_analysis.md`
- Benchmark numbers backing this charter: `data/benchmarks/hotpotqa_m2_results.json`, `data/benchmarks/maud_m2_results.json`
- Deep-research workflow output (5 angles, 25 verified claims, 8 refuted): summarized in the conversation that opened this branch on 2026-06-20.
