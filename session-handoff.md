# Chum — Session Handoff

> **Read this at the start of every Claude session before doing anything else.**  
> Update this file at the end of each session (or whenever meaningful progress is made).

---

## App Intent & Vision

**Chum** is a real-time AI co-pilot for professionals in video calls. Think of it as OBS Studio meets Copilot — it runs silently in the background during Teams/Google Meet calls and provides LLM-powered assistance on demand.

### Core User Flows

**Flow 1 — Audio Query (Hold-to-Ask)**
User holds `Ctrl+Alt+Space` while a question is being asked in the meeting. Chum marks the audio window, transcribes the conversation, and fires a query to Claude/GPT. The AI response streams into a floating transparent overlay that only the user sees.

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
| VAD | Energy-based RMS MVP (Silero ONNX planned v0.2) | Silero is better but EnergyVad unblocks MVP immediately |
| STT | Whisper.NET (whisper.cpp) | Local by default; no cost; GPU-acceleratable |
| LLM primary | Anthropic Claude Haiku 4.5 (default), Sonnet 4.6 (quality) | Long context; fast; pluggable via `ILlmProvider` |
| LLM vision | Claude Sonnet 4.6 or GPT-4o | Both support multimodal |
| Screen capture | Windows.Graphics.Capture API | Most compatible; graceful Teams blackout (v0.2) |
| Global hotkeys | Win32 LowLevel Keyboard Hook | Reliable across all apps; full combo control |
| Secure storage | Windows Credential Manager (DPAPI) | OS-managed; never keys in files |

---

## Current Status

**Date of last update:** 2026-06-27  
**Phase:** MVP source code complete — awaiting first build verification

### What Was Done Session 1 (2026-06-27, Part 1)

Created the complete product backlog and project infrastructure:
- `/product-backlog/` folder with `README.md`, 10 epic files, and `BACKLOG-STATUS.md`
- `CLAUDE.md` project guide, `session-handoff.md`, `.gitignore`
- Pushed to `https://github.com/kushal-DL/chum`

### What Was Done Session 2 (2026-06-27, Part 2)

**Wrote all MVP source code** across 4 projects. Every file listed below is complete:

#### `src/Chum.Audio/`
- `Models/AudioChunk.cs` — `AudioSource` enum + `AudioChunk` record
- `Capture/IAudioCapture.cs` — Interface with `RawAudioAvailable` event
- `Capture/LoopbackCapture.cs` — `WasapiLoopbackCapture` wrapper
- `Capture/MicCapture.cs` — `WasapiCapture` wrapper (warns on Bluetooth HFP)
- `Vad/EnergyVad.cs` — RMS energy VAD with hysteresis (−40/−45 dBFS thresholds)
- `Pipeline/AudioConverter.cs` — IeeeFloat/PCM16/PCM32 → mono 16 kHz float32
- `Pipeline/AudioPipeline.cs` — Full pipeline: pre-buffer (300ms), post-silence (600ms), max segment (25s), `Channel<AudioChunk>` output, `Pause()`/`Resume()`

#### `src/Chum.Transcription/`
- `Models/TranscriptSegment.cs` — record with `SpeakerLabel` (Me/Remote)
- `WhisperSttEngine.cs` — Whisper.net integration, model auto-download, hallucination filter, zeroes samples after transcription
- `TranscriptBuffer.cs` — Thread-safe `LinkedList<TranscriptSegment>` with auto-eviction
- `ContextExtractor.cs` — Token-budget-aware context builder (last 30s always included; 8000 token budget)

#### `src/Chum.Llm/`
- `ILlmProvider.cs` — `LlmRequest` record + `ILlmProvider` interface (streaming)
- `AnthropicLlmProvider.cs` — Direct `HttpClient` + SSE streaming, no SDK; vision support
- `PromptBuilder.cs` — Meeting-optimised system prompt (≤150 words, no preamble)

#### `src/Chum.App/`
- `Models/AppSettings.cs` — All user preferences (LLM, audio, hotkeys, overlay, behaviour)
- `Services/SettingsService.cs` — JSON at `%APPDATA%\Chum\settings.json`
- `Services/CredentialService.cs` — AdysTech DPAPI wrappers for 4 credential targets
- `Services/HotkeyService.cs` — Win32 `WH_KEYBOARD_LL`, hold events, 300ms debounce
- `Services/ModelDownloadService.cs` — Whisper model dir helper; Silero VAD download URL
- `Services/MeetingOrchestrator.cs` — Wires all services together; runs transcription loop; handles all hotkey actions
- `ViewModels/OverlayViewModel.cs` — INPC, `AppendResponseToken`, `SetListeningState`, `TranscriptLines`, `StatusColor`
- `Views/OverlayWindow.xaml` + `.cs` — Transparent always-on-top WPF window, 4-row layout, pulsing indicator, streaming cursor, transcript expander
- `Views/SettingsWindow.xaml` + `.cs` — Dark-themed settings: API key save/test, model combos, hotkey textboxes, opacity slider, behaviour checkboxes
- `App.xaml` + `App.xaml.cs` — DI wiring, tray icon (`NotifyIcon` with context menu), startup flow (opens settings if no API key), `ShutdownMode="OnExplicitShutdown"`, Serilog file logging
- `Assets/chum.ico` — Placeholder 32×32 icon (generated via System.Drawing)

