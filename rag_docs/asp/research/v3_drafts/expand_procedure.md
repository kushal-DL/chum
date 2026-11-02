### Triggering condition and BFS order

The expand procedure fires only when the assembled bundle is below a configured `min_context_tokens` floor after the initial top-k nodes have been extracted and added (default floor is 0, which disables expansion). The triggering check is a single comparison: `running_tokens < min_context_tokens`, evaluated after the top-k pass. When the floor is unmet, [`_expand_to_floor()`](src/asp/benchmarks/retrieval/bundle_assembler.py#L83-L149) seeds a BFS queue with the immediate parents of every already-included node, then walks the tree breadth-first — dequeueing a candidate, attempting to add it under the token budget, and then enqueuing that candidate's children. The BFS climbs exactly one level upward (to the parents of the original includes) and from there walks strictly *downward* through siblings and their descendants; it never re-ascends to a grandparent.

```text
function expand_to_floor(included, running_tokens, nodes_dict,
                         q_tokens, token_budget, min_context_tokens):

    if running_tokens >= min_context_tokens or nodes_dict is empty:
        return (included, running_tokens)        # no-op, floor already met

    seen_ids := { n.node_id for (n, _) in included }
    queue    := []

    # Seed: immediate parents of every already-included node
    for (node, _) in included:
        pid := node.parent_node_id
        if pid exists and pid not in seen_ids and pid in nodes_dict:
            queue.append(nodes_dict[pid])

    visited_parents := set()

    while queue is not empty and running_tokens < min_context_tokens:
        candidate := queue.pop_front()
        if candidate is None or candidate.node_id in seen_ids:
            continue

        # Reduce candidate to query-relevant paragraphs (may return full node text on weak overlap)
        extracted := extract_relevant_paragraphs(candidate, q_tokens)
        cost      := count_tokens(extracted) + HEADER_TOKEN_OVERHEAD  # +100

        # Hard budget check — never split, never truncate
        if running_tokens + cost <= token_budget:
            included.append((candidate, extracted))
            running_tokens += cost
            seen_ids.add(candidate.node_id)
        # else: skip this node but continue the BFS

        # Enqueue this candidate's own children (siblings of the original include)
        if candidate.node_id not in visited_parents:
            visited_parents.add(candidate.node_id)
            for cid in candidate.child_node_ids:
                if cid not in seen_ids and cid in nodes_dict:
                    queue.append(nodes_dict[cid])

    return (included, running_tokens)
```

### Deep-tree behaviour

Consider a 200-page agreement parsed to six nesting levels (`Agreement > Article > Section > Subsection > Clause > Sub-clause`), where the top-k retrieval surfaced one Sub-clause node at depth 6. The seed step enqueues the immediate parent — the Clause at depth 5 — and nothing else. The grandparent (Subsection at depth 4) is **not** touched at this point, and under the current implementation it never will be.

When the BFS dequeues the Clause, it does two things in order: (a) it attempts to add the Clause itself under the budget, and (b) it enqueues the Clause's *children* (the Sub-clause siblings of the original include). It does **not** enqueue the Clause's own parent. As the BFS proceeds it only ever enqueues a candidate's `child_node_ids` (see the loop body at line 145); `parent_node_id` is consulted exactly once, during the seed step, and only on the *original* `included` list — not on nodes appended during expansion. The consequence is that the expand BFS climbs exactly one level upward (to the parents of the original includes) and from there walks strictly downward through siblings and their subtrees. Grandparents and higher ancestors are unreachable, irrespective of how short the bundle stays or how long the queue runs.

This is a known limitation. Reaching multi-level ancestors would require either (a) re-seeding the parents of expansion-added nodes back into the queue, or (b) enqueuing `candidate.parent_node_id` (gated by a `visited_parents` check) alongside `candidate.child_node_ids` during expansion. Neither is done today.

Across the CUAD, MAUD, Travel Policy and JCP Credit corpora tested, the floor (~700 tokens) is satisfied within one BFS level — the parent plus a handful of siblings — so the absence of a multi-level climb has not been a practical limit on real runs.

### Token-budget conflicts

Suppose a single parent section weighs 5,000 tokens and `token_budget = 5,000` is already partially consumed (say `running_tokens = 800` from the original top-k include). The expand step does **not** see the 5,000-token raw section: [`paragraph_extractor`](src/asp/benchmarks/retrieval/paragraph_extractor.py) is run on the candidate first and typically returns only the query-relevant paragraphs — empirically a few hundred tokens, occasionally a thousand, and occasionally the full node text when query/node token overlap is weak or the node is small. The BFS cost is then `count_tokens(extracted) + 100` (the `_HEADER_TOKEN_OVERHEAD` constant covering the compact header, clause id, effective date, and authority hint).

The budget check is strictly `running_tokens + cost <= token_budget`: pass and the node is appended; fail and the node is **skipped silently**, but the BFS continues to the next queued candidate rather than aborting. The assembler never splits a node — inclusion is whole-or-nothing per node, with paragraph_extractor providing the only form of sub-node selection. If every remaining candidate fails the budget check, the bundle finishes below the floor with no truncation and no partial inclusion.

Note that `compression_applied` is **not** set in this case. The flag is only raised during the initial top-k loop (line 206) when a top-k node is dropped by the budget; `_expand_to_floor()` never touches it. An under-floor bundle therefore carries no distinguishing flag in the returned tuple — downstream code that needs to identify under-floor bundles must compare `running_tokens` to `min_context_tokens` itself. (Adding an explicit `floor_unmet` flag would be a small follow-up fix, but is out of scope here.)

The fact that EE4-X1 triggers on bundle **size** rather than on retrieval **ambiguity** — and therefore over-expands on CUAD where the top-1 is already correct but small — is the limitation addressed by EE4-X2 (sibling-score-variance gate) in §7.