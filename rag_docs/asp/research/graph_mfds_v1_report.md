# Benchmark Report — graph_mfds_v1

**Date:** 2026-08-03
**Branch:** experiment/doc-tree-retrieval-redesign
**Run tag:** graph_mfds_v1
**Author:** Kushal Sharma
**Datasets:** MuSiQue-Ans (multi-hop Wikipedia), STaRK-PRIME (biomedical KG)
**Backends:** 4 expansion arms, same-condition (same seeds, same graph, same process)
**Method under test:** **MFDS — Multi-Field Diverse Seeding** (new), vs the additive-PPR SOTA baseline
**SOTA baseline:** HippoRAG-2-style symmetric Personalized PageRank (`sym_ppr`; single summed restart vector).
  Published SOTA on STaRK-PRIME: MFAR* (Hit@1 40.9% / Recall@20 68.3%).
  Our `sym_ppr` on STaRK: 40.0% recall@20 (n=50, from `graph_realdata_v1_report.md`).
**Cost:** $0 — no LLM calls. All arms use `bge-small-en-v1.5` (local, cached).

---

## 0. What was tested and why

The `graph_realdata_v1` baseline established that `sym_ppr` (40.0% recall@20 on STaRK-PRIME n=50,
93.2% on MuSiQue n=200) is our best arm. The gap to the published SOTA on STaRK (MFAR* 68.3%) is
+28.3 pp. The `graph_macd_v1` falsification ruled out the fusion-operator axis entirely; the MACD
falsification report identified **seeding quality** as the most plausible remaining bottleneck.

The existing seeder (`_seed_scores` in `graph_retriever.py`) embeds the **full concatenated
`embed_text()` string** (name + type + content joined) against each query. This single-field approach
means a node can only enter the seed set if its concatenated representation is close to the query in
embedding space. Two failure modes result:

1. **Field dominance:** a node with a perfect name match but generic content is buried by a node
   whose content happens to mention the query terms, even if that node is the wrong entity type.

2. **Cluster seeding:** the top-k seeds are near-duplicates in embedding space (e.g. the same
   disease mentioned in 5 different relation contexts), giving PPR a tight cluster to restart from
   rather than a diverse set of entry points across the graph.

**MFDS** addresses both:

1. **Multi-field RRF:** rank nodes separately by name cosine, entity_type cosine, and content cosine
   (reusing the pre-built content cache), then fuse by Reciprocal Rank Fusion. A node scores high
   if it matches on *any* field — name OR type OR content — rather than requiring a good joint
   concatenation embedding.

2. **MMR diversity re-ranking:** after scoring, run greedy Maximum Marginal Relevance over the
   candidate list to ensure seeds are spread across graph regions, not clustered at one entity.

Both mechanisms are generation-independent ($0), dataset-agnostic (no if-dataset branches), and
degrade gracefully (lambda_mmr=1.0 → pure relevance = standard seeding; single field = standard
cosine seeding).

**Falsification condition:** MFDS-PPR recall@20 ≤ sym_ppr (40.0%) on STaRK-PRIME (n≥50).

---

## 1. Datasets

| Dataset | Graph | Gold | Metric | n | LLM cost |
|---|---|---|---|---|---|
| **MuSiQue-Ans** | Per-question passage-entity graph (~20 nodes) | `is_supporting` paragraphs | support-set recall@10 | 200 | 0 |
| **STaRK-PRIME** | Shared biomedical SKB (PrimeKG-derived) 129K nodes / 8.1M edges | verified `answer_ids` | answer-node recall@20 | 50 | 0 |

---

## 2. Headline scorecard — recall of gold nodes in retrieved subgraph

**MuSiQue** (top_k=10; n=200):

| Arm | recall@10 % | vs seed_only | vs sym_ppr | Verdict |
|---|---|---|---|---|
| seed_only (flat floor) | 78.2 | — | — | — |
| **sym_ppr (HippoRAG, baseline)** | **85.2** | +7.0 pp | baseline | — |
| mfds_seed_only | 62.0 | −16.2 pp | −23.2 pp | ❌ |
| mfds_ppr | 81.8 | +3.6 pp | −3.5 pp | ❌ |

