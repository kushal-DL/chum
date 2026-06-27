# CLAUDE.md — Chum Project Guide

## What Is Chum?

Chum is a real-time AI co-pilot for professionals in video calls. It runs on Windows, listens to meeting audio via WASAPI loopback + microphone capture, maintains a rolling transcript via local Whisper STT, and surfaces LLM-powered answers when the user holds a configurable hotkey. A second hotkey captures the screen (or reads clipboard/dropped images) and sends it to a multimodal LLM. The overlay is a transparent always-on-top WPF window that floats above the meeting app.

**Target platforms:** Windows 10 (1903+) / Windows 11  
**Primary language:** C# / .NET 8  
**Key constraint:** Microsoft Teams blocks screen capture with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` — workarounds are documented in `product-backlog/EPIC-06-screen-capture.md`.

---

## Repository Layout

```
chum/
├── CLAUDE.md                        ← You are here
├── session-handoff.md               ← Read this first every session
├── product-backlog/
│   ├── README.md                    ← Epics overview + tech stack
│   ├── BACKLOG-STATUS.md            ← AUTHORITATIVE status tracker (update this when writing code)
│   ├── EPIC-01-audio-engine.md
│   ├── EPIC-02-transcription.md
│   ├── EPIC-03-llm-integration.md
│   ├── EPIC-04-hotkeys.md
│   ├── EPIC-05-overlay-ui.md
│   ├── EPIC-06-screen-capture.md
│   ├── EPIC-07-settings.md
│   ├── EPIC-08-privacy-security.md
│   ├── EPIC-09-platform-compatibility.md
│   └── EPIC-10-performance.md
└── src/                             ← Application source (to be created)
    ├── Chum.sln
    ├── Chum.App/                    ← WPF application project
    ├── Chum.Audio/                  ← Audio capture + VAD
    ├── Chum.Transcription/          ← Whisper STT integration
    ├── Chum.Llm/                    ← LLM provider integrations
    └── Chum.Tests/                  ← Unit + integration tests
