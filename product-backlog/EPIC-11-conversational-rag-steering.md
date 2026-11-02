# EPIC-11: Conversational RAG & Interactive Category Steering

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-11-01 | Subfolder Category Discovery & Isolated Ingestion (`asp-rag-api.py`) | P1 — High | ✅ Done | 4 |
| US-11-02 | Category-Filtered RAG Retrieval (`asp-rag-api.py`) | P1 — High | ✅ Done | 4 |
| US-11-03 | Client RAG Bridge Category Integration (`RagBridgeService.cs`) | P1 — High | ✅ Done | 3 |
| US-11-04 | Category Pill Bar & Instant Redirection UI (`OverlayWindow.xaml`) | P0 — MVP | ✅ Done | 5 |
| US-11-05 | 4 Named Response Modes (`Default`, `RAG Only`, `Detailed`, `Coding`) | P0 — MVP | ✅ Done | 4 |
| US-11-06 | Dual-Track Detailed Mode (`RAG Docs` + `Theory & Best Practices`) | P1 — High | ✅ Done | 4 |
| US-11-07 | Universal Domain Steering Fallback (Zero-Doc Categories) | P1 — High | ✅ Done | 3 |
| US-11-08 | Clean Conversational Q&A Turn History & Whisper Hallucination Filter | P0 — MVP | ✅ Done | 5 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

In real-time technical interviews and meetings, naive single-turn RAG fails when follow-up questions rely on conversation context (e.g., *"Have you worked on Databricks?"* followed by *"Tell me more about these projects"*). When documents across diverse domains (such as personal resumes and large technical project specifications) are indexed into a single flat namespace, corpus imbalance causes large technical projects to overwhelm smaller personal documents.

EPIC-11 transforms Chum's document and response engine into an **Interactive Conversational RAG system**:
1. **Domain-Isolated Subfolders:** Subdirectories in `rag_docs/` (`resume/`, `asp/`, `databricks/`) define isolated document categories.
2. **Interactive Category Tags:** Clickable category pills in the overlay display all available categories. Clicking any tag immediately aborts an ongoing stream and re-directs the answer to that domain with sub-second latency.
3. **Universal Domain Steering:** If a category has no local files, the system steers the LLM's persona and knowledge base without failing.
4. **4 Explicit Named Modes:** Simplifies the cryptic numerical mode toggle into 4 self-explanatory modes: `Default`, `RAG Only`, `Detailed` (dual-track answer), and `Coding`.
5. **Clean Q&A Turn Buffer:** Separates ambient microphone silence hallucinations (*"Thank you"*, *"."*, *"I don't know"*) from real conversational Q&A history.

---

## Stories

### US-11-01: Subfolder Category Discovery & Isolated Ingestion
**Story Points: 4 · Priority: P1**  
**Component: `scripts/asp-rag-api.py`**

**As a** user organizing my knowledge vault,  
**I want** documents in `rag_docs/<category>/` subdirectories to be categorized automatically,  
**so that** diverse knowledge bases (e.g. Resume vs ASP Project) do not contaminate each other's search spaces.

**Acceptance Criteria:**
- [ ] Top-level subfolders in `rag_docs/` are automatically detected as categories (`resume`, `asp`, `databricks`). Files directly in `rag_docs/` are assigned to `general`.
- [ ] Document trees and vector chunks retain their category tag in metadata (`doc.category = "resume"`).
- [ ] New endpoint `GET /categories` returns a JSON list of available categories and document counts:
  ```json
  {
    "categories": ["resume", "asp", "databricks", "general"],
    "counts": { "resume": 1, "asp": 64, "databricks": 1, "general": 0 }
  }
  ```
- [ ] Ingestion logs display discovered categories and tree/vector counts per category.

---

### US-11-02: Category-Filtered RAG Retrieval
**Story Points: 4 · Priority: P1**  
**Component: `scripts/asp-rag-api.py`**

**As an** LLM query engine,  
**I want** the `/retrieve` endpoint to filter retrieval candidates to specific categories,  
**so that** queries only return relevant documents from the selected domain.

**Acceptance Criteria:**
- [ ] `RetrieveRequest` accepts optional `categories: list[str] = None`.
- [ ] When `categories` is specified (e.g. `["resume"]`), `TreeRetriever` only retrieves from trees belonging to `resume`.
- [ ] When `categories` is specified, `CSDR` vector search filters candidates to chunks matching the specified categories.
- [ ] If `categories` is omitted or contains `["*"]`, retrieval searches across all categories.
- [ ] If a requested category has 0 documents, endpoint returns `{"context": null, "sources": [], "strategy": "empty"}` with HTTP 200 without throwing exceptions.

---

### US-11-03: Client RAG Bridge Category Integration
**Story Points: 3 · Priority: P1**  
**Component: `Chum.App/Services/RagBridgeService.cs`**

**As the** Chum desktop client,  
**I want** `RagBridgeService` to query available categories and pass category filters to the ASP bridge,  
**so that** the desktop app can discover categories dynamically and route user queries.

**Acceptance Criteria:**
- [ ] `RagBridgeService` exposes `Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default)`.
- [ ] `RetrieveContextAsync` signature updated to accept `IReadOnlyList<string>? categories = null`.
- [ ] Results cached with 30-second TTL to avoid redundant network calls.
- [ ] Unit tests in `Chum.Tests` verify serialization and error handling when the bridge is offline.

---