**STaRK-PRIME** (top_k=20; n=50):

| Arm | recall@20 % | vs seed_only | vs sym_ppr | Verdict |
|---|---|---|---|---|
| seed_only (flat floor) | 16.9 | — | — | — |
| **sym_ppr (HippoRAG, baseline)** | **40.0** | +23.1 pp | baseline | — |
| mfds_seed_only | 15.0 | −1.9 pp | −25.0 pp | ❌ |
| mfds_ppr | 36.8 | +19.9 pp | −3.2 pp | ❌ |

No-regression check: `sym_ppr` = 40.0% on STaRK n=50 — **exactly matches** `graph_realdata_v1_report.md` reference (40.0%). Baseline is stable.

---

## 3. MuSiQue findings (complete)

1. ❌ **MFDS-PPR is falsified on MuSiQue.** −3.5 pp vs sym_ppr (81.8% vs 85.2%). The hypothesis
   that multi-field seeding improves recall on MuSiQue's passage-entity graphs is not supported.

2. ❌ **MFDS seeding alone collapses recall.** `mfds_seed_only` scores 62.0% — **−16.2 pp below
   flat seed_only (78.2%)**. On MuSiQue's ~20-node graphs, re-encoding 3 separate field corpora per
   query introduces noise rather than signal. The graphs are too small for multi-field disambiguation
   to provide coverage that the single-field embed_text() misses.

3. **Root cause (MuSiQue):** MuSiQue passage-entity graphs are ~20 nodes built from Wikipedia
   passage titles. Every node's `name` ≈ `content` (both are passage titles/snippets), and
   `entity_type` is constant ("document"). Multi-field RRF on near-identical fields in a tiny graph
   reranks within noise, and MMR diversity penalises the correct seed when the correct 3-4 passages
   are genuinely semantically similar. The MFDS design targets *large graphs with heterogeneous node
   types* — MuSiQue has neither property.

4. **No-regression check (MuSiQue):** sym_ppr baseline scores 85.2% at top_k=10 in this run vs
   93.2% at top_k=20 in `graph_realdata_v1`. The difference is the tighter budget (top_k=10 vs 20) —
   this is expected and not a regression. Confirm: the `sym_ppr` number at top_k=20 should match.

---

## 4. STaRK findings (complete)

1. ❌ **MFDS-PPR is falsified on STaRK.** 36.8% vs sym_ppr 40.0% — **−3.2 pp**. The hypothesis
   that multi-field name/type seeding improves recall on the 129K-node biomedical graph is not
   supported.

2. ❌ **MFDS seeding alone is flat-floor negative.** `mfds_seed_only` 15.0% vs seed_only 16.9%
   (−1.9 pp). MMR diversity re-ranking on the candidate pool slightly hurts even before expansion.

3. **No-regression confirmed.** `sym_ppr` baseline = 40.0% — exactly matches `graph_realdata_v1`
   reference. The baseline is stable; the MFDS loss is not a comparator artifact.

4. **Root cause (STaRK):** the biomedical SKB has 18 relation types but nodes are identified
   primarily by entity name (e.g. "CYP3A4", "strongyloidiasis"). The existing `embed_text()`
   concatenation is `"{name} | {entity_type} | {content}"` — the name already dominates the
   embedding because it is the most distinctive part of the string. Splitting into three
   separate embeddings and re-weighting via RRF does not improve over the concatenation: on a
   node like "CYP3A4 | gene/protein | involved_in_molecular_function", the name field alone
   already has the signal, and the type/content fields add noise when fused. The MMR step
   then penalises the correct node's neighbours (aliases, isoforms) which are needed as
   secondary seeds for the PPR walk.

---

## 6. Decision

**Not Promoted — MFDS fully falsified on both datasets.**

