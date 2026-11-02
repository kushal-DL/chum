# Document Tree Retrieval — Gap Analysis

**Epic:** E5 — Document Tree Retrieval Agent  
**Author:** Kushal Sharma  
**Date:** 2026-06-14  
**References:**  
- Mission: [docs/document_tree_mission.md](document_tree_mission.md)  
- Backlog: [product-backlog/epic-05-document-tree.md](../product-backlog/document-tree/epic-05-document-tree.md)

---

## Summary

The current backlog (E5, 10 stories) covers the core ingest-and-retrieve pipeline competently. The mission statement identifies five architectural challenges; cross-referencing them against the existing stories reveals **seven capability gaps** that are either absent from the backlog or present only implicitly with no acceptance criteria.

| Gap ID | Gap Title | Severity | Proposed Story |
|--------|-----------|----------|----------------|
| G1 | Cross-reference resolution | Critical | E5-S11 |
| G2 | Condition / negation / exception modelling | Critical | E5-S12 |
| G3 | Temporal versioning (node-level effective dates) | High | E5-S13 |
| G4 | Precision verification layer | High | E5-S14 |
| G5 | Domain-specific ingestion profiles | Medium | E5-S15 |
| G6 | Schema status inconsistency (S1 marked Done vs Not Started) | Housekeeping | Fix in-place |
| G7 | Mission statement not linked from epic file | Housekeeping | Fix in-place |

---

## Gap Detail

### G1 — Cross-reference resolution (Critical)

**Mission requirement:**  
> "A retrieval system that cannot resolve cross-references at query time gives half an answer — and in high-stakes domains, a confident half-answer is more dangerous than silence."

**What the backlog has:**  
The `TreeNode` schema has `clause_id`, `parent_node_id`, `child_node_ids`, `overrides_node_id`, and `superseded_by`. These cover parent-child and override relationships within a document. There is no field for arbitrary intra-document references (`see section 4.3`) or inter-document references (`see Exhibit C`, `as defined in the Master Agreement`). There is no resolver component.

**What is missing:**
- `TreeNode.cross_references: List[CrossReference]` field — structured list of references found in the node's content, each with a target `node_id` or external document slug
- `CrossReferenceResolver` component — at retrieval time, expands the retrieved node by following its cross-references and injecting resolved content into the ContextBundle
- Support for *defined term* resolution — when a node uses a term defined in a Definitions or Glossary section, that definition should be auto-injected

**Impact if not built:**  
Insurance policy queries will return clauses that reference endorsements without including the endorsement. Financial queries will return numbers defined elsewhere without the definition. The ContextBundle will look complete but contain dangling references — the model will hallucinate the missing detail.

**Proposed story:** E5-S11

---

### G2 — Condition / negation / exception modelling (Critical)

**Mission requirement:**  
> "The retrieval architecture must model conditions, negations, and exceptions as first-class structural properties — not as tokens to be semantically matched."

**What the backlog has:**  
The current schema and retriever are retrieval-by-position systems — they find the most relevant node by path matching or BM25 and return it. There is no mechanism for detecting that a retrieved positive clause has associated exception nodes, and no mechanism for automatically including them.

**What is missing:**
- `TreeNode.condition_type: Optional[ConditionType]` field — enum: `inclusion`, `exclusion`, `exception_to_exclusion`, `condition`, `definition` — allowing the retriever to understand the semantic role of a node beyond its structural position
- `TreeNode.applies_to: List[str]` field — list of `node_id`s this exception/condition node qualifies
- `ExceptionAwareRetriever` logic — when a positive clause is retrieved, automatically scan for exception/exclusion nodes that reference it via `applies_to` and append them to the result set
- Retrieval strategy 5: "exception scan" — run after strategies 1-4, before returning to caller

**Impact if not built:**  
Healthcare and insurance queries will return "this plan covers X" without "except when Y" — exactly the class of error that makes enterprise AI dangerous rather than merely inaccurate.

**Proposed story:** E5-S12

---

### G3 — Temporal versioning at node level (High)

**Mission requirement:**  
> "The policy effective on the date of the claim may not be the current policy. The retrieval system must understand which version of truth applies to which temporal scope."

**What the backlog has:**  
`TreeNode` has `effective_date` and `superseded_by` fields. `SourceAuthority` has a document-level date. However:
- There is no retrieval-time API to specify "retrieve as-of date X"
- There is no version chain — only a single `superseded_by` pointer, no history of who superseded whom
- The ingest script has no mechanism for loading multiple versions of the same document and linking their node-level amendments
- There is no test or acceptance criterion that validates temporal queries

**What is missing:**
- `TreeRetriever.retrieve(query, as_of_date: Optional[date])` parameter — filters out nodes where `effective_date > as_of_date` or `superseded_by` is set and the replacement's `effective_date <= as_of_date`
- Ingest support for document versioning — when two versions of the same document are ingested, auto-link superseded nodes
- At least one benchmark question class that validates temporal retrieval correctness

**Impact if not built:**  
For insurance claims, policy compliance, and financial audit queries — the date the question is asked about may differ from the date the document was current. Returning the wrong version is a compliance failure, not just an accuracy miss.

**Proposed story:** E5-S13

---

### G4 — Precision verification layer (High)

**Mission requirement:**  
> "Does the retrieved content answer the query, or does it answer an adjacent question? Are there conditions on this answer not yet retrieved? What is explicitly missing?"

