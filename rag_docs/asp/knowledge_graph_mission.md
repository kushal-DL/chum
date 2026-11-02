# Knowledge-Graph Retrieval — Mission Statement

**Epic:** E7 — Knowledge Graph Retrieval Agent
**Author:** Kushal Sharma
**Date:** 2026-07-29
**Status:** Foundational reference — seeds the graph-shape backlog
**Companion:** [document_tree_mission.md](document_tree_mission.md) (the shape this one is cloned from), [graph_rag_research_brief.md](research/graph_rag_research_brief.md) (the method/dataset evidence base)

---

## The Fundamental Problem

The document-tree shape proved that **the meaning of a sentence is inseparable from its position in the document's hierarchy** — and that retrieving in the document's own coordinate system beats flat vector search by +38–55 pp recall. The knowledge-graph shape extends the same thesis to a different structure:

**The answer to a relationship question is inseparable from the path that connects the entities — and that path lives in no single passage.**

"ACME Corp is fine" carries no meaning without: which accounts it shares a tax ID with, which invoices duplicate across its vendor cluster, which three-hop chain routes payments back to a shell company. "This vendor is approved" is dangerous without the connected-component that reveals it sits in a laundering ring. Any retrieval system that scores passages independently — vector or BM25 — **structurally cannot** retrieve the relationship, because the relationship is not *in* any passage. It is in the edges between them.

The Knowledge Graph Retrieval agent exists to make **relationships first-class retrieval targets**: build the entity-relation graph at ingest time, then seed → expand → authority-order → recall-score over it, exactly as the doc-tree agent does over sections.

---

## The Analogy — and Where It Breaks

This shape is a deliberate clone of the document-tree retriever. The retriever half of the analogy holds; the parser half is where relationships make it genuinely different.

### Where the analogy holds (clone directly)

| Doc-tree mechanism (proven load-bearing) | Graph analog |
|---|---|
| Path-augmented BM25 seed selection (`_strategy_merged`) | Vector/hybrid **seed-node selection** from query entities |
| `clause_id` exact-match short-circuit | **Text2Cypher / template** exact structured lookup |
| Ancestor walk + sibling scan (E5-S18), hard-capped | **k-hop Cypher traversal + Personalized PageRank** diffusion, hard-capped |
| Authority ordering (`SourceAuthority`, `is_override`) | Source authority **+ GDS-computed structural importance** (centrality, ring membership) |
| Token/span recall vs section-grounded gold (`span_recall.py`) | **Path-recall / subgraph-recall** vs gold triples / answer-nodes |

The doc-tree win came from **expansion, not chunking** (EE-Struct: heading-path chunking alone contributed zero). The prediction for graphs is the same: **subgraph expansion — Cypher traversal + PPR — is the load-bearing mechanism**, and it must be built and ablated first.

### Where relationships make it genuinely different (the new risks)

1. **Extraction is a lossy, LLM-driven stage.** Doc-tree parsing is deterministic (`parse_confidence=1.0`) over authored markers. Graph construction is probabilistic LLM entity/relation extraction — noisy extraction propagates into every downstream step. **This gates everything and is the biggest new risk.** On a free 80 req/min endpoint it is also the budget bottleneck.
2. **Topology.** A doc tree is a single-rooted DAG with one containment relation, traversed top-down. A KG is a **cyclic, multi-relational graph** traversed outward from seeds over many edge types and variable hop counts. There is no authored root or depth — the Leiden community hierarchy is a *derived* proxy.
3. **The answer IS the path.** Doc-tree gold is a span inside one node. Graph gold is often a path/subgraph across nodes that never co-occur — the relationship itself is the answer.
4. **Soft diffusion vs exact enumeration.** PPR surfaces "what is associated with X." Precise "show the exact fraud path" needs graph algorithms (shortest path, community detection) with no doc-tree analog.

---

## The Architectural Answer

### Principle 1: Retrieve in the graph's own coordinate system

The entity-relation graph — its typed nodes, typed edges, and community hierarchy — is the retrieval index. Embedding space seeds the entry points; traversal does the work.

