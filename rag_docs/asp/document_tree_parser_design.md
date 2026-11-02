# Document Tree Parser — Universal Marker-Grammar Design

**Status:** Design accepted 2026-06-19. Implementation tracked under E5-S19 → E5-S25.
**Author:** Kushal Sharma
**Trigger:** MAUD-500 benchmark regression — every MAUD excerpt fell through `MarkdownParser`
to a 1–2-node fallback bag, neutralising tree traversal, authority resolution, and bundle
compression. ASP ended up using **half** the tokens of the flat baseline (vs the travel
demo's 2.6× *more*) because the protocol's value props became no-ops on unstructured legal
prose. Full diagnosis: [docs/asp_implementation_comparison.md](asp_implementation_comparison.md).

---

## Problem statement

`MarkdownParser` only recognises `#`/`##`/`###` heading markers. Real enterprise documents
use a zoo of numbering conventions, none of which look like markdown:

| Style | Example markers |
|---|---|
| Markdown | `# H1`, `## H2`, `### H3` |
| Contract / legalese | `Article III`, `Section 2.1(b)`, `(a)`, `(i)`, `(A)` |
| SEC filings | `ITEM 7A.`, `PART II`, `Note 5 — Income Taxes` |
| Regulatory / RFC / ISO | `1.`, `1.1.`, `1.1.1.`, `4.2.3.1` |
| US Code / statutes | `§ 101.`, `(a)`, `(1)`, `(A)`, `(i)`, `(I)` |
| Recital prose | `WHEREAS, …; WHEREAS, …; NOW, THEREFORE, …` |
| Bullets (mixed) | `*`, `-`, `•`, `▪`, `‣` |
| Continental | `1°`, `2°` ; `Art. 1er`, `Art. 2`, `§ 1` |
| Indentation only | tabs / leading spaces, no markers |

A per-format parser library is one option. The **universal marker-grammar engine** described
here is the chosen path because every style above is just a specific ordering of the same
finite set of marker classes, and a single parser maintained against a single regex library
scales better than N drift-prone format-specific parsers.

---

## Design

### 1. Marker library

A marker is a `(pattern, style_class)` pair. The library lives at
`src/asp/retrieval/document_tree/parsers/marker_library.py` and ships ~25 classes covering
every style in the table above:

```python
@dataclass(frozen=True)
class Marker:
    style_class: str          # e.g. "decimal-numbered", "lower-paren-alpha"
    pattern: re.Pattern       # anchored at line start
    extract: Callable[[Match], MarkerToken]   # produces section_number, label, etc.
    is_recital: bool = False  # WHEREAS-style preamble blocks
    is_bullet: bool = False   # `*`, `-`, `•`
```

Indicative classes (this list is illustrative; the real library is in code):

```
markdown-h1, markdown-h2, markdown-h3, markdown-h4
decimal-numbered            # 1., 1.1., 1.1.1.
upper-roman                 # I., II., III.
lower-roman                 # i., ii., iii.
upper-paren-alpha           # (A), (B)
lower-paren-alpha           # (a), (b)
upper-paren-numeric         # (1), (2)
section-symbol              # § 101., § 1
article-word                # Article III, Article 5, Art. 1
section-word                # Section 2.1, Section 4(b)
sec-item-marker             # ITEM 1., ITEM 7A. (SEC filings)
sec-part-marker             # PART I, PART II
note-word                   # Note 5 —, Note 12.
whereas-recital             # WHEREAS, …
now-therefore-recital       # NOW, THEREFORE, …
bullet-dash, bullet-star, bullet-bullet  # -, *, •
continental-degree          # 1°, 2°
```

### 2. Single-pass tokeniser

Walk the document line by line. For each line:

1. Try every marker pattern in priority order (longer / more specific first).
2. Emit a `MarkerToken(style_class, label, section_number, raw_line, line_no)` on hit.
3. Lines with no marker are buffered as content for whichever node is currently open.

Output: an ordered list of `MarkerToken | ContentLine` events.

### 3. Stack-based assembler — *learn the hierarchy from the data*

This is the heart of the design and what makes it style-agnostic. The assembler maintains a
stack `[(style_class_1, depth_1), (style_class_2, depth_2), …]` where the order in which
style classes were *first observed* defines the document's nesting.

For each marker token:

```
if style_class is new:
    push (style_class, len(stack))   # next available depth
    open node at this depth

elif style_class matches the top of the stack:
    close current node, open sibling at same depth

elif style_class is already on the stack (deeper):
    pop entries above it; close those nodes; open sibling at that depth

elif style_class is unknown but indents under the current style (heuristic on
     leading whitespace and content density):
    push (style_class, top.depth + 1)   # treat as new deeper level
```

After the document is consumed, every `MarkerToken` is mapped to a `TreeNode` with `depth`,
`parent_node_id`, `child_node_ids`, `section_number`, `clause_id`, and `heading_path` filled
from the stack state at emission time.

This is how Akoma Ntoso / LegalRuleML / OLRC parsers handle US Code, and the same algorithm
covers RFCs, ISO docs, contracts, regulations, and markdown without a special case per
domain.

### 4. Learned style hierarchy as ASP metadata

The order in which style classes were observed is itself useful information: it tells
downstream consumers how this specific document is organised. Persist it on `DocumentTree`:

```python
class DocumentTree(BaseModel):
    # ... existing fields ...
    style_hierarchy: List[str] = Field(default_factory=list)
    # e.g. ["article-word", "section-word", "lower-paren-alpha", "lower-roman"]
    # for a contract; ["decimal-numbered"] for an RFC.
```

The bundle assembler can surface this in its compact header so the LLM knows whether
"§3.1" means "Article > Section" (depth 2) or "Section > sub-section" (depth 1). Cheap
context that prevents one specific class of citation hallucination.

### 5. List-vs-structure heuristic

Bulleted/numbered lists are sometimes structural (an enumerated list of *covenants*,
*exclusions*, *conditions*) and sometimes pure formatting (a sentence broken into bullets
for readability). The assembler promotes a list to TreeNodes only when **both**:

- The list has **≥3 items**, AND
- The parent node's content (excluding the list) is **non-trivial** (≥80 tokens of prose
  outside the list, or the parent is otherwise empty).

