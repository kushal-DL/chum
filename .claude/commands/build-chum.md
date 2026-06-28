# /build-chum

Resume Chum development from where the last session ended. Follows a strict read-before-build
protocol to avoid duplicating work, drifting from the repo structure, or misrepresenting
backlog status.

---

## Phase 1 — Orient (read everything before touching code)

Run these reads **in parallel** as your very first action:

1. Read `session-handoff.md` — where we stopped, what's next, any blockers or decisions made.
2. Read `product-backlog/BACKLOG-STATUS.md` — current status of all stories.
3. Read `REPO_STRUCTURE.md` — directory layout, naming conventions, interface rules.
4. Read `CLAUDE.md` — session protocol, code conventions, MVP build order.

Do not write a single line of code until all four are read and you have identified the next story.

---

## Phase 2 — Validate backlog accuracy

Before picking a story to build, verify the backlog reflects reality:

- For every story marked 🔵 Built: spot-check that its primary source file(s) actually exist
  (use Glob or Grep — do not assume the file exists just because the status says so).
- If a story is marked 🔴 Yet to Start but you find partial code for it: update its status to
  🟡 Scaffolded before proceeding.
- If a story is marked 🟡 Scaffolded but the core logic is complete: update to 🔵 Built.
- Fix any discrepancies in `BACKLOG-STATUS.md` before writing any new code.

Report what you found: "Stories X, Y were mislabelled — corrected. Proceeding to Z."

---

## Phase 3 — Pick the next story

Select the single next story to build using this priority order:

1. Any P0 story still at 🔴 Yet to Start or 🟡 Scaffolded, in the MVP build order from CLAUDE.md.
2. If all P0 stories are at least 🔵 Built: next P1 story in the same epic as the last completed P0.
3. Never work on a P2/P3 story unless every P0 and P1 story in its epic is Built.

**Build one story at a time.** If the story is large (>5 SP), split it into a logical first increment
and state clearly what you are building and what you are deferring to the next call.

---

## Phase 4 — Check structure before creating files

Before writing any new `.cs`, `.xaml`, or `.csproj` file:

1. Re-read the relevant section of `REPO_STRUCTURE.md` for the project you are working in.
2. Confirm the new file's location matches the "Where New Files Go" table.
3. Confirm the class/interface name matches the "Naming Conventions" table.
4. If the new file belongs in a directory not yet listed in `REPO_STRUCTURE.md`, add it to the
   file — but only if it represents a new structural pattern, not just a new class.

---

## Phase 5 — Build

Write the code for the chosen story. Enforce these non-negotiable principles:

**Architecture**
- Every external call goes through an interface (`ILlmProvider`, `IAudioCapture`, etc.).
- Never reference a concrete implementation from `Chum.App` directly — only via interfaces or
  the service classes in `Chum.App/Services/`.
- Dependencies flow strictly downward: Audio ← Transcription ← Llm ← App. No upward refs.

**Async / Threading**
- Never `.Wait()`, `.Result`, or `Task.Run(() => asyncMethod().Result)`.
- Audio capture threads write to `Channel<T>` — never call UI or STT directly from capture events.
- All UI mutations go through `OverlayViewModel` methods, which marshal via `Dispatcher.InvokeAsync`.

**Error handling**
- Catch specific exceptions; never bare `catch (Exception)` without a `Serilog.Log.Error` call.
- Audio/STT errors: log and continue the pipeline loop — never crash the capture thread.
- LLM errors: surface as a user-visible message in the overlay via `_overlay.ShowError(...)`.

**Security**
- API keys: `CredentialService` only — never in code, config JSON, logs, or error messages.
- Audio buffers: `Array.Clear` after transcription consumes them.
- Screen captures: memory-only pipeline — never write to disk.

**Size and scope**
- Do not add error handling, fallbacks, or abstractions for scenarios that cannot happen.
- Do not refactor surrounding code unless it directly blocks the story you are building.
- Do not add comments explaining what the code does — only add a comment when the WHY is
  non-obvious (hidden constraint, subtle invariant, specific bug workaround).

---

## Phase 6 — Update docs (mandatory — hook will block you if skipped)

After the code is written, **before finishing your turn**:

1. **`product-backlog/BACKLOG-STATUS.md`** — update the story status:
   - Core logic written, compiles (expected) → 🔵 Built
   - Only structure/scaffold created → 🟡 Scaffolded
   - Update the epic-level SP totals at the bottom of the story's epic section.
   - Update the "Overall Progress" table at the bottom of the file.

2. **`session-handoff.md`** — add to "What Was Done" for this session:
   - Which story was built, which files were created/modified.
   - Any decisions made, APIs discovered, or approaches chosen.
   - Any blockers encountered or open questions for the next session.
   - Update "Immediate Next Step" to point to the NEXT unbuilt story.

3. Also update the "Stories at a Glance" table in the relevant epic file
   (`product-backlog/EPIC-NN-*.md`).

The Stop hook (`check-handoff.ps1`) will block your turn from ending if code files changed
without the docs being updated. Do not try to work around it — just update the docs.

---

## Phase 7 — Commit

After docs are updated, commit all changes in a single commit:

```
git add <changed source files> product-backlog/BACKLOG-STATUS.md session-handoff.md
git commit -m "feat(<story-id>): <one-line description>"
git push origin main
```

Commit message format: `feat(US-NN-NN): brief description of what was built`

---

## What NOT to do

- Do not start multiple stories in one session — finish one completely before touching the next.
- Do not skip status transitions — always move through Scaffolded → Built → Done in order. Mark ✅ Done automatically when automated tests pass (do not wait for manual sign-off).
- Do not refactor, rename, or clean up code that is not part of the chosen story.
- Do not create files outside the structure defined in `REPO_STRUCTURE.md` without updating that file.
- Do not install new NuGet packages without checking if an existing package already covers the need.
