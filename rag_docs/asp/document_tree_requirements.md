# Document Requirements for ASP Document Tree Retrieval

This guide tells you what your documents must look like before you ingest them
through the ASP Document Tree agent.  The parser is strict by design — a document
that violates these rules raises a `TreeValidationError` at ingest time rather than
silently skipping sections or producing a corrupt tree.

---

## Why strictness matters

The tree structure **is** the retrieval index.  If a heading is missing or
mis-typed, the parser cannot know which section the content below it belongs to.
A silent skip means that content becomes unreachable to the retriever — no error,
no warning, just missing answers at query time.  `TreeValidationError` is preferable
to that outcome.

---

## Required heading hierarchy

Every document must have a strict, unbroken heading hierarchy:

| Level | Markdown | DOCX style | `node_type` | `depth` |
|-------|----------|------------|-------------|---------|
| 1 | `#` | `Heading 1` | `document` (root) | 0 |
| 2 | `##` | `Heading 2` | `section` | 1 |
| 3 | `###` | `Heading 3` | `subsection` | 2 |
| 4 | `####` | `Heading 4` | `clause` | 3 |

### The single most important rule

**You must not skip a heading level.**

A section cannot jump from depth 0 (H1) directly to depth 2 (H3) without an H2
in between.  The parser will raise `TreeValidationError: orphan node` and halt.

```
# Policy Document            ← OK  (depth 0)
## 1 Baggage Allowance       ← OK  (depth 1, parent = root)
### 1.1 Hand Luggage         ← OK  (depth 2, parent = §1)
#### 1.1.1 Prohibited Items  ← OK  (depth 3, parent = §1.1)
```

This is invalid and will raise:

```
# Policy Document            ← OK  (depth 0)
### 1.1 Hand Luggage         ← RAISES: orphan at depth 2, no depth-1 ancestor
```

---

## Pre-flight checklist for document authors

Before ingesting a new document, verify all of the following:

- [ ] **Exactly one H1 / Heading 1** — this becomes the document root.  Multiple H1s
  raise `multiple root nodes found`.
- [ ] **No skipped heading levels** — every H3 must be preceded by an H2;
  every H4 by an H3.
- [ ] **No empty headings** — a blank `##` or a `Heading 2` paragraph with no text
  is silently ignored by the parser; the content that follows it will be
  attached to whichever section was open before it.  Always give headings text.
- [ ] **Consistent heading style in DOCX** — all headings must use the built-in
  `Heading N` paragraph styles, not manual bold/font-size formatting.  A paragraph
  that *looks* like a heading but uses `Normal` style is treated as body text.
- [ ] **No duplicate heading paths** — two sections with identical titles at the
  same depth (e.g. two `## Introduction` sections) will have the same `node_id`
  and raise `duplicate node_id`.  Make titles unique within a document.
- [ ] **Tables have at least one header row** — a table with no rows produces an
  empty `table_columns` list, which makes the table node unsearchable by column
  name.

---

## Common errors and how to fix them

### `TreeValidationError: orphan node '...' at depth N has no ancestor at depth N-1`

**Cause:** A heading level was skipped.

**How to find it:**
- In Markdown: search for a `###` that does not follow a `##` earlier in the file.
- In DOCX: open the Navigation Pane (View → Navigation Pane) and look for
  indentation gaps in the heading outline.

**Fix:** Insert the missing intermediate heading, or promote the orphan heading
to the correct level.

---

### `TreeValidationError: duplicate node_id: '...'`

**Cause:** Two headings at the same depth have identical text, producing the
same `node_id` (`{doc_slug}::{heading_path}`).

**Fix:** Make heading titles unique within the document.  Adding a section number
prefix (e.g. `## 3 Baggage` vs `## 4 Baggage`) is the safest approach because
it also helps the retriever's section-number exact-match strategy.

---

### `TreeValidationError: multiple root nodes found`

**Cause:** The document contains more than one H1 / `Heading 1`.

**Fix:** Use exactly one H1 as the document title.  Demote any other H1s to H2
or lower.

---

### `TreeValidationError: no root node found`

**Cause:** The document has no H1 / `Heading 1`, or the document is empty.

**Fix:** Add a single H1 at the top of the document.

---

### Section content silently missing from the tree (no error raised)

**Cause:** A DOCX heading uses manual formatting (bold, font size) instead of
the built-in `Heading N` style.  The parser cannot detect it as a heading.

**How to confirm:** Open the document in Word, click the paragraph in question,
and check the style selector in the ribbon.  It should read `Heading 2` (or similar),
not `Normal` or a custom style name.

**Fix:** Apply the correct `Heading N` built-in style to the paragraph.

---

### `table_metadata.table_columns` is empty

**Cause:** The table has no rows, or the first row contains only empty cells.

**Fix:** Ensure the table's first row is a proper header row with non-empty cell text.

---

## Clause ID annotation (Markdown only)

If your policy documents reference clauses by ID (e.g. `CL-031`), you can embed
the ID as an HTML comment on the line immediately after the heading.  The parser
extracts it into the `clause_id` field, enabling exact-match retrieval that bypasses
any keyword or BM25 scoring:

```markdown
## 4 Baggage Allowance
<!-- clause: CL-031 -->

Content of the section.
```

The comment line is consumed by the parser and does not appear in `content`.

---

## Document types suited to this agent

The Document Tree agent is the right shape for documents that have **explicit
structural hierarchy** — where sections, clauses, or appendices carry different
authority levels or applicability rules.  Good examples:

- Corporate travel and expense policies
- Employment handbooks
- Regulatory filings (10-K, prospectus)
- Insurance policy documents
- Vendor contracts and SLAs
- Compliance frameworks (ISO, SOC 2 control descriptions)

It is the **wrong shape** for:

| Document type | Better shape |
|---------------|--------------|
| Free-form emails, chat transcripts, notes | Vector retrieval |
| ERP exports, financial tables, CRM records | Semantic Table |
| Org charts, fraud patterns, supply chain data | Knowledge Graph |
| Scanned PDFs with no programmatic structure | PDF parser (E5-S4, P2) — use LlamaParse fallback |

---

## Running the ingest script

Once your document meets the checklist above:

```bash
python scripts/ingest_tree.py \
  --input  data/raw/policies/ \
  --output data/processed/tree_json/ \
  --format auto          # detects .md / .docx by extension
```

If `TreeValidationError` is raised, the script prints the full error message
(including the offending `node_id` and the document slug) and exits with a
non-zero status.  Fix the document and re-run — the script is idempotent.

---

*Part of the ASP Document Tree Retrieval agent (E5).  See also: `src/asp/retrieval/document_tree/`.*
