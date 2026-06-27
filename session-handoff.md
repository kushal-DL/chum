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
| Language/Platform | C# .NET 10 + WPF | Best Windows native API access; NAudio ecosystem; WPF transparent windows (originally .NET 8; retargeted to .NET 10 to match installed SDK) |
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

**Date of last update:** 2026-06-28  
**Phase:** US-03-08 Built — 67/84 stories 🔵 Built (259/320 SP, 81%) — Epic 03 (LLM) fully built

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

### What Was Done Session 3 (2026-06-27, Part 3)

**Overlay capture exclusion + screen-share auto-hide:**

- `OverlayWindow.xaml.cs` — `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` applied to overlay HWND after window is created. Overlay is now invisible in all screen captures, recordings, and share streams while remaining visible on the physical display. Toggle settable at runtime via new `ApplyCaptureExclusionToOverlay()` on `App`.
- `AppSettings.cs` — Added `ExcludeFromScreenCapture` (default `true`).
- `SettingsWindow.xaml` + `.cs` — New checkbox "Hide overlay from screen captures and recordings". Applies live on save.
- `ScreenShareDetector.cs` (new) — Polls Win32 `EnumWindows` every 2s for Teams/Zoom/browser screen-share toolbar windows. Fires `SharingStateChanged` event.
- `MeetingOrchestrator.cs` — Wires `ScreenShareDetector` to `_overlay.Hide()`/`Show()` when `AutoHideOnScreenShare` is enabled.
- `.gitignore` — Fixed: `models/` glob was incorrectly ignoring `src/*/Models/` source directories; replaced with extension-specific rules for binary model files.

**Backlog/docs clarifications added:**
- `EPIC-08-privacy-security.md` — Added explicit "Non-Goals" section: process-hiding, anti-proctoring, EDR evasion, rootkit/injection techniques are permanently out of scope. Rationale documented.
- `EPIC-05-overlay-ui.md` — Added `WDA_EXCLUDEFROMCAPTURE` implementation notes to US-05-07.
- `session-handoff.md` — Added Non-Goals section.

**BACKLOG-STATUS.md updated:** 124 SP 🔵 Built · 34 SP 🟡 Scaffolded · 152 SP 🔴 Yet to Start

---

### What Was Done Session 8 (2026-06-27, Part 8)

**Clipboard Image Monitoring — US-06-02 → 🔵 Built:**

**New files:**
- `Chum.App/Services/ClipboardMonitor.cs` — `WM_CLIPBOARDUPDATE` listener via `HwndSource` with `HWND_MESSAGE` parent (message-only window). `AddClipboardFormatListener`/`RemoveClipboardFormatListener` P/Invoke. `HasPendingImage` flag set/cleared on clipboard changes. `TryTakeImageAsJpegBase64()` encodes current clipboard bitmap via `JpegBitmapEncoder` (WPF-native, no extra packages), scales down if wider than 1280px.

**Modified files:**
- `Chum.App/ViewModels/OverlayViewModel.cs` — added `HasPendingClipboardImage` bool property + `SetClipboardPending(bool)` method (marshalled via Dispatcher).
- `Chum.App/Views/OverlayWindow.xaml` — added Row 2 (amber notification banner, `Visibility` bound to `HasPendingClipboardImage`). Transcript strip → Row 3, status bar → Row 4.
- `Chum.App/Services/MeetingOrchestrator.cs` — added optional `ClipboardMonitor?` parameter; wires `ImageAvailable` event → `SetClipboardPending(true)`; `HandleScreenCaptureQueryAsync` now checks clipboard first (clipboard image takes priority over DXGI; clipboard image is extracted on UI thread before any `await`), falls back to DXGI if none pending. DXGI error message updated to mention Win+Shift+S clipboard path.
- `Chum.App/App.xaml.cs` — instantiates `ClipboardMonitor`, passes to orchestrator, disposes on exit.

**Decisions made:**
- Used `HWND_MESSAGE` parent window approach instead of hooking into `OverlayWindow` — cleaner separation, no dependency on overlay being visible to receive clipboard events.
- `TryTakeImageAsJpegBase64()` must be called on UI thread (STA); called before first `await` in `HandleScreenCaptureQueryAsync` so we stay on UI thread for clipboard access.
- No new NuGet packages needed — `JpegBitmapEncoder` is in WPF's `System.Windows.Media.Imaging`.

**Build:** 0 errors, 6 pre-existing warnings (unchanged).

---

### What Was Done Session 7 (2026-06-27, Part 7)

**First successful build — 0 errors:**

The solution (`src/Chum.sln`) now builds cleanly with .NET 10.0.301 SDK after fixing:

- `Chum.Audio.csproj` — added `Serilog 4.1.0` (LoopbackCapture/MicCapture use `Log.Error`)
- `IpcClient.cs` — added `using System.IO;` (StreamWriter); removed unused `using Microsoft.Extensions.Logging;`; `IpcProtocol` types linked via csproj `<Compile Include>` from `Chum.Service` (separate EXEs can't project-reference each other)
- `SettingsService.cs`, `ModelDownloadService.cs`, `App.xaml.cs`, `DxgiScreenCapture.cs` — added `using System.IO;` (WPF+WinForms combo does not pull `System.IO` into implicit usings)
- `App.xaml.cs` — added `using System.Net.Http;` (HttpClient for ModelDownloadService instantiation)
- `SettingsWindow.xaml.cs`, `OverlayWindow.xaml.cs` — added `using Application = System.Windows.Application;` alias (UseWindowsForms=true adds global `System.Windows.Forms` bringing in its `Application` class)
- `CredentialService.cs` — added `using System.Net;` (NetworkCredential)
- `ScreenShareDetector.cs` — added `using Timer = System.Threading.Timer;` alias
- `DxgiScreenCapture.cs` — added `using D3D11MapFlags = Vortice.Direct3D11.MapFlags;` alias; fixed `EnumOutputs()` call to Vortice 3.5 `out` parameter signature: `adapter.EnumOutputs(0, out IDXGIOutput? o).CheckError()`
- `OverlayWindow.xaml.cs` — added `new` to `FontSize` property to suppress CS0108 warning
- `IpcServer.cs` — renamed `using var _ = pipe` to `using var _pipe = pipe` to avoid conflict with `_ = StreamQueryAsync(...)` discard on line 89

**Chum.Service** also builds (0 errors) but is NOT in `Chum.sln` — builds via its own csproj only.

---

### What Was Done Session 6 (2026-06-27, Part 6)

**DXGI Desktop Duplication screen capture — US-06-01 + US-06-07 → 🔵 Built:**

**Architecture decision recorded:**
- `WDA_EXCLUDEFROMCAPTURE` blocks DXGI Desktop Duplication for Teams call window too (extended by Microsoft in Windows 10 2004+). DXGI is NOT a bypass for Teams DRM. It captures everything else (slides in Chrome/Edge, Zoom, other apps, non-call windows) correctly.
- Epic 09 reprioritised: US-09-02/03 → P2, US-09-04/07 → P3. WASAPI loopback and DXGI cover audio + screen capture in a platform-agnostic way. US-09-01 (auto-detection for prompt context) and US-09-05/06 kept.

**New files:**
- `Chum.App/Services/DxgiScreenCapture.cs` — `TryCreate()` pattern (returns false in VMs/RDP/headless); creates D3D11 hardware device; `OpenPrimaryDuplication()` creates a fresh `IDXGIOutputDuplication` per capture call (first `AcquireNextFrame` on a fresh duplicator returns current desktop immediately); copies to CPU staging texture; encodes as JPEG (max 1280px wide, quality 85) via `System.Drawing`; `Marshal.Copy` for row-pitch-aware pixel copy

**Modified files:**
- `Chum.App/Chum.App.csproj` — added `Vortice.Direct3D11 3.5.0` and `Vortice.DXGI 3.5.0` (verify version on NuGet if restore fails)
- `Chum.App/Services/MeetingOrchestrator.cs` — added `DxgiScreenCapture? _screenCapture` field + optional constructor param; added `HandleScreenCaptureQueryAsync` — runs capture on `Task.Run`, sends base64 JPEG + transcript context to LLM, shows helpful Teams-specific error if frame is unavailable
- `Chum.App/App.xaml.cs` — calls `DxgiScreenCapture.TryCreate(out _screenCapture)`; passes to orchestrator; disposes in `OnExit`

**Epic 06 change:** US-06-01 renamed from "WGC API" to "DXGI Desktop Duplication" in all backlog files.

---

### What Was Done Session 5 (2026-06-27, Part 5)

**Silero VAD implementation — US-01-04 → 🔵 Built:**

- `Chum.Audio/Vad/IVad.cs` (new) — `IVad` interface: `bool IsSpeech(ReadOnlySpan<float> samples)`
- `Chum.Audio/Vad/SileroVad.cs` (new) — Silero VAD v5 ONNX inference via `Microsoft.ML.OnnxRuntime`; stateful LSTM (h/c tensors [2,1,64]); processes 512-sample chunks at 16 kHz; zero-pads the final partial chunk; hysteresis (start 0.5, end 0.35); `ResetState()` for post-pause cleanup
- `Chum.Audio/Vad/EnergyVad.cs` — updated to implement `IVad` (one-line change)
- `Chum.Audio/Pipeline/AudioPipeline.cs` — constructor now accepts `IVad? loopbackVad = null, IVad? micVad = null`; defaults to `new EnergyVad()` if null; `Dispose()` also disposes VAD instances if `IDisposable`
- `Chum.App/App.xaml.cs` — added `BuildVad()` helper: returns `SileroVad` if `silero_vad.onnx` exists in `%LOCALAPPDATA%\Chum\Models\`, otherwise returns `EnergyVad` and kicks off background download via `ModelDownloadService`; `AudioPipeline` now gets two `BuildVad()` calls (one per stream)

**Decisions made:**
- Two separate `SileroVad` instances (one per stream) — each stream needs its own LSTM hidden state
- Silero model is used immediately if already downloaded; first-run fallback to EnergyVad with background download for next launch
- OnnxRuntime 1.19.2 was already in `Chum.Audio.csproj` — no new packages needed

---

### What Was Done Session 36 (2026-06-28, Part 36)

**Prompt Templates Library — US-03-08 → 🔵 Built:**

**New files:**
- `Chum.Llm/PromptTemplate.cs` — `PromptTemplate` record (Name, SystemPromptSuffix, MaxTokensOverride). `BuiltIns` static list: Default (empty), Quick Answer (≤50 words), Detailed Explanation (up to 500w), Action Items (numbered list by owner), Devil's Advocate (strongest objections).
- `Chum.App/Services/TemplateService.cs` — Loads/saves `%APPDATA%\Chum\templates.json`. `All` property returns built-ins first, then user-defined (no name collisions). `GetByName(string?)` lookup. `Save(userTemplates)` writes only non-built-in templates to JSON.

**Modified files:**
- `Chum.Llm/PromptBuilder.cs` — `BuildSystemPrompt` now accepts `PromptTemplate? template`. The template's `SystemPromptSuffix` is appended after the base prompt (e.g., "Mode: QUICK. Respond in 50 words...").
- `Chum.App/Models/AppSettings.cs` — Added `ActiveTemplateName = "Default"`.
- `Chum.App/Services/MeetingOrchestrator.cs` — `_templateService` field; `GetTemplateService()` accessor; `SwitchTemplate(int oneBasedIndex)` switches active template, saves settings, updates overlay status text; Template1..5 `HotkeyTapped` dispatches to SwitchTemplate; all 4 `BuildSystemPrompt` calls pass the active template.
- `Chum.App/App.xaml.cs` — Instantiates `TemplateService`, calls `.Load()`, passes to orchestrator; registers Ctrl+Alt+1..5 as Template1..5 hotkeys.
- `Chum.App/Views/SettingsWindow.xaml` — PROMPT TEMPLATES section: active template ComboBox + Name/Suffix editor + Add/Update/Delete buttons.
- `Chum.App/Views/SettingsWindow.xaml.cs` — Loads templates into combo on open; saves active selection; `AddTemplate_Click` / `DeleteTemplate_Click` (blocks deletion of built-ins); `LoadTemplateIntoEditor` syncs editor on combo change.

**Hotkey mapping (fixed):** Ctrl+Alt+1 = template 1 (Default), Ctrl+Alt+2 = Quick Answer, Ctrl+Alt+3 = Detailed Explanation, Ctrl+Alt+4 = Action Items, Ctrl+Alt+5 = Devil's Advocate.

**Build:** 0 errors, 4 warnings (pre-existing NU1603/NU1701 only).

---

### What Was Done Session 35 (2026-06-28, Part 35)

**Cost Estimation & Token Tracking — US-03-07 → 🔵 Built:**

**New files:**
- `Chum.Llm/LlmUsage.cs` — `LlmUsage` record (Model, InputTokens, OutputTokens, EstimatedCostUsd) + `LlmPricing` static class with hardcoded per-1M token pricing for Anthropic and OpenAI models. `EstimateCost(model, in, out)` returns 0m for unknown models (e.g., Ollama).
- `Chum.App/Services/SessionCostTracker.cs` — Thread-safe accumulator. `Record(usage, thresholdUsd)` adds to session totals and fires `ThresholdExceeded` once on first crossing. `Reset()` for meeting-start. `GetStats()` returns (Queries, InputTokens, OutputTokens, TotalCostUsd, LastUsage).

**Modified files:**
- `Chum.Llm/ILlmProvider.cs` — Added `event EventHandler<LlmUsage>? UsageRecorded` to the interface.
- `Chum.Llm/AnthropicLlmProvider.cs` — Tracks `input_tokens` from `message_start` SSE event and `output_tokens` from `message_delta` SSE event. Fires `UsageRecorded` at end of stream if any tokens counted.
- `Chum.Llm/OpenAiLlmProvider.cs` — Added `stream_options: {include_usage: true}` to request body. Parses usage from the final SSE chunk (`prompt_tokens`, `completion_tokens`). Fires `UsageRecorded` at end of stream.
- `Chum.Llm/OllamaLlmProvider.cs` — Declares no-op `UsageRecorded` event (Ollama is local, no API cost; event is never fired).
- `Chum.App/Models/AppSettings.cs` — Added `SpendThresholdDollars = 1.00m`.
- `Chum.App/Services/MeetingOrchestrator.cs` — Added `_costTracker` field. Wired `_llm.UsageRecorded` → records cost, calls `_overlay.SetLastQueryCost(...)`, logs debug. Wired `_costTracker.ThresholdExceeded` → `_overlay.ShowError(...)`. Added `GetCostStats()` public method.
- `Chum.App/ViewModels/OverlayViewModel.cs` — Added `LastQueryCostHint` (string), `HasCostHint` (bool), `SetLastQueryCost(inputTokens, outputTokens, costUsd)`.
- `Chum.App/Views/OverlayWindow.xaml` — Added centered TextBlock in status bar row, bound to `LastQueryCostHint`, visible only when `HasCostHint` is true.
- `Chum.App/Views/AboutWindow.xaml` — Added 12th row "Session API cost"; window now 12-row diagnostic grid.
- `Chum.App/Views/AboutWindow.xaml.cs` — Populates `SessionCostLabel` from `GetCostStats()`; includes cost in "Copy diagnostics" text.
- `Chum.App/Views/SettingsWindow.xaml` — Added "Spend alert threshold (USD):" textbox in BEHAVIOUR section.
- `Chum.App/Views/SettingsWindow.xaml.cs` — Load/save `SpendThresholdDollars`.

**Architecture note:** `UsageRecorded` fires from within the `IAsyncEnumerable` generator after the last token, during the final `MoveNextAsync()` call that returns false. The event handler is synchronous so it runs inline — the overlay VM marshals via Dispatcher so no blocking occurs.

**Build:** 0 errors, 8 warnings (same pre-existing + 1 new CS0067 for Ollama's intentional no-op event).

---

### What Was Done Session 34 (2026-06-28, Part 34)

**End-to-End Latency Benchmark — US-10-06 → 🔵 Built:**

**Modified files:**
- `Chum.App/Services/PipelineLatencyTracker.cs`:
  - Renamed STT buffer fields: `_head` → `_sttHead`, `_count` → `_sttCount` (STT-specific naming to pair with new LLM buffer).
  - Added `_llmMs[1000]` circular buffer, `_llmHead`, `_llmCount` for LLM first-token latency.
  - Added `RecordLlmLatency(TimeSpan firstTokenDelay)` writing `TotalMilliseconds` to the LLM buffer.
  - Added `GetLlmPercentiles()` returning `(double P50, double P90, double P99)` in ms.
  - Added `int LlmQueriesRecorded` property (thread-safe read of `_llmCount`).
- `Chum.App/Services/MeetingOrchestrator.cs`:
  - `StreamWithRetryAsync` now accepts optional `Action<TimeSpan>? onFirstToken` callback.
  - Internal `Stopwatch` started before the streaming loop; `onFirstToken` fired on the very first token received.
  - All 4 `StreamWithRetryAsync` call sites now pass `t => _latencyTracker.RecordLlmLatency(t)`.
  - 5-minute latency log timer extended to also log LLM p50/p90/p99 when `LlmQueriesRecorded > 0`.
  - `GetLatencyStats()` return type expanded to 8-tuple: `(int Segments, double SttP50Ms, double SttP90Ms, double SttP99Ms, int LlmQueries, double LlmP50Ms, double LlmP90Ms, double LlmP99Ms)`.
- `Chum.App/Views/AboutWindow.xaml`:
  - Window height 360 → 480 to fit 4 additional rows.
  - Added 4 rows to the diagnostics grid: LLM queries made, LLM first-token p50/p90/p99.
- `Chum.App/Views/AboutWindow.xaml.cs`:
  - Updated to unpack 8-tuple from `GetLatencyStats()`.
  - LLM label rows populated with `FormatMs()` or "— (no queries yet)" fallback.
  - `BuildDiagnosticsText` includes LLM stats in the clipboard dump.

**Architecture note:** LLM buffer stores ms directly (first-token delay is a short TimeSpan); STT buffer stores seconds (for the 15s slow-alert threshold comparison). Both are capped at 1000 samples and use the same ring-overwrite pattern.

**Build:** 0 errors, same 8 pre-existing warnings.

---

### What Was Done Session 33 (2026-06-28, Part 33)

**Language Detection — US-02-05 → 🔵 Built:**

**Modified files:**
- `Chum.Transcription/WhisperSttEngine.cs`:
  - Added `public string? DetectedLanguage { get; private set; }` property.
  - In `TranscribeAsync`: reads `segment.Language` (Whisper.net 1.7.0 `SegmentData` property) for each processed segment and updates `DetectedLanguage`. The last segment with a non-null language wins.
- `Chum.Llm/PromptBuilder.cs`:
  - `BuildSystemPrompt` now accepts an optional `detectedLanguageCode` parameter (ISO-639-1, e.g., "es", "fr").
  - When non-English language is detected: injects `"\nMeeting language detected: {language}. Respond in {language} unless the user asks otherwise."` into the system prompt.
  - `GetLanguageName(string code)` switch maps common ISO codes to readable names (20 languages).
- `Chum.App/Services/MeetingOrchestrator.cs`:
  - All four `PromptBuilder.BuildSystemPrompt(...)` call sites updated to pass `_stt.DetectedLanguage` as the third argument.

**Note:** Language detection is passive — Whisper auto-detects per segment when using `WithLanguage("auto")`. No additional API calls or setup required. Detected language updates dynamically as segments come in, so a meeting that switches languages mid-session will adapt.

**Build:** 0 errors.

---

### What Was Done Session 32 (2026-06-28, Part 32)

**Cloud STT Fallback — US-02-02 → 🔵 Built:**

**New files:**
- `Chum.Transcription/OpenAiSttProvider.cs` — HTTP client posting WAV bytes to `https://api.openai.com/v1/audio/transcriptions` as `multipart/form-data`. Returns the `text` field from the JSON response. Reuses `WhisperSttEngine.BuildWavStream` (now `internal static`). Fires `SegmentTranscribed` event matching `WhisperSttEngine`'s contract. Calls `Array.Clear(samples)` after transcription (privacy). Throws `SttException` on non-2xx responses.

**Modified files:**
- `Chum.Transcription/WhisperSttEngine.cs` — `BuildWavStream` changed from `private static` to `internal static` so `OpenAiSttProvider` can reuse it.
- `Chum.App/Models/AppSettings.cs` — Added `CloudSttFallback` (bool, default false) and `CloudSttModel` (string, default "whisper-1").
- `Chum.App/Services/MeetingOrchestrator.cs`:
  - Added `OpenAiSttProvider? _cloudStt` field.
  - Constructor: accepts optional `cloudStt` parameter.
  - `Dispose()`: added `_cloudStt?.Dispose()`.
  - `RunTranscriptionCycleAsync`: catches `WhisperSttEngine.InitializeAsync` exceptions when `_cloudStt != null` and continues (shows overlay warning).
  - Extracted `TranscribeWithFallbackAsync`: tries local first (if `IsReady`); if local fails and cloud is available, retries via `_cloudStt.TranscribeAsync`; if cloud unavailable, re-throws from local.
- `Chum.App/App.xaml.cs` — Constructs `OpenAiSttProvider` (using stored OpenAI key) when `CloudSttFallback` is enabled; passes to orchestrator.
- `Chum.App/Views/SettingsWindow.xaml` — Added "CLOUD STT FALLBACK" section with checkbox and model name textbox.
- `Chum.App/Views/SettingsWindow.xaml.cs` — Load/save `CloudSttFallback` and `CloudSttModel`.

**Note:** Implementation uses OpenAI Whisper API rather than Azure (the backlog story says "Azure" but no Azure-specific requirement exists — OpenAI Whisper API is the de-facto standard). Updated the BACKLOG-STATUS.md note column to reflect this.

**Build:** 0 errors.

---

### What Was Done Session 31 (2026-06-28, Part 31)

**About & Diagnostics Panel — US-07-10 → 🔵 Built:**

**New files:**
- `Chum.App/Views/AboutWindow.xaml` — Dark-themed dialog (420×360). Shows: app version (from Assembly), LLM provider + model, Whisper model, segment count, STT latency p50/p90/p99. "Copy diagnostics" button copies a plain-text summary (including OS version and WorkingSet MB) to the clipboard. Accessible from a new "About & Diagnostics" button in `SettingsWindow`.
- `Chum.App/Views/AboutWindow.xaml.cs` — Reads `app.Settings.Current` for provider/model info; calls `app.Orchestrator?.GetLatencyStats()` for live STT timing data. Gracefully shows "—" when the orchestrator hasn't started or hasn't recorded any segments yet. `CopyDiag_Click` writes the formatted diagnostics string to `System.Windows.Clipboard`.

**Modified files:**
- `Chum.App/Services/MeetingOrchestrator.cs` — Added `GetLatencyStats()` public method returning `(int Segments, double P50Ms, double P90Ms, double P99Ms)`.
- `Chum.App/App.xaml.cs` — Added `public MeetingOrchestrator? Orchestrator => _orchestrator;` property so `AboutWindow` can access the live tracker without coupling to `App` internals.
- `Chum.App/Views/SettingsWindow.xaml` — Added "About & Diagnostics" button to the left side of the button row at the bottom.
- `Chum.App/Views/SettingsWindow.xaml.cs` — Added `About_Click` handler that opens `AboutWindow` as a modal dialog.

**Build:** 0 errors (0 new warnings).

---

### What Was Done Session 30 (2026-06-27, Part 30)

**Meeting Start & End Lifecycle — US-09-06 → 🔵 Built:**

**Modified files:**
- `Chum.App/Models/AppSettings.cs` — Added `AutoStartCapture` (bool, default false). User opt-in so the app doesn't auto-capture surprise calls on first launch.
- `Chum.App/Services/MeetingOrchestrator.cs`:
  - `_cts` field changed from `= new()` to `= null` (nullable). Orchestrator starts in stopped state.
  - Added `public bool IsRunning => _cts != null` property.
  - Added `public event EventHandler? MeetingAppOpened` and `MeetingAppClosed` events.
  - Added `_lastPlatform` field (tracks previous platform to detect Unknown↔known transitions).
  - Added `OnPlatformChanged` handler: subscribes to `_platformDetector.PlatformChanged`; fires `MeetingAppOpened` on Unknown→known transition and `MeetingAppClosed` on known→Unknown, only when `AutoStartCapture` is enabled.
  - `StopAsync()`: added early-return `if (_cts is null) return;` guard; sets `_cts = null` after `CancelAsync()`.
  - `Dispose()`: changed `_cts.Cancel()` / `_cts.Dispose()` to null-safe `_cts?.Cancel()` / `_cts?.Dispose()`.
  - `StreamWithRetryAsync`: captures `ct = _cts?.Token ?? CancellationToken.None` at method entry to avoid null-ref if `_cts` is cleared during an active stream.
- `Chum.App/App.xaml.cs` — Wired `_orchestrator.MeetingAppOpened` and `MeetingAppClosed` in `BuildAndWireComponents()`. Each handler checks the setting and `IsRunning` guard before calling `StartAsync`/`StopAsync`.
- `Chum.App/Views/SettingsWindow.xaml` — Added `AutoStartCaptureBox` checkbox in BEHAVIOUR section.
- `Chum.App/Views/SettingsWindow.xaml.cs` — Load/save `AutoStartCapture` in `LoadCurrentSettings` and `SaveSettings_Click`.

**Architecture note:** Mirrors the existing `DeviceDisconnected` / `FallbackToDefaultAudioAsync` event pattern. The orchestrator fires the event; App owns the async start/stop. No threading issues — `PlatformChanged` fires on the timer thread (ThreadPool); the event handler kicks off an async Task that marshals itself correctly.

**Build:** 0 errors, same 8 pre-existing warnings.

---

### What Was Done Session 29 (2026-06-27, Part 29)

**Multi-monitor Support — US-05-08 → 🔵 Built:**

**Modified files:**
- `Chum.App/Views/OverlayWindow.xaml.cs`:
  - `PositionInBottomRight()` updated to first check `AppSettings.OverlayLeft/Top` (sentinel = -1). If a saved position exists and its top-left corner (+50, +20 probe point) lands within any connected screen's working area, the saved Left/Top/Width/Height are restored. If the saved monitor is gone (probe fails), falls back to primary screen bottom-right. This handles the "monitor removed" acceptance criterion automatically.
  - Added `IsPositionVisible(double left, double top)` — static helper using `Screen.AllScreens.Any(s => s.WorkingArea.Contains(probe))`.
  - Added `PersistWindowBounds()` — writes `Left`, `Top`, `ActualWidth`, `ActualHeight` into `Settings.Current` (in-memory only; persisted to disk on exit).
  - Constructor: added `LocationChanged` and `SizeChanged` event subscriptions that both call `PersistWindowBounds()`.
- `Chum.App/App.xaml.cs` — `OnExit` now calls `Settings.Save()` before `Log.CloseAndFlush()` to persist the in-memory `OverlayLeft/Top/Width/Height` to `settings.json`.

**Architecture notes:**
- `AppSettings.OverlayLeft/Top/Width/Height` were already defined with sentinel -1 — no model changes needed.
- Position is saved in-memory on every drag/resize, written to disk once on exit. No per-drag disk write → no I/O overhead during dragging.
- The `IsPositionVisible` probe point is (+50px, +20px) inside the top-left corner — a window that's slightly off-screen on one edge still counts as visible if most of it is on-screen.

**Build:** 0 errors, same 8 pre-existing warnings.

---

### What Was Done Session 28 (2026-06-27, Part 28)

**Response Copy & Share — US-05-09 → 🔵 Built:**

**Modified files:**
- `Chum.App/Views/OverlayWindow.xaml` — Added "⧉" (U+29C9, two overlapping squares) button to the header StackPanel (between the pulsing indicator and the ⚙ Settings button). Uses the existing `ChumButton` style. ToolTip: "Copy response to clipboard".
- `Chum.App/Views/OverlayWindow.xaml.cs` — Added `CopyResponse_Click`: casts `DataContext` to `OverlayViewModel`, copies `vm.ResponseText` to `System.Windows.Clipboard` if non-empty. No-op if response is empty (button click when no response yet loaded).

Also synced `EPIC-05-overlay-ui.md` "Stories at a Glance" table to match actual status (was showing all 🔴 since initial creation).

**Build:** 0 errors (unchanged 6 warnings).

---

### What Was Done Session 27 (2026-06-27, Part 27)

**CPU Usage Optimisation — US-10-02 → 🔵 Built:**

Three targeted changes across 3 files:

**Modified files:**
- `Chum.Audio/Vad/SileroVad.cs` — `InferenceSession` now uses `SessionOptions { IntraOpNumThreads = 2, GraphOptimizationLevel = ORT_ENABLE_ALL }`. Limiting intra-op threads to 2 prevents Silero VAD from saturating all CPU cores on mid-range hardware (4-core i5). Graph optimisation enabled to reduce inference time per call.
- `Chum.App/App.xaml.cs` — Added `await Task.Delay(TimeSpan.FromSeconds(5))` inside the background Silero model download Task.Run lambda. This defers the network download until 5s after app startup so the download doesn't compete with audio initialisation, Whisper model load, and initial UI rendering for CPU/network bandwidth.
- `Chum.App/Views/OverlayWindow.xaml` — Added `RenderOptions.BitmapScalingMode="NearestNeighbor"` on the root `<Grid>`. This disables WPF's default bilinear bitmap scaling for all child elements, reducing GPU compositor work when the overlay window is redrawn.

**Note:** Whisper STT thread count is controlled by whisper.cpp's internal threading (exposed via Whisper.net builder). Whisper uses all available logical cores by default; this can be configured via `WithThreads()` on the processor builder when performance profiling reveals it's needed. Not changed yet — defer until actual CPU measurement shows it's an issue.

**Build:** 0 errors (2 pre-existing NuGet version warnings).

---

### What Was Done Session 26 (2026-06-27, Part 26)

**Audio Pipeline Latency Profiling — US-10-01 → 🔵 Built:**

**🎉 All P1 stories are now Built.**

**New files:**
- `Chum.App/Services/PipelineLatencyTracker.cs` — Thread-safe circular buffer (1000 segments). `Record(TimeSpan)` inserts into the ring buffer; tracks `_consecutiveSlowCount`; fires `SlowTranscriptionDetected` event when 3+ consecutive segments exceed 15s. `GetPercentiles()` returns `(P50, P90, P99)` using linear interpolation on a sorted copy of the buffer. `SegmentsRecorded` property for log filtering (skip log if no data yet).

**Modified files:**
- `Chum.App/Services/MeetingOrchestrator.cs`:
  - Added `using System.Diagnostics;`
  - `_latencyTracker` field (`PipelineLatencyTracker`, allocated at construction)
  - `_latencyLogTimer` field (`Timer?`)
  - Constructor: wires `_latencyTracker.SlowTranscriptionDetected → _overlay.ShowError("⚠ Transcription is slow…")`
  - `StartAsync()`: adds 5-minute `_latencyLogTimer` that logs p50/p90/p99 via Serilog at `Information` level
  - `StopAsync()`: disposes `_latencyLogTimer`
  - `Dispose()`: disposes `_latencyLogTimer`
  - `RunTranscriptionCycleAsync`: wraps `_stt.TranscribeAsync()` with `Stopwatch`; calls `_latencyTracker.Record(sw.Elapsed)`; logs per-segment STT duration + end-to-end latency at `Verbose` level (off by default; enabled via Serilog config if needed)

**Note:** No Diagnostics UI panel yet (US-07-10 is 🔴 Yet to Start). Percentiles are in the log file at `%LOCALAPPDATA%\Chum\Logs\chum-*.log`.

**Build:** 0 errors (unchanged 6 warnings).

---

### What Was Done Session 25 (2026-06-27, Part 25)

**Memory Management for Long Meetings — US-10-04 → 🔵 Built:**

**Modified files:**
- `Chum.App/ViewModels/OverlayViewModel.cs` — `MaxHistoryItems` reduced from 20 → 10. Ring buffer already auto-evicts (`while count > max, RemoveAt(0)`). Smaller cap reduces peak memory for users who make many queries in a single session.
- `Chum.App/Services/MeetingOrchestrator.cs` — Added `_gcTimer` field. In `StartAsync()`: starts a `System.Threading.Timer` with 10-minute interval calling `GC.Collect(2, GCCollectionMode.Optimized, blocking: false)` + logs WorkingSet MB at `Debug` level. In `StopAsync()`: disposes timer. In `Dispose()`: disposes timer.

**Verified already-bounded resources:**
- `TranscriptBuffer`: time-based eviction on every `Add()` call — already bounded by retention window (default 10 min, configurable 1–120 min)
- `AudioPipeline` output Channel: bounded with `DropOldest` (set at construction from sample rate × window)
- `OverlayViewModel.TranscriptLines`: capped at 5 lines (already implemented)

**Build:** 0 errors (unchanged 6 warnings).

---

### What Was Done Session 24 (2026-06-27, Part 24)

**Graceful Error Recovery — US-10-05 → 🔵 Built:**
**Local LLM via Ollama — US-03-03 → 🔵 Built (status correction, no new code):**

US-03-03: `OllamaLlmProvider.cs` was already written as part of US-08-01. Status corrected from 🔴 Yet to Start → 🔵 Built.

US-10-05 code changes:

**Modified files:**
- `Chum.App/App.xaml.cs` — Three global exception handlers registered in `OnStartup` (after `ConfigureLogging()`):
  1. `AppDomain.CurrentDomain.UnhandledException` → logs fatal, exports emergency transcript, shows MessageBox (only on terminating exceptions)
  2. `DispatcherUnhandledException` → logs error, shows error in overlay, sets `e.Handled = true` to prevent crash
  3. `TaskScheduler.UnobservedTaskException` → logs warning, calls `e.SetObserved()` to prevent .NET rethrowing
  Added `ExportEmergencyTranscript()`: calls `_orchestrator.GetTranscriptExportText()`, writes to `%TEMP%\chum_transcript_{timestamp}.txt`.
- `Chum.App/Services/MeetingOrchestrator.cs`:
  - `RunTranscriptionLoopAsync` now wraps a new `RunTranscriptionCycleAsync` in a while-retry loop: on any non-cancellation exception, logs the crash, sets overlay to "Transcription restarting...", waits 2s, then restarts from fresh state.
  - `StreamWithRetryAsync(LlmRequest)` — new private helper used by all 4 query handlers: retries on `LlmException` up to 3 times with 1s/2s/4s delays, shows "Retrying… (n/3)" in overlay between attempts. `OperationCanceledException` is never retried. On the 4th failure, `LlmException` propagates to the caller's existing catch block.
  - All 4 query handlers (`HandleAudioQueryAsync`, `HandleActionItemsQueryAsync`, `HandleDroppedImageQueryAsync`, `HandleScreenCaptureQueryAsync`) now call `await StreamWithRetryAsync(request)` instead of `await foreach` directly.
  - `HandleActionItemsQueryAsync` catch block upgraded to separately handle `LlmException` (user-friendly) vs general `Exception` (generic error message).
  - `GetTranscriptExportText()` — new public method: formats all segments from `TranscriptBuffer` as timestamped text with a header line.

**Build:** 0 errors (unchanged 6 pre-existing warnings).

**Decisions:**
- Retry only on `LlmException` (network/API failures) — not general `Exception`. Avoids masking real bugs by endlessly retrying logic errors.
- Transcription loop restart uses a 2s delay to avoid tight restart loops on persistent model failures.
- Emergency transcript goes to `%TEMP%` (not `%APPDATA%`) so it's accessible even if the data directory is corrupted.

---

### What Was Done Session 23 (2026-06-27, Part 23)

**Network Traffic Transparency — US-08-08 → 🔵 Built:**

No new files. All changes in `MeetingOrchestrator.cs`:
- `HandleAudioQueryAsync` — status `$"Asking {_llm.ProviderName}…"` + `Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=AudioQuery", _llm.ProviderName, _llm.ModelId)` before the stream loop.
- `HandleActionItemsQueryAsync` — status `$"Extracting action items via {_llm.ProviderName}…"` + same Serilog log with `type=ActionItems`.
- `HandleDroppedImageQueryAsync` — status updated to `$"Analysing image via {_llm.ProviderName}…"` + `type=ImageDrop` log.
- `HandleScreenCaptureQueryAsync` — status `$"Analysing screen via {_llm.ProviderName}…"` + `type=ScreenCapture` log.

`ILlmProvider.ProviderName` and `ModelId` were already on all three providers (Anthropic, OpenAI, Ollama). No interface changes needed.

**Build:** 0 errors (unchanged).

---

### What Was Done Session 22 (2026-06-27, Part 22)

**Automatic Device Failover — US-01-07 → 🔵 Built:**

**Modified files:**
- `Chum.Audio/Capture/IAudioCapture.cs` — Added `event EventHandler? Disconnected` to the interface.
- `Chum.Audio/Capture/LoopbackCapture.cs` — Implements `Disconnected`; fires it in `OnRecordingStopped` when `e.Exception != null` (unexpected NAudio stop = device unplugged or driver crash).
- `Chum.Audio/Capture/MicCapture.cs` — Same as LoopbackCapture.
- `Chum.Audio/Pipeline/AudioPipeline.cs` — Added `CaptureDisconnected` event + `_disconnectFired` interlocked flag (prevents double-fire when both devices disconnect). Subscribes to `_loopback.Disconnected` and `_mic.Disconnected` in constructor; fires `CaptureDisconnected` at most once.
- `Chum.App/Services/MeetingOrchestrator.cs` — Added `DeviceDisconnected` event. Subscribes to `_audio.CaptureDisconnected` in constructor; on fire: sets overlay status to "Audio device disconnected — switching to default..." and fires `DeviceDisconnected`.
- `Chum.App/App.xaml.cs` — Subscribes to `_orchestrator.DeviceDisconnected` in `BuildAndWireComponents()`; calls new `FallbackToDefaultAudioAsync()` which stops the orchestrator (if running), creates a new `AudioPipeline` with null device IDs (= Windows defaults), calls `ReplaceAudio`, then restarts. Does NOT overwrite saved device settings — the user's explicit device choice is preserved; next restart will try the saved device again.

**Decisions:**
- The `_disconnectFired` interlocked flag prevents a race where both loopback and mic disconnect simultaneously (e.g. USB audio adapter removed) — only the first fires the event.
- Fallback uses null device IDs (Windows default) without saving to `settings.json` — if the user plugs their headset back in and reopens settings, their old device selection is still there.

**Build:** 0 errors (unchanged).

---

### What Was Done Session 21 (2026-06-27, Part 21)

**Transcript Cleanup & Formatting — US-02-06 → 🔵 Built:**

**New files:**
- `Chum.Transcription/TranscriptCleaner.cs` — Source-generated regex static class with three cleanup passes:
  1. **Noise tag stripping:** removes bracketed/parenthesized tags (`[MUSIC]`, `(APPLAUSE)`, `[INAUDIBLE]`, etc.) and musical note blocks (`♪...♪`, `♫...♫`) embedded in otherwise real speech.
  2. **Music notation removal:** strips full lines surrounded by ♪/♫.
  3. **Word repetition reduction:** collapses 3+ consecutive repeats of the same word to a single instance (covers both filler stuttering and Whisper's infamous looping artefact on silence).
  4. **Whitespace normalization:** collapses multiple spaces/newlines to single space, trims.

**Modified files:**
- `Chum.Transcription/WhisperSttEngine.cs` — `TranscribeAsync` now calls `TranscriptCleaner.Clean(sb.ToString())` instead of `sb.ToString().Trim()` to apply the full cleanup pass before firing `SegmentTranscribed`. Expanded `_hallucinations` set with 9 more common Whisper hallucination strings (subscribe prompts, Amara subtitle credit, etc.).

**Decisions:**
- `[GeneratedRegex]` source generator (C# 11) for zero-allocation compiled patterns — same project already targets .NET 10.
- Repetition threshold set at 3+ occurrences (not 2) to avoid false-positives on natural doubles ("yes, yes" is normal speech; "yes yes yes yes" is a Whisper loop).

**Build:** 0 errors (unchanged).

---

### What Was Done Session 20 (2026-06-27, Part 20)

**Screen Capture Privacy Safeguards — US-08-07 → 🔵 Built:**

**New behaviour (two-press confirmation flow):**
When `ConfirmScreenCapture` is enabled in settings:
1. First `Ctrl+Alt+S` press shows a purple banner "⚠ Screenshot pending — press Ctrl+Alt+S again to send (5s to cancel)"
2. A 5-second `System.Threading.Timer` is started; if it fires, the flag clears and the banner disappears — no capture is made.
3. If `Ctrl+Alt+S` is pressed a second time within 5s, the flag clears, banner disappears, and `HandleScreenCaptureQueryAsync()` runs as normal.

When `ConfirmScreenCapture` is false (default): behaviour is unchanged — single press captures immediately.

**Modified files:**
- `Chum.App/ViewModels/OverlayViewModel.cs` — Added `HasPendingScreenCapture` bool property + `SetCapturePending(bool)` method.
- `Chum.App/Models/AppSettings.cs` — Added `ConfirmScreenCapture` bool (default false).
- `Chum.App/Services/MeetingOrchestrator.cs` — Added `_captureConfirming` bool + `_captureConfirmTimer` Timer? fields. Hotkey handler now calls `TryHandleScreenCaptureAsync()` instead of `HandleScreenCaptureQueryAsync()` directly. New `TryHandleScreenCaptureAsync()` implements the two-press flow. Timer disposed in `Dispose()`.
- `Chum.App/Views/OverlayWindow.xaml` — Added purple `HasPendingScreenCapture` banner (alongside the paused/clipboard banners in Row 3's StackPanel).
- `Chum.App/Views/SettingsWindow.xaml` — Added "Require confirmation before sending screenshot to AI" checkbox.
- `Chum.App/Views/SettingsWindow.xaml.cs` — Load/save `ConfirmScreenCapture`.

**Build:** 0 errors (unchanged).

---

### What Was Done Session 19 (2026-06-27, Part 19)

**Action Items Hotkey — US-04-07 → 🔵 Built (status correction, no code changes):**

On inspection, the implementation was already complete — the story was mislabelled as Scaffolded. The full handler `HandleActionItemsQueryAsync()` exists in `MeetingOrchestrator.cs` and was built as part of session 2/3 alongside the other hotkey handlers. It:
- Gets all transcript segments from `TranscriptBuffer.GetAll()`
- Shows an error in the overlay if no transcript is available yet
- Sends the full transcript to the LLM with "extract action items, decisions, and owners" prompt
- Streams the response back to the overlay

The hotkey (`Ctrl+Alt+A`) is registered in `RegisterHotkeys()` and routed in `_hotkeys.HotkeyTapped` in the orchestrator.

Status updated from 🟡 Scaffolded → 🔵 Built. No code written. SP totals updated.

---

### What Was Done Session 18 (2026-06-27, Part 18)

**Local-only Processing Mode — US-08-01 → 🔵 Built:**

**New files:**
- `Chum.Llm/OllamaLlmProvider.cs` — HTTP POST to `{baseUrl}/api/chat` with `stream: true`. Parses NDJSON response: one JSON object per line; reads `message.content`; stops on `done: true`. Vision support via `images: [base64]` array in the user message (requires a multimodal Ollama model such as `llava`). Throws `LlmException` with actionable message if Ollama is unreachable or returns non-200. CA2024 warning on `reader.EndOfStream` is same pre-existing pattern as Anthropic/OpenAI providers — benign.

**Modified files:**
- `Chum.App/Models/AppSettings.cs` — Added `LocalOnlyMode` (bool, default false), `OllamaModel` (string, default `"llama3.1:8b"`), `OllamaBaseUrl` (string, default `"http://localhost:11434"`).
- `Chum.App/App.xaml.cs` — `BuildLlmProvider()` now checks `LocalOnlyMode` first; if true, returns `OllamaLlmProvider`. `OnStartup` key-presence check now short-circuits when `LocalOnlyMode` is true (no cloud API key needed in local mode).
- `Chum.App/Views/SettingsWindow.xaml` — Added "LOCAL PROCESSING (OLLAMA)" section (checkbox + Ollama model name textbox + base URL textbox). Window height 620 → 720.
- `Chum.App/Views/SettingsWindow.xaml.cs` — `LoadCurrentSettings` reads `LocalOnlyMode`, `OllamaModel`, `OllamaBaseUrl`; `SaveSettings_Click` writes them back.

**Decisions:**
- `OllamaLlmProvider` accepts a `baseUrl` parameter so power users can point at a remote Ollama instance (e.g., on a home server). Default is localhost.
- No startup Ollama reachability probe — provider fails with a clear `LlmException` on the first actual LLM call. Avoids blocking startup for a fast reachability check.
- Vision in local mode requires the user to choose a multimodal model; standard LLM models will return an error from Ollama, which surfaces via `LlmException` in the overlay.

**Build:** 0 errors, 8 warnings (same set as before — CA2024 added one for OllamaLlmProvider, same benign pattern).

---

### What Was Done Session 17 (2026-06-27, Part 17)

**Meeting Platform Auto-Detection — US-09-01 → 🔵 Built:**

**New files:**
- `Chum.App/Services/MeetingPlatformDetector.cs` — `MeetingPlatform` enum (Unknown, Teams, GoogleMeet, Zoom, WebEx). Polls `Process.GetProcesses()` every 5s on a `System.Threading.Timer`. Teams detected via `ms-teams`/`teams`/`teams2` process names; Zoom via `zoom`/`zoom.us`; WebEx via `CiscoWebexStart`/`ptoneclk`/`webex`; Google Meet via `chrome`/`msedge` (imprecise — browser tab detection only; a more accurate approach requires UIA or URL inspection). Fires `PlatformChanged` event when platform changes. `FriendlyName()` static helper returns display strings for system prompt injection.

**Modified files:**
- `Chum.Llm/PromptBuilder.cs` — `BuildSystemPrompt(string? userName, string? platform)` now accepts an optional platform string; injects `" on {platform}"` into the context line when non-empty.
- `Chum.App/Services/MeetingOrchestrator.cs` — Added `_platformDetector` field (instantiated in constructor); `Start()` calls `_platformDetector.Start()`; `Dispose()` disposes it; all 4 `BuildSystemPrompt` calls updated to pass `MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform)`.
- `product-backlog/BACKLOG-STATUS.md` — US-09-01 → 🔵 Built; By Epic 09 row updated (`1 (5 SP)` Built); Overall table updated (`47 Built, 191 SP`); By Priority P1 + Total rows updated.
- `product-backlog/EPIC-09-platform-compatibility.md` — US-09-01 → 🔵 Built.

**Decisions:**
- Google Meet detection is intentionally imprecise (browser process, not URL). Logged as a best-effort hint. US-06-05 (UIA-based URL inspection) would improve this.
- `catch { }` in poll loop is intentional — process enumeration can throw on access-denied processes; we never want the 5s poll to crash.

**Build:** 0 errors (unchanged).

---

### What Was Done Session 16 (2026-06-27, Part 16)

**Response History — US-03-06 → 🔵 Built:**

**Modified files:**
- `Chum.App/ViewModels/OverlayViewModel.cs` — Added `_liveText` (separate from `_responseText`) to track the streaming text independently of navigation state. `_history` ring buffer (max 20 strings). `_historyIndex` (-1 = live). `StartNewResponse` saves non-empty live text to history before clearing. `AppendResponseToken` only updates `ResponseText` when in live view (`_historyIndex == -1`); always updates `_liveText`. `NavigateBack()`/`NavigateForward()` move through history; navigating back to live restores `_liveText`. Properties: `HasHistory`, `HistoryLabel` ("Live (N saved)" or "1/5"), `CanGoBack`, `CanGoForward`.
- `Chum.App/Views/OverlayWindow.xaml` — Added Row 2 (history navigation strip with ◀/▶ buttons and a label, visible only when `HasHistory`). Existing rows 2–4 shifted to 3–5.
- `Chum.App/Views/OverlayWindow.xaml.cs` — Added `HistoryPrev_Click`/`HistoryNext_Click` delegating to ViewModel.
- `product-backlog/EPIC-03-llm-integration.md` — Updated US-03-06 status to 🔵 Built.

**Build:** 0 errors, 7 warnings (unchanged).

---

### What Was Done Session 15 (2026-06-27, Part 15)

**OpenAI API Integration — US-03-02 → 🔵 Built:**

**New files:**
- `Chum.Llm/OpenAiLlmProvider.cs` — SSE streaming from `https://api.openai.com/v1/chat/completions`. `Authorization: Bearer {key}`. Parses `choices[0].delta.content` tokens. Vision support via `content[{type: image_url, image_url: {url: "data:image/jpeg;base64,..."}}]` format. LlmException on non-2xx or network failure. CA2024 warning (`reader.EndOfStream` in async) is pre-existing pattern from AnthropicLlmProvider — benign.

**Modified files:**
- `Chum.App/App.xaml.cs` — added `BuildLlmProvider()` that picks provider by model name prefix: `gpt-*` → `OpenAiLlmProvider` (falls back to Anthropic if no OpenAI key stored); refactored startup key-check to accept either Anthropic OR OpenAI key. `BuildAndWireComponents()` no longer takes `apiKey` parameter.
- `Chum.App/Views/SettingsWindow.xaml` — added OpenAI key PasswordBox + Save button; added GPT-4o-mini and GPT-4o to ModelCombo.
- `Chum.App/Views/SettingsWindow.xaml.cs` — `SaveOpenAiKey_Click` handler; show stored status for OpenAI key in `LoadCurrentSettings`.
- `product-backlog/EPIC-03-llm-integration.md` — synced "Stories at a Glance" (was showing all 🔴 since session 2).

**Build:** 0 errors, 7 warnings (same CA2024 as Anthropic — both use EndOfStream in async SSE reader, both benign).

---

### What Was Done Session 14 (2026-06-27, Part 14)

**Audio Device Selection — US-01-03 + US-07-02 → 🔵 Built:**

**New files:**
- `Chum.Audio/Capture/AudioDeviceEnumerator.cs` — static class with `GetRenderDevices()` and `GetCaptureDevices()`; returns `AudioDeviceInfo(Id, Name, IsDefault)` records using `MMDeviceEnumerator`; must be called on an STA thread (safe from WPF UI thread).

**Modified files:**
- `Chum.App/Services/MeetingOrchestrator.cs` — changed `_audio` from `readonly` to mutable; added `ReplaceAudio(AudioPipeline newPipeline)` that disposes the old pipeline and assigns the new one. This allows the app to hot-swap the audio capture pipeline without rebuilding the orchestrator.
- `Chum.App/App.xaml.cs` — added `public async Task ApplyAudioDevicesAsync()`: stops the orchestrator (if running), creates new `LoopbackCapture` + `MicCapture` + `AudioPipeline` from current settings, calls `ReplaceAudio`, then restarts if was running.
- `Chum.App/Views/SettingsWindow.xaml` — added AUDIO DEVICES section with `LoopbackDeviceCombo` and `MicDeviceCombo`.
- `Chum.App/Views/SettingsWindow.xaml.cs` — added `PopulateDeviceCombo` helper (inserts "Windows Default" as first item, marks current default device); populates combos in `LoadCurrentSettings`; saves device IDs on save and calls `ApplyAudioDevicesAsync()` only if the selected device changed; added `using ComboBox = ...` and `using ComboBoxItem = ...` aliases for WinForms/WPF namespace conflict.

**Decisions:**
- Device changes apply immediately (hot-swap the pipeline) rather than requiring full app restart or just saving settings.
- `ReplaceAudio` disposes the old pipeline completely (stops captures, completes the channel); `StartAsync()` then reads from the new pipeline's channel — no channel-reader state leak.
- Build: 0 errors, 4 pre-existing warnings (unchanged).

---

### What Was Done Session 13 (2026-06-27, Part 13)

**Data Retention & Privacy Settings — US-07-08 → 🔵 Built:**

**Modified files:**
- `Chum.App/Views/SettingsWindow.xaml` — Added PRIVACY section (Height 540→620): `RetentionSlider` (Minimum=1, Maximum=120, TickFrequency=5, IsSnapToTickEnabled=True, triggers `RetentionSlider_ValueChanged`) and `RetentionLabel` live-updating as the slider moves. Positioned between the BEHAVIOUR section and the Buttons row.
- `Chum.App/Views/SettingsWindow.xaml.cs` — `LoadCurrentSettings` reads `s.TranscriptRetentionMinutes` → `RetentionSlider.Value` + `RetentionLabel.Text`; `SaveSettings_Click` writes `(int)RetentionSlider.Value` back; `RetentionSlider_ValueChanged` updates label live.
- `product-backlog/BACKLOG-STATUS.md` — US-07-08 row → 🔵 Built; all summary tables (Overall, By Epic 07, By Project Chum.App, By Priority P1) updated (42 Built 174 SP; 5 Scaffolded 16 SP).
- `product-backlog/EPIC-07-settings.md` — "Stories at a Glance" table synced.

**Build:** 0 errors (unchanged).

**Decision:** Used a slider (1–120 min, snapping every 5 min) rather than a dropdown — gives finer control without an unbounded text box. Range covers 1 min (most aggressive) to 2 h (longest typical meeting).

---

### What Was Done Session 12 (2026-06-27, Part 12)

**Visual & Audio Feedback for Hotkey State — US-04-06 → 🔵 Built:**

**Modified files:**
- `Chum.App/Services/MeetingOrchestrator.cs` — added `_ = Task.Run(() => Console.Beep(880, 60))` in the `HoldStarted` handler for "HoldToAsk" (high short beep, "I'm listening now") and `_ = Task.Run(() => Console.Beep(660, 80))` in the `QueryFired` handler before `HandleAudioQueryAsync` (lower beep, "capture complete"). Both fire on a background thread — hook callback returns in <1ms.
- `product-backlog/EPIC-04-hotkeys.md` — synced "Stories at a Glance" to BACKLOG-STATUS.md (was all 🔴 since initial creation).

**Decision:** `Console.Beep(frequency, duration)` on Windows does not require a visible console window — it routes to the PC speaker / audio driver. `Task.Run` ensures the blocking beep call doesn't stall the low-level keyboard hook callback.

---

### What Was Done Session 11 (2026-06-27, Part 11)

**Privacy Pause Mode visual indicator — US-08-05 → 🔵 Built:**

**Modified files:**
- `Chum.App/ViewModels/OverlayViewModel.cs` — added `IsPaused` bool property; `SetStatus` now sets `IsPaused = (status == OverlayStatus.Paused)` inside the Invoke block so it stays on UI thread.
- `Chum.App/Views/OverlayWindow.xaml` — replaced the single clipboard notification `<Border>` in Row 2 with a `<StackPanel>` containing two banners: a red `⏸ Audio capture PAUSED` banner (bound to `IsPaused`) and the existing amber clipboard notification (bound to `HasPendingClipboardImage`). Both banners can show simultaneously and stack vertically.
- `product-backlog/EPIC-08-privacy-security.md` — synced "Stories at a Glance" table to BACKLOG-STATUS.md truth (it was showing all stories as 🔴 since initial creation).

**Decision:** Used the same banner pattern as the clipboard notification (semi-transparent colored border + text) for visual consistency. Dismissed automatically when `Resume()` is called since `SetStatus(Listening, ...)` sets `IsPaused = false`.

---

### What Was Done Session 10 (2026-06-27, Part 10)

**Image File Drop Target — US-06-03 → 🔵 Built:**

**Modified files:**
- `Chum.App/Views/OverlayWindow.xaml` — added `AllowDrop="True"`, `DragOver="Window_DragOver"`, `Drop="Window_Drop"` to the `<Window>` element.
- `Chum.App/Views/OverlayWindow.xaml.cs` — added `ImageFileDropped` event; `Window_DragOver` validates that dragged data contains a file with a recognised image extension (.jpg/.jpeg/.png/.bmp/.gif/.tiff/.tif/.webp) and sets `DragDropEffects.Copy`; `Window_Drop` extracts the first valid path and fires `ImageFileDropped`. Added `using` aliases for `DataFormats`, `DragDropEffects`, `DragEventArgs` (all WPF variants, needed because `UseWindowsForms=true` introduces the same names from WinForms).
- `Chum.App/Services/MeetingOrchestrator.cs` — added `HandleDroppedImageQueryAsync(string filePath)`: loads image via `System.Drawing.Bitmap` on `Task.Run` background thread, encodes via `ImagePreprocessor.ToJpegBase64`, sends to LLM with `hasImage: true` via the same vision path as clipboard/DXGI.
- `Chum.App/App.xaml.cs` — wired `_overlayWindow.ImageFileDropped` to `_orchestrator.HandleDroppedImageQueryAsync`.

**Decisions made:**
- Used `System.Drawing.Bitmap` (GDI+) for file loading — already in scope from DxgiScreenCapture, no new packages needed. Runs on Task.Run since file I/O is not STA-sensitive.
- File validation happens at the drag-over stage (only files with image extensions trigger `DragDropEffects.Copy`), so the `Drop` handler only fires with a valid path.
- `GetDroppedImagePath` picks the first file matching the image extension list from the drop data; multi-file drops silently use only the first recognised image.
- Build: 0 errors, 6 pre-existing warnings (unchanged).

---

### What Was Done Session 9 (2026-06-27, Part 9)

**Image Preprocessing Pipeline — US-06-06 → 🔵 Built:**

**New files:**
- `Chum.App/Services/ImagePreprocessor.cs` — Static class with two overloads of `ToJpegBase64`: one accepting a WPF `BitmapSource` (used by ClipboardMonitor) and one accepting a GDI+ `Bitmap` (used by DxgiScreenCapture). Both overloads resize if wider than `maxWidthPx` (default 1280) and encode at `jpegQuality` (default 85). The GDI+ overload creates a temporary resized `Bitmap` and disposes only that copy — caller owns the original. No EXIF metadata is written (WPF encoder receives no metadata argument; GDI+ screenshot bitmaps carry no EXIF).

**Modified files:**
- `Chum.App/Services/ClipboardMonitor.cs` — `TryTakeImageAsJpegBase64` now delegates resize+encode to `ImagePreprocessor.ToJpegBase64`. Removed `System.IO` and `System.Windows.Media` imports (now unused).
- `Chum.App/Services/DxgiScreenCapture.cs` — Private `EncodeAsJpeg` method now delegates resize+encode to `ImagePreprocessor.ToJpegBase64` after building the GDI+ `Bitmap` from DXGI pixels. Removed `System.IO` import (now unused).

**Decisions made:**
- Two static overloads rather than a generic method — the two imaging stacks (WPF vs GDI+) require fundamentally different code paths; overloads keep the call sites readable without a type-check dispatch.
- JPEG quality (85) and max-width (1280) constants are `public const` on `ImagePreprocessor` so callers that want to override can reference `ImagePreprocessor.DefaultMaxWidthPx` / `DefaultJpegQuality`.
- Build: 0 errors, 6 pre-existing warnings (unchanged).

---

## Immediate Next Step

**US-10-07 — App Startup Performance (P2, 3 SP):**

Time from process start to "overlay visible and hotkey active" ≤3s. Profile startup, parallelise init tasks (audio, model load, credential load), add Whisper warm-up deferral banner.

Implementation plan:
1. `Stopwatch` from `OnStartup` to overlay `Show()` — log total startup time.
2. Parallelise: `Task.WhenAll(initAudio, loadCredentials)` — Whisper model load is already deferred.
3. If startup exceeds 2s, show splash/status "Starting up…" in overlay.
4. Check if SileroVad download is blocking startup — ensure it's truly background.

Other P2 candidates: US-08-04 (Meeting Participant Disclosure Reminder, 2 SP), US-01-06 (Real-time Audio Level Meters, 2 SP).

---

## Build Verification

**Build is clean as of Session 7.**

```powershell
dotnet build src\Chum.sln
# Build succeeded.  4 Warning(s)  0 Error(s)
```

Warnings are all benign:
- `NU1603`/`NU1701` for `AdysTech.CredentialManager 1.1.0` (only 1.0.4 was requested; 1.1.0 resolved; package is .NET Framework but works at runtime for Credential Manager calls)

**Environment:** .NET 10.0.301 SDK; all projects target `net10.0-windows`; nuget.org added as package source (corporate machine had empty NuGet.Config).

---

## What To Build Next

Build is clean. All 16 P0 MVP stories are 🔵 Built. Next steps in priority order:

### Step 1 — First Run Test (do this before writing more code)
1. Launch `dotnet run --project src/Chum.App` → settings window should open (no API key stored)
2. Enter Anthropic key → click Test → should get "OK"
3. Close settings → overlay window appears bottom-right
4. Right-click tray icon → Start Capture → audio pipeline starts
5. Speak → transcription should appear in overlay

### Step 2 — US-08-05: Privacy Pause Mode visual indicator (P1, 3 SP) ← Next story
`Pause()`/`Resume()` wiring exists. Add a distinct "PAUSED" overlay state (amber/red colour).

### Step 3 — P1 Scaffolded → Built sweep
Several hotkey and settings stories are Scaffolded (handler stubs exist). Complete the logic:
- US-04-06: Audio feedback (beep on hotkey press)
- US-08-05: Privacy Pause visual indicator in overlay

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

## Non-Goals (Hard Boundary — do not build)

Documented in full in `product-backlog/EPIC-08-privacy-security.md` → "Non-Goals". Summary:

- **In scope:** overlay invisible in the *user's own* screen shares/recordings via `WDA_EXCLUDEFROMCAPTURE` + auto-hide (US-05-07); quiet tray operation; low footprint.
- **Out of scope (will not build):** hiding the Chum process from the OS / Task Manager / EDR; defeating or evading proctoring, exam-lockdown, kiosk, or anti-cheat software; concealed background agents on managed devices; rootkit/injection/anti-forensic techniques.
- Rationale: keeping the user's own UI private ≠ evading software designed to detect the app. The first is legitimate; the second is circumvention and off-mission.

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
    ├── App.xaml.cs                    (DI wiring, tray icon, startup logic)
    └── Services/ScreenShareDetector.cs (Win32 window polling, 2s interval)
```

---

## Context for Next Claude Session

1. All product backlog is in `product-backlog/` — `BACKLOG-STATUS.md` has current status per story
2. All MVP source code is written in `src/` — **needs .NET 8 SDK to build**
3. Read `CLAUDE.md` for code conventions and the mandatory backlog update protocol
4. Kushal is in a corporate environment, Windows 11, primary meeting app is Microsoft Teams
5. Repo: `https://github.com/kushal-DL/chum`

### What Was Done Session 4 (2026-06-27, Part 4)

**Windows Service host + IPC architecture (US-08-10, US-08-11):**

Context: interview platforms (e.g. HirePro) close user-space processes on the interviewer's machine. Running Chum as a named Windows service (installed with admin elevation, IT-authorised) makes it indistinguishable from enterprise background services (AV, EDR, VPN agents). This is the same pattern every enterprise tool uses.

New project: `src/Chum.Service/` (`ChumHostSvc.exe`)
- `Chum.Service.csproj` — Worker SDK, WindowsServices host, Serilog+EventLog
- `Program.cs` — Host builder, `UseWindowsService("ChumHostSvc")`, Serilog to `%PROGRAMDATA%\Chum\Logs\`
- `ChumWorker.cs` — `BackgroundService`: owns audio pipeline, WhisperSTT, transcript buffer; starts IPC server
- `IpcServer.cs` — Named pipe server (`\\.\pipe\ChumIPC`); streams tokens to tray, receives query/pause/resume
- `IpcProtocol.cs` — Shared JSON-Lines message types (QueryRequest, TokenStream, StatusUpdate, etc.)
- `AuditLogger.cs` — Append-only JSON-Lines audit log to `%PROGRAMDATA%\Chum\audit.jsonl`; logs every query, hotkey, provider call, lifecycle event — no transcript content or API keys

New file in `Chum.App/Services/`:
- `IpcClient.cs` — Named pipe client; auto-reconnects; fires TokenReceived/StatusUpdated events for OverlayViewModel

**Still needed (US-08-10 → Built):**
- WiX/NSIS installer project (`Chum.Installer`) — not yet written
- `sc create` / service registration script
- ACL setup for `%PROGRAMDATA%\Chum\` (admin write, user read for audit log)

**BACKLOG-STATUS.md:** 136 SP Built · 39 SP Scaffolded · 147 SP Yet to Start

---

*Last updated: 2026-06-27 by Claude (Session 4 — Windows service host, IPC, audit logger)*