**BACKLOG-STATUS.md updated:** 116 SP 🔵 Built · 34 SP 🟡 Scaffolded · 160 SP 🔴 Yet to Start

---

## Immediate Next Step: Verify the Build

**The .NET 8 SDK is NOT installed on this machine.** Only the runtime exists at `C:\Program Files\dotnet\` — there is no `sdk/` subdirectory. `dotnet build` will fail until the SDK is installed.

**Action required (user):** Install .NET 8 SDK from https://aka.ms/dotnet/download, then run:
```powershell
cd c:\Users\kushal.f.sharma\repos\chum
dotnet build src\Chum.sln
```

Expected first-run issues:
1. NuGet restore (first run — needs internet access)
2. Whisper.net.Runtime may need GPU-specific package configuration
3. Possible WPF-specific warnings (harmless)

---

## What To Build Next (After Successful Build)

### Priority 1 — Fix any compile errors
Source files were written without a live compiler. Likely issues:
- Missing `using` statements
- Namespace conflicts (e.g., `System.Windows.Application` vs `Application`)
- NAudio API surface mismatches (verify event/property names)

### Priority 2 — First Run Test
1. Launch app → settings window should open (no API key yet)
2. Enter Anthropic key → click Test → should get "OK"  
3. Close settings → overlay window should appear bottom-right
4. Right-click tray icon → Start Capture → audio capture begins

### Priority 3 — Screen Capture (EPIC-06)
Next major feature after MVP works end-to-end. Start with:
- `US-06-01` — WGC API screen capture (`Windows.Graphics.Capture`)
- `US-06-07` — Multimodal LLM vision request (already wired in `AnthropicLlmProvider`)
- `US-06-02` — Clipboard image monitoring (simplest workaround for Teams DRM)

### Priority 4 — Silero VAD (US-01-04 → 🔵 Built)
Replace `EnergyVad` with proper Silero ONNX model for much better VAD accuracy.
- Model already has download URL in `ModelDownloadService`
- ONNX runtime already in `Chum.Audio.csproj`

---

## Known Decisions Deferred

| Decision | Why Deferred | When To Revisit |
|----------|-------------|-----------------|
| Screen capture / vision (EPIC-06) | Teams DRM complexity; not in MVP | After audio loop verified working |
| Silero VAD | EnergyVad unblocks MVP; Silero is better | After first successful build |
| OpenAI API provider | ILlmProvider interface ready; just needs impl | v0.2 |
| Multi-platform (macOS) | WPF is Windows-only | After v1.0 |
| Speaker diarization | Requires pyannote.audio or NeMo | v0.3 |
| Auto-update mechanism | Out of scope until distributable build | After first public release |

---

## File Map (Key Source Files)

```
src/
├── Chum.sln
├── Chum.Audio/
│   ├── Chum.Audio.csproj              (NAudio 2.2.1, OnnxRuntime 1.19.2)
│   ├── Models/AudioChunk.cs
│   ├── Capture/IAudioCapture.cs
│   ├── Capture/LoopbackCapture.cs
│   ├── Capture/MicCapture.cs
│   ├── Vad/EnergyVad.cs
│   └── Pipeline/AudioPipeline.cs + AudioConverter.cs
├── Chum.Transcription/
│   ├── Chum.Transcription.csproj      (Whisper.net 1.7.0)
│   ├── Models/TranscriptSegment.cs
│   ├── WhisperSttEngine.cs
│   ├── TranscriptBuffer.cs
│   └── ContextExtractor.cs
├── Chum.Llm/
│   ├── Chum.Llm.csproj                (no external packages)
│   ├── ILlmProvider.cs
│   ├── AnthropicLlmProvider.cs
│   └── PromptBuilder.cs
└── Chum.App/
    ├── Chum.App.csproj                (WPF+WinForms, AdysTech.CredentialManager, Serilog)
    ├── Models/AppSettings.cs
    ├── Services/
    │   ├── SettingsService.cs
    │   ├── CredentialService.cs
    │   ├── HotkeyService.cs
    │   ├── ModelDownloadService.cs
    │   └── MeetingOrchestrator.cs
    ├── ViewModels/OverlayViewModel.cs
    ├── Views/
    │   ├── OverlayWindow.xaml + .cs
    │   └── SettingsWindow.xaml + .cs
    ├── Assets/chum.ico                (placeholder 32×32 blue dot)
    ├── App.xaml                       (ShutdownMode=OnExplicitShutdown)
    └── App.xaml.cs                    (DI wiring, tray icon, startup logic)
```

---

## Context for Next Claude Session

1. All product backlog is in `product-backlog/` — `BACKLOG-STATUS.md` has current status per story
2. All MVP source code is written in `src/` — **needs .NET 8 SDK to build**
3. Read `CLAUDE.md` for code conventions and the mandatory backlog update protocol
4. Kushal is in a corporate environment, Windows 11, primary meeting app is Microsoft Teams
5. Repo: `https://github.com/kushal-DL/chum`

*Last updated: 2026-06-27 by Claude (Session 2 — complete MVP source code written)*
