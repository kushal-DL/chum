# Research Brief — Knowledge-Graph Retrieval Shape (ASP Epic 7 seed)

**Author context:** Kushal Sharma / ASP
**Date:** 2026-07-29
**Branch:** experiment/doc-tree-retrieval-redesign
**Method:** multi-agent web research (16 verified sub-agents) + adversarial licensing/loadability verification
**Hypothesis under test:** *structured-document traversal ≈ graph-data traversal, except graph traversal returns RELATIONSHIPS (most valuable for fraud detection). Clone the doc-tree parser+retriever approach for graphs first, then measure how far it gets.*

**Grounding:** The doc-tree shape is proven — ObliQA **76.1% recall vs flat 37.9% (+38 pp)**, GovReport-QS **89.6% vs 34.9% (+55 pp)**, both with no dataset-specific knob, reproduced across Nemotron-30B and Llama-70B ([hierarchical_datasets_findings.md](hierarchical_datasets_findings.md)). The load-bearing mechanisms per EE-Struct are **tree traversal, parent/sibling expansion, and authority-ordered assembly** — heading-path chunking alone contributes zero. Epic 7 as it stood assumed a Neo4j + Cypher + Microsoft-GraphRAG stack with synthetic fraud demo data, but had **no benchmark/recall-scoring story** — the gap this brief fills.

---

## 1. Production graph-RAG method shortlist

Ranked for enterprise fraud/relationship retrieval, with maturity flags.

| Rank | Method | Maturity | Best at | Fraud/relationship fit |
|------|--------|----------|---------|-------------------------|
| **1** | **neo4j-graphrag + LlamaIndex PropertyGraphIndex + Neo4j GDS** | **Production-mature (GA, first-party, LTS)** | Seed-then-traverse (vector→Cypher), Text2Cypher, GDS ring/centrality analytics | **Best-in-class.** Variable-hop Cypher + GDS (WCC/Louvain for rings, PPR/centrality for risk) are purpose-built for fraud. Fully on-prem (data-sovereignty ✓) |
| **2** | **Microsoft GraphRAG** | **Research demo — explicitly "not officially supported," breaking changes across minor versions** | Global "sensemaking" over large corpora; Leiden community hierarchy; local/global/DRIFT search | Strong for narrative/document-derived relationships; graph is LLM-extracted, not a curated transactional graph |
| **3** | **HippoRAG / HippoRAG 2** | Research-origin, productionizable (pip, MIT, vLLM/local) | **Single-step multi-hop** via Personalized PageRank; 10–30× cheaper / 6–13× faster than iterative retrievers | Strong fit — PPR seeded on query entities is relationship-diffusion. Soft ranking, not exact path enumeration |
| **4** | **LightRAG / nano-graphrag / fast-graphrag** | OSS, widely adopted, no GA/SLA | ~6× cheaper than MS GraphRAG (skip community summaries); real-time incremental updates | fast-graphrag's PPR + incremental updates suit streaming transaction graphs; LightRAG most production-packaged |

### Recommendation: clone **Microsoft GraphRAG** first — as the *conceptual analog*, served on Neo4j