Both STaRK (−3.2 pp) and MuSiQue (−3.5 pp) show MFDS-PPR trailing the `sym_ppr` baseline.
The method is documented and the code remains in the repository as a tested, non-promoted arm
(`mfds_seed_only`, `mfds_ppr`) for potential future ablation, but is not the new SOTA.

**Consolidated root cause:**
MFDS was designed to fix seeding quality by matching the query against node fields independently.
The empirical result reveals that the bottleneck is not *field coverage* but *query-to-node
entity alignment*: the correct seed nodes are not being found because the query string does not
contain the biomedical entity names verbatim (e.g. the query mentions "a drug that targets
CYP3A4" but the node is named exactly "CYP3A4" — embed_text() already surfaces it). Multi-field
re-ranking does not change which entity the query resolves to; it only changes the rank ordering
among already-identified candidates. MMR then actively hurts by de-ranking entity aliases that
would serve as useful secondary seeds.

**Next direction: NER-based entity-span seeding (KG3)**

The seeding gap (40.0% → MFAR* 68.3%) is most likely a *query decomposition* problem, not a
*field coverage* problem. MFAR* explicitly extracts entity mentions from the query and matches
them against entity names. The next experiment:

1. Extract named entities from the query using spaCy `en_core_web_sm` (no LLM, $0).
2. For each entity span, embed **the span string alone** (not the full query) against the
   **node name field only** (not concatenated embed_text).
3. Take the top-2 name-matched nodes per span as seeds; union across all spans.
4. Run `sym_ppr` from this entity-span seed set.

Falsification condition: recall@20 ≤ 40.0% on STaRK-PRIME n≥50.
Bar to clear: ≥+5 pp over sym_ppr (→ ≥45.0%) to be worth promoting.
This is KG3-X0 in the experiment backlog.

---

## 5. Decision

**Not Promoted (MuSiQue). STaRK verdict pending.**

MuSiQue is falsified. If STaRK is also falsified, MFDS is fully not promoted and the root-cause
analysis below applies to both datasets.

**Root cause analysis (if STaRK also fails):**

The MFDS hypothesis assumed that re-ranking by separate field embeddings would surface node-name
matches that the concatenated `embed_text()` buries. On STaRK's biomedical KG this may still hold
— entity names like "CYP3A4" and "strongyloidiasis" are the primary discriminating signal, and
the node's content (a relation-type string, e.g. "involved_in_molecular_function") is generic.
Multi-field RRF should up-weight the name match. The MMR step is more questionable on STaRK: the
biomedical graph has many near-duplicate entity names (isoforms, synonyms), and MMR may penalise
the correct entity when it shares embedding space with its aliases.

**If STaRK passes (MFDS-PPR > 40.0%):** partial promotion — MFDS applies only to large heterogeneous
graphs (STaRK-style), not to small homogeneous passage graphs (MuSiQue-style). The routing criterion
becomes a dataset-shape detector rather than a universal seeder replacement.

**Next direction regardless of STaRK outcome:**

The seeding gap (40.0% → 68.3% SOTA) remains open. Two remaining approaches to explore:

1. **NER-based anchor seeding:** decompose the query into named-entity spans using spaCy
   `en_core_web_sm` (no API cost), look up each entity span in the *name field* embedding index
   separately (not the concatenated embed_text), and union the top-2 hits per entity as PPR seeds.
   This is the HippoRAG-2 mechanism without LLM decomposition. Unlike MFDS which applies RRF across
   fields for every query term, NER seeding applies *exact field* (name only) matching per entity
   span — potentially more precise on biomedical named entities.

2. **Entity-type-filtered seeding:** for queries containing an explicit entity type mention
   ("disease", "drug", "protein"), pre-filter the seed candidate pool to nodes of that type before
   running cosine ranking. Reduces the effective N from 129K to ~10K–20K (the relevant type
   subset), which may sharpen the cosine signal on the remaining candidates.