```

---

## Recommended Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | WPF (.NET 8) — transparent always-on-top window |
| Audio | NAudio — WASAPI loopback + mic capture |
| VAD | Silero VAD via ONNX Runtime |
| STT | Whisper.NET (whisper.cpp bindings) — local by default |
| LLM | Anthropic SDK (Claude) + OpenAI SDK — pluggable via `ILlmProvider` |
| Vision | Claude Sonnet 4.6 or GPT-4o for image analysis |
| Screen Capture | Windows.Graphics.Capture API |
| Hotkeys | Win32 LowLevel Keyboard Hook (`WH_KEYBOARD_LL`) |
| Secure Storage | Windows Credential Manager (`AdysTech.CredentialManager`) |
| Logging | Serilog + rolling file sink |

---

## Session Protocol — Read Every Time

**At the start of every session:**
1. Read `session-handoff.md` — it contains where we left off and what to do next
2. Check `product-backlog/BACKLOG-STATUS.md` for the current status of all stories

**While working:**
3. When you write or significantly modify code for a user story, update that story's status in `product-backlog/BACKLOG-STATUS.md`
4. Use these exact status values (emoji + text):
   - `🔴 Yet to Start` — not begun
   - `🟡 Scaffolded` — project/file structure created but logic incomplete
   - `🔵 Built` — code written and compiles, not yet tested end-to-end
   - `✅ Done (Built & Tested)` — code complete and verified working

**At the end of every session (or when user signals they're done):**
5. Update `session-handoff.md` — record what was done, what's next, any blockers or decisions made

---

## Backlog Status Update Rule

**This is a mandatory instruction.** Whenever you write or modify code files (`.cs`, `.xaml`, `.csproj`, `.json` config), you MUST:

1. Identify which story ID(s) the code belongs to (e.g., US-01-01)
2. Update the `Status` column for those stories in `product-backlog/BACKLOG-STATUS.md`
3. Also update the "Stories at a Glance" table inside the relevant epic file (e.g., `EPIC-01-audio-engine.md`)

**Status transition rules:**
- Creating project structure / empty classes → `🟡 Scaffolded`
- Implementing core logic (compiles, feature works in isolation) → `🔵 Built`
- After manual or automated testing confirms the feature works end-to-end → `✅ Done (Built & Tested)`
- Never skip from "Yet to Start" to "Done" — always pass through Scaffolded and Built

---

## Code Conventions

### Project Structure
- One .NET project per major layer (Audio, Transcription, Llm, App)
- `Chum.App` references all other projects
- All external API calls go through interfaces (`ILlmProvider`, `ISttProvider`, `IAudioCapture`) to allow mocking and swapping

### Naming
- Interfaces: `IXxx` (e.g., `ILlmProvider`)
- Records for data: `AudioChunk`, `TranscriptSegment`, `LlmRequest`
- Async methods: suffix `Async` (e.g., `StreamResponseAsync`)
- Hotkey IDs: match story ID format (e.g., `HotkeyId.HoldToAsk`)

### Threading Model
- UI thread: WPF Dispatcher only; never block it
- Audio capture: dedicated high-priority threads (one per stream)
- Transcription: `ThreadPool`; `BelowNormal` priority
- LLM calls: `Task`-based async; never `Task.Wait()` or `.Result`
- Use `System.Threading.Channels.Channel<T>` for producer-consumer pipelines

### Security
- API keys: Windows Credential Manager only — never in files, logs, or config JSON
- Audio buffers: zero out after transcription (`Array.Clear`)
- Screen captures: memory-only; never write to disk unless user explicitly exports

### Error Handling
- Catch specific exceptions; never `catch (Exception)` without logging
- Audio/transcription errors: log + restart; do not propagate to UI as crash
- LLM errors: surface as user-friendly message in overlay; retry with backoff

---

## Key Technical Challenges (Know These)

1. **Teams screenshot DRM** — `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` blocks all standard capture of Teams call window. Use layered strategy: WGC for non-Teams, clipboard monitor, image drop. Full details: `EPIC-06-screen-capture.md`.

2. **WASAPI exclusive mode** — Some audio drivers lock the device; loopback impossible. Detect and prompt user to install VB-Cable.

3. **Whisper cold start** — Model loads in 2–5s. Warm up on background thread at startup; never block the hotkey on model readiness.

4. **LowLevel keyboard hook thread requirement** — Must be installed on an STA thread with a running message loop. Hook callback must return in <1ms.

5. **WPF transparency + GPU** — `AllowsTransparency="True"` can disable hardware acceleration on some GPU drivers. Test on target hardware.

---

## MVP Stories (Build These First)

The following P0 stories form the minimum viable product. Build in this order:

| Order | Story | Epic |
|-------|-------|------|
| 1 | US-07-01: API Key Management | Settings |
| 2 | US-01-01: Capture System Audio Loopback | Audio |
| 3 | US-01-02: Capture Microphone Audio | Audio |
| 4 | US-01-04: Voice Activity Detection | Audio |
| 5 | US-01-05: Audio Ring Buffer | Audio |
| 6 | US-02-01: Local Whisper Transcription | Transcription |
| 7 | US-02-03: Rolling Transcript Buffer | Transcription |
| 8 | US-02-08: Context Window Prep for LLM | Transcription |
| 9 | US-03-01: Claude API Integration | LLM |
| 10 | US-03-04: Meeting System Prompt | LLM |
| 11 | US-03-05: Streaming Response | LLM |
| 12 | US-04-01: Global Hold-to-Ask Hotkey | Hotkeys |
| 13 | US-05-01: Transparent Overlay Window | UI |
| 14 | US-05-02: Response Display Panel | UI |
| 15 | US-08-02: Audio Buffer Auto-Purge | Privacy |
| 16 | US-08-06: Secure API Key Storage | Privacy |

---

## Build & Run (to be updated as code is written)

```powershell
# Build
dotnet build src/Chum.sln

# Run
dotnet run --project src/Chum.App

# Tests
dotnet test src/Chum.Tests
```

---

## GitHub Repository

`https://github.com/kushal-DL/chum`

Default branch: `main`
