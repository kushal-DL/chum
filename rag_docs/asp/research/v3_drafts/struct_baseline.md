### Structure-aware chunking baseline (EE-Struct)

Laxmikant's review made the point sharply: "ASP cannot rest on beating BM25 + flat hybrid alone. That's the easy baseline. The comparison that actually proves the thesis is ASP against a strong structure-aware-chunking baseline." This is the right question to ask. A sliding-window BM25+vector baseline is a strawman for any system that claims to exploit document structure — most production RAG stacks already chunk on headings. EE-Struct introduces a third backend, `flat_struct`, that uses ASP's own document-tree parser to produce section-level chunks with heading paths prepended, then runs the *same* hybrid retriever as plain flat. No tree traversal, no parent/sibling expansion, no authority-ordered assembly. Only the chunk boundaries and the heading-path prefix are inherited from ASP. If ASP's win disappears against this stronger baseline, the structural machinery is unjustified.

### Method

The structure-aware baseline pipeline lives in [flat_structured_rag.py](src/asp/benchmarks/pipelines/flat_structured_rag.py). For each document we run the same `_parse_to_tree` call ASP uses; every parsed node becomes one chunk whose text is `"{heading_path}\n{title}\n{content}"`. The chunks are then fed through the identical retrieval stack as `flat`: Chroma dense embeddings, BM25 lexical, reciprocal-rank fusion, and a cross-encoder reranker. The Chroma cache namespace is `{dataset}_struct` so the structure-aware index is built and stored separately from the sliding-window flat index. Prompts, LLM slots, evaluation harness, item ordering, and n=500 per cell are held constant across all three backends. The only thing that varies is what counts as a chunk.

### Results

CUAD (span extraction):

| Dataset | Backend     | Model    | EM%   | F1%   | avg_ctx_tok | avg_lat_ms | Verdict |
|---------|-------------|----------|-------|-------|-------------|------------|---------|
| CUAD    | ASP         | Nemotron | 73.6  | 77.5  | 240         | 1,820      | ✅      |
| CUAD    | ASP         | Mistral  | 72.2  | 77.0  | 240         | 2,140      | ✅      |
| CUAD    | flat        | Nemotron | 58.6  | 62.1  | 681         | 2,560      | —       |
| CUAD    | flat        | Mistral  | 57.6  | 62.2  | 681         | 2,910      | —       |
| CUAD    | flat_struct | Nemotron | 58.4  | 62.5  | 1594        | 3,980      | ❌      |
| CUAD    | flat_struct | Mistral  | 56.0  | 60.1  | 1595        | 4,310      | ❌      |

MAUD (multiple-choice classification):

| Dataset | Backend     | Model    | Acc%  | avg_ctx_tok | avg_lat_ms | Verdict |
|---------|-------------|----------|-------|-------------|------------|---------|
| MAUD    | ASP         | Nemotron | 29.8  | 497         | 2,210      | ❌      |
| MAUD    | ASP         | Mistral  | 27.6  | 497         | 2,480      | ❌      |
| MAUD    | flat        | Nemotron | 31.6  | 870         | 2,840      | —       |
| MAUD    | flat        | Mistral  | 32.0  | 870         | 3,120      | —       |
| MAUD    | flat_struct | Nemotron | 21.4  | 227         | 1,640      | ❌      |
| MAUD    | flat_struct | Mistral  | 23.6  | 244         | 1,790      | ❌      |

Verdict column is relative to plain `flat` as the comparator: ✅ = beats flat by >1pp on the primary metric, ⚠️ = within 1pp, ❌ = trails flat by >1pp.

### Reading the results

On CUAD, ASP beats both baselines by roughly +15pp EM (73.6 vs 58.6 vs 58.4 on Nemotron; the Mistral pair tracks within a point). It does so while consuming only ~35% of flat's average context (240 vs 681 tokens) and roughly 70% of flat's latency (1,820 vs 2,560 ms on Nemotron) — simultaneously more accurate *and* about 3× cheaper on retrieved-context cost. `flat_struct` and plain `flat` sit on top of each other to within 1–2pp on both EM and F1, even though `flat_struct`'s average context balloons to 1594 tokens versus flat's 681 — each parsed node becomes one chunk, and parsed nodes are not bounded to the 256-token tiktoken sliding window used by flat. The heading-path prefix gives essentially zero lift on CUAD despite more than doubling the retrieved-context budget. Whatever explains ASP's CUAD advantage, it is not heading-path enrichment.

On MAUD the comparison inverts. `flat_struct` *loses* to plain `flat` by −10.2pp on Nemotron (21.4 vs 31.6) and −8.4pp on Mistral (23.6 vs 32.0). It is worth being explicit that ASP also trails `flat` on MAUD, though by a much smaller margin (−1.8pp on Nemotron, 29.8 vs 31.6; −4.4pp on Mistral, 27.6 vs 32.0). MAUD's M&A agreements parse into many fine-grained clauses, and any retrieval strategy whose chunk boundaries are inherited from the parse tree pays a clause-fragmentation tax — `flat_struct` pays it most acutely because it has no expansion mechanism, ASP pays a smaller version of it because parent/sibling expansion can only widen a bundle so far when the underlying clauses are themselves the unit of meaning. For `flat_struct` specifically, the retriever pulls back tiny isolated clause fragments with no surrounding context (avg 227 tokens, versus flat's 870) — the same context-starvation failure mode that motivated EE4-X1 in §6.2, but worse, because `flat_struct` has no expansion mechanism to widen the bundle once a short chunk is selected.

The net read: heading-path chunking is not table stakes. On the corpus type ASP is built for — clause-heavy contracts where the right answer often lives in a neighbouring clause rather than the one that lexically matched — heading-path chunking actively underperforms plain flat, and even ASP's tree-traversal machinery cannot fully overcome clause fragmentation on MAUD-style multiple-choice classification. The load-bearing parts of ASP are tree traversal, parent/sibling expansion via `_expand_to_floor()` in [bundle_assembler.py](src/asp/benchmarks/retrieval/bundle_assembler.py), and authority-ordered bundle assembly. The heading-path prefix is a footnote, not the mechanism.

### Limitation

`flat_struct` as run here uses only `heading_path + title + content` as chunk text. A stronger structure-aware baseline would add the per-chunk LLM-generated "contextual retrieval" prefixes Anthropic published — paying a one-time index-time LLM cost to attach a short situating summary to each chunk. We expect that variant to close some of the CUAD gap, since the contextual prefix would compensate for the heading-path prefix's failure to disambiguate near-duplicate clauses. It would not, however, fix the MAUD inversion: over-fragmented chunk boundaries are a structural problem, and no amount of per-chunk enrichment can reassemble a clause with its neighbours at query time.