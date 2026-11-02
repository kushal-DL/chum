# Document Tree Retrieval — Mission Statement

**Epic:** E5 — Document Tree Retrieval Agent  
**Author:** Kushal Sharma  
**Date:** 2026-06-14  
**Status:** Foundational reference — not superseded by implementation

---

## The Fundamental Problem

Most retrieval systems treat documents as bags of sentences and use embedding similarity to rank them. For commodity queries this is adequate. For financial filings, insurance policies, healthcare formularies, and enterprise compliance documents it fails for a precise structural reason:

**The meaning of a sentence is inseparable from its position in the document's hierarchy.**

"Revenue: $1.2B" carries no meaning without: which period, which accounting standard, which inclusions and exclusions defined three sections earlier. "Coverage applies" is dangerous without the exclusion clause two paragraphs below it. Any retrieval system that divorces text from structure will produce confident-sounding wrong answers.

The Document Tree Retrieval agent exists to fix this at the architecture level — not at query time by prompting the model to be careful, but at ingest time by preserving the semantic architecture of the document as the retrieval index itself.

---

## The Five Core Challenges

### 1. Structure carries semantics

A balance sheet is a relational structure where row and column position encodes meaning. A policy has a deliberate hierarchy: general terms → specific inclusions → exclusions → exceptions to exclusions. Collapsing this into a linear text stream destroys semantic relationships.

Most systems flatten documents and then try to recover semantics at query time via LLM reasoning. That is backwards — it burns tokens and introduces hallucination risk. **The tree must be built at ingest time.**

### 2. Cross-reference webs

Insurance policies are the extreme case: a clause references a defined term in section 2, which is modified by Endorsement A, which only applies if Schedule B criteria are met. Financial filings constantly reference "as defined in Note 14" or "subject to Exhibit C."

A retrieval system that cannot resolve cross-references at query time gives half an answer — and in high-stakes domains, a confident half-answer is more dangerous than silence.

### 3. Negation, conditions, and exceptions

*"This plan covers hospitalization except for pre-existing conditions during the first 12 months unless the employer elected the waiver endorsement."*

Dense retrieval finds the positive signal ("covers hospitalization") and frequently misses the exceptions. In healthcare and insurance this is catastrophic. The retrieval architecture must model conditions, negations, and exceptions as first-class structural properties — not as tokens to be semantically matched.

### 4. Multi-document temporal reasoning

The policy effective on the date of the claim may not be the current policy. A Q3 filing supersedes Q2 on some numbers. Healthcare formularies change quarterly. The retrieval system must understand which version of truth applies to which temporal scope — this cannot be delegated to model inference.

### 5. Tables are first-class semantic units

A balance sheet linearized as text is nearly meaningless. Row label + column header + cell value form a three-way relationship that only makes sense as a table structure. Tables must be ingested as typed semantic objects with column headers preserved, not as prose.

---

## The Architectural Answer

### Principle 1: Retrieve in the document's own coordinate system

The document tree — its section hierarchy, defined terms, cross-reference graph — is the retrieval index. Embedding space is a fallback for when structure is insufficient, not the primary mechanism.

**Query routing by shape (in order of precision):**
1. Clause ID exact match (`CL-031`) — zero LLM reasoning, maximum precision
2. Section path keyword match — heading text contains query keywords
3. Shallow BM25 over titles + first 200 chars — structure-aware lexical search
4. Dense embedding fallback — only when structural signals are exhausted

### Principle 2: Context assembly is not a retrieval afterthought

When you retrieve a node, you have found a starting point. The semantically complete answer requires:

- **Upward resolution:** every ancestor section from root to the retrieved node — each ancestor provides constraining context
- **Sibling awareness:** adjacent clauses that qualify the retrieved clause
- **Cross-reference resolution:** every `[[defined_term]]` and `see section X` reference, recursively resolved
- **Exception capture:** scan the condition/exclusion graph for any nodes that apply to the retrieved content
- **Override precedence:** if an override node exists for the retrieved content, include it first

The ContextBundle is not a chunk — it is a semantically complete unit with all dependencies resolved.

### Principle 3: Precision verification before returning

- Does the retrieved content answer the query, or does it answer an adjacent question?
- Are there conditions on this answer not yet retrieved?
- Is this answer time-bounded (check temporal scope markers)?
- What is explicitly missing that a complete answer would require?

The response includes: the answer, exact structural provenance (document → section → subsection → clause), qualifying conditions, what was NOT found, and a confidence score with explanation.

### Principle 4: Every field exists because the LLM cannot safely infer it

If a field is absent and the LLM must compensate by reasoning over content, we pay tokens and accept hallucination risk. Every field on `TreeNode` eliminates a disambiguation step the LLM would otherwise perform — or enables the retriever to filter/rank without touching the LLM at all. This is the schema design principle that all future extensions must respect.

---

## Domain-Specific Targets

### Financial filings
- Tables are the primary retrieval unit; numbers only make sense with row/column/period context
- Defined terms (e.g. "Net Revenue") must resolve to the document's specific definition
- Comparative retrieval across periods requires temporal alignment of the document tree

### Insurance policies
- The coverage model is a tree: positive coverage at root → inclusions → exclusions → exceptions to exclusions
- Retrieval that does not traverse the full root-to-leaf path will produce wrong answers
- Endorsements that override base clauses must be versioned, linked via `is_override` / `overrides_node_id`, and applied in precedence order

### Healthcare documents
- Dosing information requires exact match, not semantic similarity — "up to 10mg" and "not less than 10mg" are polar opposites but similar in embedding space
- Clinical criteria for prior authorization are decision trees embedded in policy text; they must be extracted as decision trees, not prose
- Formulary version at the date of the clinical event must be retrievable, not just the current version

### Enterprise policies
- Version management: the policy in effect at the time of the incident, not the current policy
- Approval chains embedded in policy structure need to be navigable as a graph
- Jurisdiction scoping at node level (`jurisdiction` field) — not just at document level

---

## Success Definition

The Document Tree Retrieval agent succeeds when:

1. **Precision:** 17 out of 20 adversarially chosen benchmark questions return the correct structural node as the top result
2. **Completeness:** The returned ContextBundle contains all conditions and exceptions relevant to the answer — not just the positive clause
3. **Latency:** p99 retrieval latency ≤10 s (beating PageIndex 30–120 s baseline)
4. **Token efficiency:** Context bundle tokens ≤ 40% of what flat RAG would have sent for the same question
5. **Provenance:** Every answer includes exact structural citation traceable to a node_id and heading_path
6. **No silent truncation:** If context is dropped from the bundle, it is logged in `ContextBundle.dropped_items`

---

## What This Is Not

- This is not a general-purpose vector search system with a tree wrapper
- This is not a PDF reader that extracts text and chunks it
- This is not a system where the LLM compensates for poor retrieval with extra reasoning
- This is not a system where "close enough" answers are acceptable in high-stakes domains

---

*This document is the north-star specification for E5. Implementation stories in [epic-05-document-tree.md](../product-backlog/document-tree/epic-05-document-tree.md) must be evaluated against this mission. New stories should close gaps identified in [document_tree_gap_analysis.md](document_tree_gap_analysis.md).*