Otherwise the list stays inline in the parent's content. This is heuristic, not a rule —
the threshold is exposed in `configs/document_tree.yaml` and tuned against the benchmark
corpus.

### 6. Style sniffer + dispatch

Single sniffer at `src/asp/retrieval/document_tree/parsers/dispatcher.py`:

```
1. Score the first ~200 lines against the marker library — count hits per style_class.
2. If markdown-h{1,2,3} dominates → use the existing MarkdownParser (fast path).
3. Otherwise → use the universal marker-grammar parser.
4. If both yield <3 nodes for a >1500-token document → flag for the LLM-assisted
   fallback path (E5-S24, off by default).
```

The sniffer is a fast pre-filter, not a parser-per-domain switchboard. It exists only so
markdown documents continue to use the fast deterministic markdown path instead of
re-running through the more expensive marker-grammar pipeline.

### 7. Ingestion-profile overrides (E5-S15 + E5-S23)

The existing `DocumentProfile` enum (E5-S15) gains a thin override layer that:

- Forces certain `node_type` tagging (`financial_filing` → all numeric tables become
  `node_type=table` regardless of marker grammar).
- Adjusts the list-vs-structure threshold (`insurance_policy` keeps lists structural at
  ≥2 items because exceptions matter).
- Pre-loads marker-pattern extensions specific to that domain (e.g. `note-word` priority
  bump for SEC filings).

Profiles are config-driven; no per-domain Python parsers.

---

## Alternatives considered

### Option A — Style-specific parser library (rejected)

One parser file per format (`legal_clause_parser.py`, `regulatory_parser.py`,
`sec_filing_parser.py`, `recital_parser.py`) plus a sniffer. Deterministic and easy to
debug, but every new document family is a new parser file; behaviour drifts; you maintain
N parsers. **Rejected** because the marker classes overlap so heavily across "format
families" that the parser bodies would be 80% duplicate code anyway.

### Option C — Indentation + visual-cue ML hybrid (rejected)

Featurise each line — leading whitespace, ALL CAPS ratio, bold/italic markers, marker
regex match, line length, blank-line neighbourhood — and learn a depth classifier
(gradient-boosted tree, ~5K labelled lines). Same pipeline as B but learned, not rule-based.

**Pros:** handles "indentation only, no markers" docs (poorly-OCR'd PDFs) which the
marker-grammar engine cannot.

**Rejected** because it conflicts with the "deterministic / reproducible" constraint
in CLAUDE.md, requires a labelled corpus that doesn't exist, and reviewers will ask
why a depth is what it is — hard to answer for a classifier.

### Option D — LLM-assisted parsing (kept as alternative; tracked under E5-S24)

If the deterministic marker-grammar parser returns < 3 nodes for a document with > 1500
tokens, send a heading-extraction prompt to a small LLM and use the response to wire the
tree. Off by default; gated by `configs/document_tree.yaml`. Available as a research-mode
fallback because the very edge cases that break Option B (truly unstructured prose, OCR
noise, extreme stylistic mixing) are exactly where an LLM's pattern-matching is strongest.

**Note from author (Kushal):** "Things like the list-vs-structure heuristic make me
genuinely think Option D might be better long-term. The grammar engine is the
deterministic baseline; D is the path to chase if heuristic tuning becomes a treadmill."
Documenting D as a first-class alternative — not just a fallback — so the team knows it's
on the table for a future iteration once we have the marker-grammar baseline working.

---

## Test corpus

Implementation must pass all of:

- [ ] 20 Markdown policy docs from `example-workflow/data/raw/` (regression — must produce
      identical trees to the existing `MarkdownParser`)
- [ ] 50 random MAUD contract excerpts (target: ≥10 nodes per excerpt, depth ≥3)
- [ ] 30 random CUAD contracts (target: ≥30 nodes per contract, table nodes preserved)
- [ ] 20 SEC 10-K extracts (target: `ITEM`/`PART`/`Note` correctly nested)
- [ ] 10 RFCs (target: decimal numbering correctly nested 4 levels deep)
- [ ] 5 EU regulations / ISO standards (target: continental numbering handled)

For each corpus the harness reports: `total_documents`, `mean_nodes_per_doc`,
`mean_depth_per_doc`, `fallback_rate` (% of docs that fell through to the LLM-assisted
path).

---

## Acceptance gate

The marker-grammar parser ships when, on the same MAUD-500 benchmark that triggered this
work:

| Metric | Today (broken) | Target |
|---|---|---|
| Mean nodes per excerpt | 1.91 | ≥ 5 |
| `compression_applied` rate | 0/500 | ≥ 50/500 |
| ASP accuracy | 43.2% | ≥ flat hybrid |
| Wrongness disagreements (flat-only correct) | 80 | ≤ 30 |

If the parser ships and ASP still loses to flat hybrid on MAUD by more than the noise
floor, the diagnosis is **not** the parser — it's a paragraph-extractor or retrieval-
ranking issue, which is a separate fix.