**Query routing by precision (in order):**
1. **Exact structured lookup** (Text2Cypher / template) — zero fuzzy reasoning, maximum precision. The `clause_id`-exact-match analog.
2. **Seed-node selection** — vector/hybrid search over entity names/descriptions finds entry entities.
3. **Subgraph expansion** — k-hop Cypher traversal + Personalized PageRank diffusion from seeds, hard-capped.
4. **Community roll-up** — for global "what are the main patterns?" questions, ascend the Leiden community hierarchy (the doc-tree section-summary analog).

### Principle 2: Subgraph assembly is not a retrieval afterthought

When you retrieve a seed node, you have found a starting point. The relationship-complete answer requires:

- **Outward expansion:** connected entities within the hop cap that provide the relationship context
- **Path materialization:** the actual node-edge-node chain(s) connecting the query entities, rendered as readable triples
- **Structural-importance ordering:** GDS-computed centrality / ring-membership so the assembler surfaces the load-bearing nodes first
- **Authority ordering:** source authority on nodes/edges, exactly as doc-tree orders by document authority

The `ContextBundle` is not a bag of retrieved nodes — it is a **subgraph** with its connecting paths made explicit.

### Principle 3: Recall verification before returning

- Did the retrieved subgraph contain the gold path / answer-nodes?
- Was any node/edge dropped by the hop cap or token budget? (log to `dropped_items` — **no silent truncation**, per CLAUDE.md)
- Is the extracted graph confident enough here, or is extraction noise the failure mode?

### Principle 4: Every node/edge property exists because the LLM cannot safely infer it

Inherited verbatim from the doc-tree mission. Edge type, direction, provenance, and computed importance each eliminate a disambiguation step the LLM would otherwise perform over raw triples — or let the retriever rank without touching the LLM.

---

## Domain-Specific Targets

### Fraud / AML (the flagship relationship use-case)
- The signal is the **subgraph topology**: fan-in/fan-out, cycles, stacking, shared-attribute clusters (same tax ID, address, device)
- Rings are **connected components / communities** (GDS WCC, Louvain); risk is **centrality / PPR** — these are the retrieval targets, not passages
- **Honest gap:** no public dataset pairs a fraud graph with an NL-question→gold-subgraph layer. Must be authored over a synthetic AML generator (AMLSim / IBM AMLworld) where embedded typologies are guaranteed-correct gold subgraphs.

### Supply chain
- Component dependency and single-point-of-failure are **reachability / articulation-point** queries — graph-native, no vector analog.

### Entity networks / KYC
- "Who connects to whom, and how" is variable-hop traversal with entity resolution as the extraction-quality gate.

---

## Success Definition

The Knowledge Graph Retrieval agent succeeds when:

1. **Relationship recall:** path-recall / supporting-triple recall@k beats the flat vector baseline by ≥ +15 pp (aspiring to the doc-tree +38–55 pp band), same model both sides, **no dataset-specific knob**.
2. **Mechanism attribution (falsifiable):** with subgraph expansion **off**, the advantage collapses toward flat but does not invert — proving relationship expansion, not extraction tuning, does the work.
3. **Completeness:** the returned `ContextBundle` contains the connecting path(s), not just the endpoint entities.
4. **Provenance:** every answer cites the node-edge-node path traceable to source entities.
5. **No silent truncation:** anything dropped by the hop cap or token budget is logged in `dropped_items`.
6. **Budget honesty:** on the free 80 req/min endpoint, shipped-graph datasets (MetaQA, STaRK) run recall at ~0 LLM cost; extraction-based datasets (2Wiki) cache per-paragraph extraction so cost is one-time and bounded.

---

## What This Is Not

- Not a vector search with a graph wrapper — expansion over edges is the point, not a re-ranker
- Not running Microsoft GraphRAG as the production engine — its *design* (community hierarchy, local/global search) is cloned onto Neo4j; the research library is not the ops substrate
- Not a system where the LLM reasons over raw triples to reconstruct paths the retriever should have materialized
- Not a fraud demo on synthetic data masquerading as a benchmark — the benchmark uses external, permissively-licensed, gold-path datasets first (§3 of the research brief)

---

*This document is the north-star specification for the graph-shape work in E7 and E14. Implementation stories must be evaluated against this mission. Evidence base: [graph_rag_research_brief.md](research/graph_rag_research_brief.md).*
