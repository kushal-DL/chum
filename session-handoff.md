# Chum — Session Handoff

> **Read this at the start of every Claude session before doing anything else.**  
> Update this file at the end of each session (or whenever meaningful progress is made).

---

## App Intent & Vision

**Chum** is a real-time AI co-pilot for professionals in video calls. Think of it as OBS Studio meets Copilot — it runs silently in the background during Teams/Google Meet calls and provides LLM-powered assistance on demand.

### Core User Flows

**Flow 1 — Audio Query (Hold-to-Ask)**
User holds `Ctrl+Alt+Space` while a question is being asked in the meeting. Chum marks the audio window, transcribes the conversation, and fires a query to Claude/GPT. The AI response streams into a floating transparent overlay that only the user sees. User reads the answer; releases the hotkey and continues the meeting naturally.

**Flow 2 — Visual Query (Screen Capture)**
User presses `Ctrl+Alt+S`. Chum captures the screen (or clipboard/dropped image) and sends it to a multimodal LLM along with recent transcript context. Useful for whiteboards, shared slides, diagrams in screen-share.

**Flow 3 — Action Items**
User presses `Ctrl+Alt+A` near meeting end. Chum sends the full session transcript to the LLM with "extract action items and owners" prompt. Response displays in overlay; user copies to clipboard.

### What Makes This Hard (Key Constraints)
- **Teams DRM** — `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` blocks screen capture of Teams call window. Workaround: layered strategy (WGC for non-Teams content, clipboard, image drop, UIA text). Full details: `product-backlog/EPIC-06-screen-capture.md`
- **Privacy** — App listens to meetings. Raw audio never persists; transcript in-memory only by default. Local-only mode (Whisper + Ollama) keeps everything on-device.
- **Latency** — Whisper STT on CPU ~5s lag. Mitigated by VAD-gated chunking and GPU acceleration.

---

## Technology Decisions Made

| Decision | Choice | Reason |
|----------|--------|--------|
| Language/Platform | C# .NET 8 + WPF | Best Windows native API access; NAudio ecosystem; WPF transparent windows |
| Audio capture | NAudio WASAPI | Loopback + mic in shared mode; device change events |
| VAD | Silero VAD (ONNX) | Local, fast (~1ms), MIT license |
| STT | Whisper.NET (whisper.cpp) | Local by default; no cost; GPU-acceleratable |
| LLM primary | Anthropic Claude Haiku 4.5 (default), Sonnet 4.6 (quality) | Long context; fast; pluggable via `ILlmProvider` |
| LLM vision | Claude Sonnet 4.6 or GPT-4o | Both support multimodal |
| Screen capture | Windows.Graphics.Capture API | Most compatible; graceful Teams blackout |
| Global hotkeys | Win32 LowLevel Keyboard Hook | Reliable across all apps; full combo control |
| Secure storage | Windows Credential Manager (DPAPI) | OS-managed; never keys in files |

---

## Current Status

**Date of last update:** 2026-06-27  
**Phase:** Pre-development — Product backlog complete

### What Was Done This Session (2026-06-27)

This was the **inaugural session**. No code has been written yet. The following documentation was created from scratch:

1. **Created `/product-backlog/` folder** with complete product backlog:
   - `README.md` — App name suggestions, vision, tech stack, architecture diagram, epic overview
   - `EPIC-01-audio-engine.md` through `EPIC-10-performance.md` — 10 detailed epic files with full user stories, acceptance criteria, technical implementation notes, known challenges, and workarounds
   - `BACKLOG-STATUS.md` — Master status tracker for all 75 stories (310 total story points)

2. **Story priorities assigned** (P0–P3) across all 75 stories in all epic files and BACKLOG-STATUS.md

3. **Created `CLAUDE.md`** — Project guide for Claude: tech stack, session protocol, backlog update rules, code conventions, MVP build order

4. **Created `session-handoff.md`** (this file)

5. **Pushed to GitHub** — `https://github.com/kushal-DL/chum` (branch: `main`)

### What Has NOT Been Done
- No `.sln` or `.csproj` files created yet
- No source code written
- No NuGet packages configured
- No CI/CD pipeline set up

---

## Next Session — What To Do First

### Immediate Priority: Scaffold the Solution

Build in this order (all P0 MVP stories):