### US-11-04: Category Pill Bar & Instant Redirection UI
**Story Points: 5 · Priority: P0**  
**Component: `Chum.App/Views/OverlayWindow.xaml`, `OverlayViewModel.cs`**

**As a** candidate in a live interview,  
**I want** clickable category pills at the top of the overlay that instantly re-route the LLM mid-response,  
**so that** I can redirect an off-track answer in under 500ms without touching my microphone.

**Acceptance Criteria:**
- [ ] Compact 22px Category Pill Bar added to Row 0.5 of `OverlayWindow.xaml`.
- [ ] Tags populate dynamically from `GetCategoriesAsync()` (e.g. `[● Resume]`, `[ASP]`, `[Databricks]`, `[General]`).
- [ ] Active category has distinct visual highlight (cyan border / glowing dot).
- [ ] Clicking any category pill:
  - If a response is actively streaming: cancels stream immediately via `_cts?.Cancel()`.
  - Sets clicked category as `ActiveCategory`.
  - Immediately re-dispatches the current question with context from the new category.
- [ ] Global keyboard shortcuts `Alt + 1` through `Alt + 9` switch active categories without mouse interaction.

---

### US-11-05: 4 Named Response Modes
**Story Points: 4 · Priority: P0**  
**Component: `OverlayViewModel.cs`, `MeetingOrchestrator.cs`, `OverlayWindow.xaml`**

**As a** user,  
**I want** 4 clearly named response modes (`Default`, `RAG Only`, `Detailed`, `Coding`) instead of cryptic numbers,  
**so that** I always know the exact formatting and grounding constraints applied to the LLM answer.

**Acceptance Criteria:**
- [ ] `ResponseModeType` enum created: `Default = 1`, `RagOnly = 2`, `Detailed = 3`, `Coding = 4`.
- [ ] Mode button on overlay displays full name: `Default`, `RAG Only`, `Detailed`, `Coding`.
- [ ] Clicking the button cycles to the next mode; `Ctrl + M` cycles modes via keyboard.
- [ ] System prompt instructions updated per mode:
  - `Default`: Direct, conversational, 2–3 concise paragraphs.
  - `RagOnly`: Strictly grounded in provided documents; states *"Information not found in local documents"* if missing.
  - `Coding`: Code-first, minimal prose, syntax, edge cases, complexity.
  - `Detailed`: Dual-track output (handled in US-11-06).

---

### US-11-06: Dual-Track Detailed Mode
**Story Points: 4 · Priority: P1**  
**Component: `MeetingOrchestrator.cs`**

**As an** interview candidate,  
**I want** `Detailed` mode to output two distinct sections: one from my local project documents and one from broader engineering theory,  
**so that** I get both my project stats and the theoretical depth needed for follow-up questions.

**Acceptance Criteria:**
- [ ] When `ResponseMode == Detailed`, prompt forces two visually distinct sections:
  ```text
  ━━━ FROM LOCAL DOCS ━━━━━━━━━━━━━━━━━━━━━━━━
  [Specific metrics, team responsibilities, and architecture from documents]

  ━━━ EXTENDED ARCHITECTURE & BEST PRACTICES ━
  [Industry best practices, trade-offs, and deep theoretical concepts]
  ```
- [ ] If local docs exist, Section 1 synthesizes document facts, and Section 2 elaborates with external engineering depth.
- [ ] If no local docs exist, Section 1 gracefully states *"No local documents found for {Category}"* while Section 2 provides complete theoretical coverage.

---

### US-11-07: Universal Domain Steering Fallback
**Story Points: 3 · Priority: P1**  
**Component: `MeetingOrchestrator.cs`**

**As a** user selecting a category that has no local documents,  
**I want** the LLM to still answer using focused domain expertise,  
**so that** empty folders never cause errors, blank responses, or broken workflows.

**Acceptance Criteria:**
- [ ] When RAG returns 0 documents for a selected category, system prompt injects domain steering:
  `"Domain Focus: {Category}. (Local docs: None). Answer using authoritative technical depth on {Category}."`
- [ ] LLM answers naturally within that technical domain without hallucinating that local files were read.
- [ ] Overlay status briefly indicates `Asking {Model} ({Category} - Domain mode)…`.

---

### US-11-08: Clean Conversational Q&A Turn History & Whisper Noise Filter
**Story Points: 5 · Priority: P0**  
**Component: `Chum.App/Services/MeetingOrchestrator.cs`, `TranscriptBuffer.cs`**

**As a** user having a multi-turn conversation,  
**I want** the LLM to remember recent Q&A turns without being polluted by ambient microphone silence hallucinations,  
**so that** follow-up questions like *"Tell me more about these projects"* have perfect context.

**Acceptance Criteria:**
- [ ] `QaTurnHistory` buffer implemented, storing last 3 clean turns: `{ Question, Answer, Category, Mode, Timestamp }`.
- [ ] Only explicitly triggered queries and completed assistant answers enter `QaTurnHistory`.
- [ ] Whisper silence hallucinations (*"Thank you"*, *"."*, *"I don't know"*, audio $< 1.2\text{ s}$) filtered from entering `TranscriptBuffer` and conversation context.
- [ ] `HandleAudioQueryAsync` formats prompt using recent `QaTurnHistory` turns as multi-turn dialogue.
- [ ] Ambient meeting audio (`AudioSource.Loopback`) is clearly labeled as background transcript, never confused with candidate dialogue.