**What the backlog has:**  
E5-S7 (`TreeRetriever`) logs strategy used and latency. The ContextBundle has `dropped_items`. There is no post-retrieval verification step that assesses whether the assembled bundle is actually complete for the query.

**What is missing:**
- `RetrievalVerifier` component — after bundle assembly, checks: (a) are all cross-references in the returned nodes resolved, (b) are all conditions/exceptions for returned nodes included, (c) does the bundle's structural coverage match the query intent
- `ContextBundle.missing_items` field — structured list of things the verifier identified as relevant but not retrievable (e.g. referenced external document not in the store)
- `ContextBundle.verification_notes: str` — human-readable summary of what the verifier found and flagged
- Confidence score with explanation (beyond the existing `parse_confidence` per node)

**Impact if not built:**  
The system has no self-awareness of incomplete answers. A query that requires three related clauses but only retrieves two will appear successful internally — the model receives a partial bundle and either hallucinates the missing clause or answers incorrectly without flagging the gap.

**Proposed story:** E5-S14

---

### G5 — Domain-specific ingestion profiles (Medium)

**Mission requirement:**  
> Domain-specific challenges for financial, insurance, healthcare, and enterprise policy documents each require distinct structural treatment.

**What the backlog has:**  
Parsers are generic — they use heading hierarchy as structural signal regardless of document type. There is no concept of a domain profile that influences parse strategy, field population, or retrieval configuration.

**What is missing:**
- `DocumentProfile` enum: `financial_filing`, `insurance_policy`, `healthcare_formulary`, `enterprise_policy`, `generic`
- Profile-aware parser configuration: financial filings emphasize table extraction and period tagging; insurance policies emphasize condition type tagging and endorsement linking; healthcare docs emphasize exact-match numeric fields; enterprise policies emphasize jurisdiction and version tagging
- Profile-aware retrieval: financial queries default to table nodes first; insurance queries always run exception scan; healthcare queries use exact-match strategy before BM25
- `--profile` flag on ingest script

**Impact if not built:**  
The system works well on travel policies (the demo use case) but will need significant per-domain tuning before it handles insurance, financial, or healthcare documents at production precision. Not building domain profiles now means that tuning happens ad-hoc at paper-writing time — harder to reproduce and harder to benchmark.

**Proposed story:** E5-S15

---

### G6 — Schema status inconsistency (Housekeeping)

**What was found:**  
In the epic-05 file, the Status Summary table shows `E5-S1 TreeNode Pydantic schema: Done`, but the story card for E5-S1 has `Status: Not Started`. This is a data entry inconsistency — the status table reflects the correct state (Done, as the schema is implemented and in use by S2-S9). The story card is stale.

**Fix:** Update E5-S1 story card status to `Done` and tick all acceptance criteria checkboxes.

---

### G7 — Mission statement not linked from epic file (Housekeeping)

The mission statement document created at `docs/document_tree_mission.md` is not yet referenced from the epic file. The epic's goal statement should point to the mission for the rationale behind design decisions.

**Fix:** Add mission reference to epic-05 file header.

---

## What the Current Backlog Does Well

These areas are well-specified and do not need new stories:

| Area | Assessment |
|------|-----------|
| TreeNode schema design | Comprehensive — every field has a rationale. The field-rationale table in E5-S1 is the right level of detail. |
| Parser coverage (Markdown, DOCX) | Complete for text-heavy structured documents |
| Table extraction | Well-specified; `TableMetadata` on every table node is the right design |
| Tree builder / normaliser | Covers structure validation, token accounting, parent-child links |
| MCP integration | Tool description is well-crafted for routing accuracy |
| Ingest script | Idempotent, multi-format, correct output |
| Override and authority fields | `is_override`, `overrides_node_id`, `jurisdiction` are present and well-motivated |

---

## Proposed New Stories

The following stories are proposed to close the identified gaps. Full acceptance criteria are in [epic-05-document-tree.md](../product-backlog/document-tree/epic-05-document-tree.md).

| Story | Title | Gap | Priority | Effort | Phase |
|-------|-------|-----|----------|--------|-------|
| E5-S11 | Cross-reference resolver | G1 | P1 | L | 3 |
| E5-S12 | Condition / exception type modelling | G2 | P1 | L | 3 |
| E5-S13 | Temporal versioning (as-of-date retrieval) | G3 | P2 | M | 3 |
| E5-S14 | Precision verification layer | G4 | P2 | M | 3–4 |
| E5-S15 | Domain-specific ingestion profiles | G5 | P2 | M | 3 |

---

## Impact on Other Epics

| Epic | Impact |
|------|--------|
| E3 — ASP Protocol Core | `ContextBundle` needs `missing_items` and `verification_notes` fields (E5-S14). Requires a schema update in E3-S3 and re-export of JSON schema (E3-S8). |
| E8 — Orchestrator | The orchestrator's routing description for `document_tree_search` should mention exception-aware and cross-reference-aware retrieval once G1/G2 are closed. |
| E9 — Memory Contracts | Cross-reference resolved nodes should carry their own `reuse_key` so they are not double-fetched in subsequent queries. |
| E10 — Benchmark | The evaluation dataset (E10-S3) must include questions that exercise cross-reference resolution, exception retrieval, and temporal versioning — not just positive-clause lookup. |
| E11 — Research Paper | The paper's related work section should cite the structural challenges documented here as the motivation for ASP's document-tree shape. |

---

*This gap analysis should be reviewed after Phase 3 implementation to confirm gaps were closed or explicitly deferred with rationale.*