**Step 1 — Solution scaffold**
```
src/
├── Chum.sln
├── Chum.App/           WPF application, entry point
├── Chum.Audio/         Class library — WASAPI capture + VAD
├── Chum.Transcription/ Class library — Whisper STT + transcript buffer
├── Chum.Llm/           Class library — ILlmProvider + Claude + OpenAI
└── Chum.Tests/         xUnit test project
```
→ Updates: US-07-01 (scaffold settings), US-01-01, US-01-02, US-01-05 to 🟡 Scaffolded

**Step 2 — Settings & API key management (US-07-01)**
- `SettingsService` backed by `settings.json` in `%APPDATA%\Chum\`
- `CredentialService` wrapping Windows Credential Manager
- Settings panel window (basic — just API key entry to start)
→ Updates: US-07-01 to 🔵 Built

**Step 3 — Audio capture (US-01-01, US-01-02)**
- `WasapiLoopbackCapture` + `WasapiCapture`
- Resample to 16kHz/16-bit mono
- `Channel<AudioChunk>` ring buffer
→ Updates: US-01-01, US-01-02, US-01-05 to 🔵 Built

**Step 4 — VAD (US-01-04)**
- Silero VAD ONNX model integration
- Gated segment extraction from audio buffer
→ Updates: US-01-04 to 🔵 Built

**Step 5 — Whisper STT (US-02-01, US-02-03)**
- Whisper.NET integration; `small` model
- `TranscriptBuffer` with rolling 10-min window
→ Updates: US-02-01, US-02-03 to 🔵 Built

**Step 6 — LLM integration (US-03-01, US-03-04, US-03-05)**
- `ILlmProvider` interface
- `AnthropicLlmProvider` with streaming
- Meeting system prompt
→ Updates: US-03-01, US-03-04, US-03-05 to 🔵 Built

**Step 7 — Hotkey (US-04-01)**
- LowLevel keyboard hook on STA thread
- Hold-start / hold-end events
→ Updates: US-04-01 to 🔵 Built

**Step 8 — Overlay UI (US-05-01, US-05-02)**
- WPF transparent `Topmost` window
- Response `FlowDocument` panel with streaming append
- Wire hotkey → LLM → overlay
→ Updates: US-05-01, US-05-02 to 🔵 Built

At the end of Step 8, the core MVP loop is functional: hold hotkey → transcript context → LLM → streamed response in overlay.

---

## Known Decisions Deferred

| Decision | Why Deferred | When To Revisit |
|----------|-------------|-----------------|
| Screen capture / vision (EPIC-06) | Teams DRM complexity; not in MVP | After core audio loop works (v0.2) |
| Multi-platform (macOS) | WPF is Windows-only; Tauri would be needed | After v1.0 ships on Windows |
| Speaker diarization (multiple remote voices) | Requires pyannote.audio or NeMo — complex | v0.3 |
| Auto-update mechanism | Out of scope until distributable build exists | After first public release |
| Crash reporting (Sentry) | Opt-in feature; not needed for personal use | v0.5 |

---

## Open Questions (Resolve in Next Session)

1. **Project layout preference** — Monorepo with `src/` subfolder, or source at repo root?
2. **Whisper model hosting** — Download from GitHub Releases or Hugging Face on first run?
3. **Default LLM model** — Claude Haiku 4.5 (fast/cheap) or Sonnet 4.6 (better quality) as the default?
4. **Overlay position default** — Bottom-right corner, or centered-top?
5. **Testing approach** — Unit tests for domain logic only, or integration tests with virtual audio devices?

---

## Context for Next Claude Session

When you start a new session on this project:

1. The entire product backlog is in `product-backlog/` — read `README.md` for overview
2. All 75 stories have priorities and statuses in `product-backlog/BACKLOG-STATUS.md`
3. The app does not have any code yet — you're starting from scratch
4. Read `CLAUDE.md` for code conventions, the MVP build order, and the backlog update protocol
5. The user (Kushal) is building this for personal use in corporate meetings — the primary meeting app is Microsoft Teams

**Kushal's context:** Corporate environment, Windows 11, likely has Teams + Google Meet. Building this as a personal productivity tool. Repo is at `https://github.com/kushal-DL/chum`.

---

*Last updated: 2026-06-27 by Claude (inaugural session — backlog creation and GitHub push)*