The closest structural mirror of the doc-tree approach: its **Leiden community hierarchy is a tree of increasingly specific nodes, and global search descends/rolls-up that hierarchy exactly like doc-tree section-summary traversal** ([graphrag query overview](https://microsoft.github.io/graphrag/query/overview/)). The most direct "same machinery, different data" milestone.

Two qualifications for the backlog:

1. **Do not run MS GraphRAG as the production engine.** It is a research demonstration with breaking changes between minor versions and expensive indexing (one LLM call per chunk + per-community reports). Clone its **community-hierarchy + local/global retrieval design**, but materialize it on **Neo4j** (the E7-S1 stack) so ASP keeps a GA, on-prem, first-party substrate — the pattern Neo4j documents at [neo4j fraud-detection](https://neo4j.com/developer/industry-use-cases/finserv/fraud-detection/). **This matters more given the 80 req/min free-tier LLM limit** — one-LLM-call-per-chunk indexing at n=500 is budget-hostile.
2. **Add HippoRAG's Personalized PageRank as the "relationship expansion" primitive** early. It is the graph analog of doc-tree's parent/sibling expansion (§2) and is the mechanism most likely to be load-bearing, mirroring how expansion — not chunking — carried the doc-tree win.

Production vs research: **neo4j-graphrag/GDS = production** (substrate); **MS GraphRAG, HippoRAG, LightRAG family = research/OSS** (clone designs, own the ops).

Refs: [microsoft/graphrag](https://github.com/microsoft/graphrag) · [arxiv 2404.16130](https://arxiv.org/abs/2404.16130) · [neo4j-graphrag-python](https://github.com/neo4j/neo4j-graphrag-python) · [HippoRAG arxiv 2405.14831](https://arxiv.org/abs/2405.14831) · [HippoRAG 2 arxiv 2502.14802](https://arxiv.org/abs/2502.14802) · [HKUDS/LightRAG](https://github.com/HKUDS/LightRAG) · [circlemind-ai/fast-graphrag](https://github.com/circlemind-ai/fast-graphrag) · [Neo4j GDS](https://neo4j.com/docs/graph-data-science/current/)

---

## 2. The "doc-tree ≈ graph traversal" framing

Component-by-component mapping. Doc-tree components cited from `src/asp/retrieval/document_tree/retriever.py` and Epic 5.

| Doc-tree component (built) | Graph-RAG analog | Analogy holds? |
|---|---|---|
| **Parser → tree** (`markdown_parser.py`, `universal_parser.py`: markers → `TreeNode`s with `depth`, `parent_node_id`, `child_node_ids`, `heading_path`) | **Entity + relation extraction → graph** (LLM OpenIE / SimpleKGPipeline: text → typed nodes + typed edges) | **Partial.** Both convert raw text into a navigable structure. But the parser is *deterministic* over authored markers (`parse_confidence=1.0`); graph extraction is *LLM-probabilistic and lossy* — noisy extraction propagates downstream. **Biggest new risk.** |
| **`heading_path` / depth stack** (single-rooted hierarchy mirroring authored outline) | **Leiden community hierarchy** (C0 coarse → deeper sub-communities, bottom-up by graph clustering) | **Holds for traversal pattern, differs in origin.** Both are trees you ascend for broad questions and descend for specific ones. Doc-tree nodes are *authored sections*; community nodes are *synthesized summaries*. Doc-tree is intra-document; community tree is inter-document. |
| **Path-augmented BM25 retriever** (`_strategy_merged`: 0.4·heading-hit + 0.6·BM25 over title+keywords+preview; `clause_id` exact-match short-circuit) | **Local search / seed-node selection** (vector/hybrid search finds entry entities; neo4j `VectorCypherRetriever` seed step) | **Holds.** Both pick high-precision entry points before expansion. `clause_id` exact-match ≈ **Text2Cypher / CypherTemplateRetriever** exact structured lookup. |
| **Ancestor walk + sibling scan** (E5-S18 multi-hop expansion, hard-capped) | **Subgraph expansion** — Cypher variable-length `-[:REL*1..k]-`; HippoRAG/fast-graphrag **Personalized PageRank** diffusion from seed nodes | **Core of the analogy and the core value.** Doc-tree "expand to parent/sibling for context" ≈ graph "expand to connected entities for context." **This mechanism carried the doc-tree win** → first thing to build and measure on graphs. |
| **Authority ordering** (`skip_superseded`, `is_override`/`overrides_node_id`, `SourceAuthority` ordering) | **Source authority on nodes/edges + GDS-materialized scores** (edge provenance; PPR/centrality/ring-membership written back as node properties) | **Holds and extends.** Doc-tree orders by document authority; graph can order by *both* source authority *and* computed structural importance (centrality, ring risk) — strictly richer. |
| **Recall scoring** (`metrics/span_recall.py`: token-recall / whole-span recall vs section-grounded gold; ObliQA/GovReport-QS) | **Path-recall / subgraph-recall** (retrieved node/edge set vs gold reasoning path or gold answer-node set) | **Holds — the key methodological port.** Graph scores retrieved subgraph against gold triples/paths/answer-nodes (§3 datasets ship these). |

### Where relationships make it genuinely different (call these out in the epic)

- **Topology.** A doc tree is a single-rooted DAG with one dominant containment relation, traversed top-down. A KG is a **cyclic, multi-relational graph** traversed outward from seeds over many edge types and variable hop counts. No canonical "root" or "depth" — the Leiden hierarchy is a *derived* proxy, not authored.
- **The answer IS the path.** Doc-tree gold is a *span inside one node*. Graph gold is often *a path/subgraph across many nodes that never co-occur in any single passage* — the relationship itself is the answer (fraud rings, entity linkage). Pure similarity structurally cannot retrieve it; this is the shape's whole reason to exist.
- **Extraction is now a lossy, LLM-driven stage.** Doc-tree parsing is deterministic; graph construction quality gates everything downstream. Budget goes to extraction-prompt quality / entity resolution the doc-tree parser never needed.
- **Soft diffusion vs exact enumeration.** PPR gives "who/what is associated with X" — great for surfacing. Precise "show the exact fraud path" needs graph algorithms (shortest path, community detection / Cypher), which have no doc-tree analog.

**Net:** the clone is legitimate for the *retriever half* (seed → expand → authority-order → recall-score). The *parser half* is where the analogy breaks — deterministic marker parsing becomes probabilistic LLM extraction, the new risk to manage.

---

## 3. Benchmark dataset shortlist

Filter applied: genuinely graph-shaped (multi-hop/relational over entities), permissively licensed, loadable without credentialing, ships gold supporting facts / evidence paths for retrieval-recall scoring (mirroring ObliQA/GovReport-QS). All four **verified loadable and permissively licensed** in the verification pass.

| Dataset | Domain | Task | Gold evidence | License | Source | LLM-extraction cost @ n=500 | Why it fits |
|---|---|---|---|---|---|---|---|
| **MetaQA** *(sanity baseline)* | Movies (closed KB) | 1/2/3-hop KGQA over an explicit `kb.txt` triple store | Gold answer entities + hop-type; **KB triples shipped** → gold subgraph reconstructable | Creative Commons (WikiMovies non-commercial research) | **~0** (graph shipped) | Purest "walk relational edges" task with a shipped graph — controlled check that traversal works before touching noisy LLM-extracted graphs. GitHub `yuyuz/MetaQA`; HF `haritzpuerto/MetaQA_Datasets` (**note: `rmanluo/...` path does NOT exist**) |
| **STaRK-PRIME** *(graph-native)* | Biomedicine | Retrieve answer node(s) from a semi-structured heterogeneous KB given relational+textual query | Verified gold `answer_ids` over an explicit SKB graph; entity-recall via SKB relations | **Code MIT; data CC BY 4.0** | **~0** (SKB shipped) | **True KG retrieval, not Wikipedia-QA.** Answers reachable only via relational constraints — pure vector search fails by design. Hit@k / Recall@20 / MRR map onto ASP recall. `pip install stark-qa`; HF `snap-stanford/stark` |
| **2WikiMultiHopQA** *(the real proof)* | Wikipedia + Wikidata | Multi-hop QA (comparison / inference / compositional / bridge) | **Richest:** sentence-level `supporting_facts` **+** explicit `evidences` = `[subject, relation, object]` reasoning-path triples | **Apache 2.0** | ~5k calls (~10 paras × 500) → **~70 min/run @ 80/min; cache per-paragraph** | Questions built from Wikidata triples → each answer is a **concrete KG path**. `[s,r,o]` triples = direct analog of gold sections → **path-recall works out of the box.** HF `xanhho/2WikiMultihopQA` (viewer disabled → `trust_remote_code=True`) |
| **MuSiQue-Ans** *(hardest, defer to P1)* | Wikipedia | 2–4 hop QA, composed so each hop depends on the previous | Gold supporting paragraphs **+** `question_decomposition` (ordered sub-Qs, per-hop support idx) | **CC BY 4.0** | most (2–4 hop, ~20 paras) | Connected-chain composition defeats shortcuts (unlike HotpotQA's shallow 2-hop). Step-wise gold enables **per-hop path recall.** HF `dgslibisey/MuSiQue` |

**Recommended set (cost-ordered for the 80 req/min free NVIDIA endpoint at n=500):**
**MetaQA + STaRK-PRIME (P0, ~0 LLM cost — shipped graphs, recall is generation-independent) → 2WikiMultiHopQA (P0, the extraction-based proof, ~70 min/run cached) → MuSiQue (P1, hardest chains).**
This spans clean shipped-graph traversal (MetaQA), graph-native retrieval (STaRK), template-clean extracted paths (2Wiki), and hard composed chains (MuSiQue) — the graph-shape equivalent of how ObliQA + GovReport-QS anchored the doc-tree proof. HotpotQA is *already in the suite* and stays as a low-bar boundary (shallow 2-hop, world-knowledge-answerable) — continuity, not a stressor.

**Why cost-order this way:** path-recall is generation-independent (like `span_recall.py`), so on shipped-graph datasets (MetaQA, STaRK) the primary metric costs almost no rate-limited calls — seed with a *local* embedding model. Only 2Wiki's extraction is budget-heavy, and per-paragraph caching makes it a one-time ~70 min cost per model.

### Fraud-specific gap — flagged honestly

**No public dataset combines the fraud/finance domain AND an NL-question→gold-subgraph layer. That layer does not exist off the shelf and must be authored.**

- **Fraud graph datasets ship structure but zero NL-QA:** Elliptic1/2/++ (node/subgraph *classification*, anonymized features → no interpretable questions), IEEE-CIS (tabular, license-restricted, hidden test labels), PaySim (shallow one-hop). Elliptic2 (122K labeled subgraphs, CC BY 4.0) is the closest "gold-subgraph" analog but has no question side.
- **KGQA datasets have the NL-QA + gold-path structure but are the wrong domain** (movies/Freebase trivia).
- **Best path to a fraud benchmark:** use a **synthetic, open, pattern-labeled AML generator** — **IBM AMLworld** ([Kaggle `ealtman2019/ibm-transactions-for-anti-money-laundering-aml`](https://www.kaggle.com/datasets/ealtman2019/ibm-transactions-for-anti-money-laundering-aml), CC BY) or **[AMLSim](https://github.com/IBM/AMLSim)** (Apache 2.0) — where embedded laundering typologies (fan-in/out, cycles, stacking) act as **guaranteed-correct gold subgraphs**, then author an NL-question layer over them. AMLSim is strongest because *you generate ground truth rather than annotate it.* This is a **build task, not a download** — scope it explicitly (P2 deferred). Also, the generated graph = ~0 extraction cost, which fits the LLM budget.

Refs: [2WikiMultihopQA](https://huggingface.co/datasets/xanhho/2WikiMultihopQA) · [StonyBrookNLP/musique](https://github.com/StonyBrookNLP/musique) · [snap-stanford/stark](https://github.com/snap-stanford/stark) · [yuyuz/MetaQA](https://github.com/yuyuz/MetaQA) · [Elliptic2 arxiv 2404.19109](https://arxiv.org/abs/2404.19109)

---

## 4. Proposed graph-RAG dataset inclusion filter

Adapted from the doc-tree criteria ([hierarchical_datasets_findings.md](hierarchical_datasets_findings.md), [ADR 0001](../adr/0001-reject-min-context-floor-adopt-recall-datasets.md)). A dataset (or production data source) is a **knowledge-graph shape target** when it has:

1. **Genuine relational multi-hop** — the answer requires ≥2 connected hops across entities that do **not co-occur** in a single passage/row (structurally unanswerable by flat vector top-k). *Rejects: HotpotQA-style shallow/parallel comparisons, single-hop lookups, PaySim one-hop.*
2. **Answer-is-the-path displacement** — the gold answer lives in the *relationship/path/subgraph*, not in one node's text. (Graph analog of doc-tree's "gold in sibling/parent of the keyword hit.")
3. **Gold evidence path or answer-node set** — ships gold triples, reasoning decomposition, SPARQL/graph-query, or verified answer-node IDs, so **path-recall / subgraph-recall is scorable** without hand-annotation. *The hard gate — eliminates most fraud graph datasets.*
4. **Permissive license + no-credential load** — CC BY / Apache / MIT, loadable via HF/pip/public download without gating. *Rejects: IEEE-CIS.*
5. **Explicit or reconstructable graph substrate** — the entity-relation graph is shipped or deterministically buildable (triples, SKB, kb.txt), so retrieval scores against the *same* graph the gold path lives in.
6. **Fraud/relationship relevance (soft, tie-breaker)** — prefer finance/network-crime/supply-chain; where absent, accept general-domain KGQA as *methodological template* and plan to port the harness onto a synthetic fraud graph.

**Falsifiable test (ported from ADR 0001):** ASP wins with **subgraph expansion on**, degrades gracefully with it **off**, and needs **no dataset-specific knob**. If turning off PPR/Cypher expansion collapses the advantage but doesn't invert vs flat, the relationship mechanism — not extraction tuning — is doing the work. The graph analog of the doc-tree parent/sibling-expansion ablation.

---

## 5. Recommended first experiment

**Milestone: "Clone doc-tree for graphs" — minimal seed→expand→recall-score loop, starting on the shipped-graph datasets to stay within the LLM budget, then 2WikiMultiHopQA for the extraction-based proof.**

Smallest thing that tests the hypothesis end-to-end, reusing the existing same-condition ASP-vs-flat harness (CUAD/MAUD/Travel Policy/JCP Credit/HotpotQA/ObliQA/GovReport-QS):

1. **Build the graph (parser analog):** for MetaQA/STaRK use the *shipped* graph (~0 LLM). For 2Wiki, LLM entity+relation extraction over the ~10 candidate paragraphs per question → typed nodes/edges (bounded, cached, isolates the retriever from extraction-scale cost).
2. **Seed (path-augmented BM25 analog):** vector/hybrid seed-node selection from question entities — the `_strategy_merged` analog. Local embedding model → no rate-limited calls.
3. **Expand (ancestor/sibling-walk analog — load-bearing):** k-hop Cypher traversal *and* HippoRAG-style Personalized PageRank from seed nodes, hard-capped (mirror E7-S6 `max_hops`, doc-tree's hard cap).
4. **Assemble + authority-order:** subgraph → `ContextBundle` as node-edge-node triples (E7-S5 format), source-authority ordered, with `DroppedItem` logging on truncation (honors CLAUDE.md "no silent truncation").
5. **Score:** **path-recall / supporting-triple recall@k** against gold triples/paths/answer-nodes — the direct analog of doc-tree token-recall. Also report generation F1/EM.

**The metric that proves it:**

> **Supporting-triple / path recall@k: ASP graph-expansion beats the flat vector baseline by a clear margin (target ≥ +15 pp, aspiring to the doc-tree's +38–55 pp band), same model both sides, no dataset-specific knob** — AND the falsifiable ablation holds (expansion off → advantage collapses toward flat but does not invert).

Report per the mandatory benchmark-report standard at `docs/research/{run_tag}_report.md`, adding a **path-recall** column alongside EM/F1. If it clears the bar, promote to MuSiQue (hard chains), then — only then — invest in the AMLSim synthetic-fraud NL-QA layer for the domain-specific proof.

**Why this order:** proves the *retriever-half* clone (where the analogy holds) on datasets with ready-made gold paths and the lowest LLM cost first, before spending budget on the *parser-half* (LLM extraction at scale) and the fraud NL-QA authoring (net-new build work). Fails fast and cheap if relationship-expansion doesn't reproduce the doc-tree mechanism.

---

*Seeds [epic-07-knowledge-graph.md](../../product-backlog/knowledge-graph/epic-07-knowledge-graph.md) and [epic-14-graph-benchmark.md](../../product-backlog/knowledge-graph/epic-14-graph-benchmark.md). Graph-shape north-star: [knowledge_graph_mission.md](../knowledge_graph_mission.md).*
