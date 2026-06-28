# Chum — Master Backlog Status

> **This is the authoritative status tracker.** Update this file whenever code is written or a story status changes.  
> Also update the "Stories at a Glance" table in the corresponding epic file.

**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)  
**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low

---

## At a Glance

### Overall

| Status | Stories | Story Points | % of Total SP |
|--------|---------|--------------|---------------|
| ✅ Done | 90 | 348 SP | 98% |
| ⚠️ Superseded | 1 | 8 SP | 2% |
| 🔵 Built | 0 | 0 SP | 0% |
| 🟡 Scaffolded | 0 | 0 SP | 0% |
| 🔴 Yet to Start | 0 | 0 SP | 0% |
| **Total** | **91** | **356 SP** | |

> **90 stories ✅ Done. US-10-11 (ONNX DirectML, 8 SP) was superseded in S59 by US-02-09 (sherpa streaming).**

> **All 18 P0 (MVP) stories ✅ Done** (added US-02-09 streaming STT, US-04-08 press-to-record query modes).
> **S59 STT rework: sherpa-onnx streaming replaces batch Whisper; press-to-record query modes; noise suppression.**

---

### By Epic

| Epic | Stories | Total SP | ✅ Done | ⚠️ Superseded | 🟡 Scaffolded | 🔴 Yet to Start |
|------|---------|----------|---------|--------------|--------------|-----------------|
| 01 · Core Audio Engine | 8 | 32 SP | 8 (32 SP) | — | — | — |
| 02 · Transcription & Context | 9 | 41 SP | 9 (41 SP) | — | — | — |
| 03 · LLM Integration | 10 | 38 SP | 10 (38 SP) | — | — | — |
| 04 · Hotkey & Trigger System | 8 | 31 SP | 8 (31 SP) | — | — | — |
| 05 · Overlay UI & Display | 9 | 42 SP | 9 (42 SP) | — | — | — |
| 06 · Screen Capture & Vision | 7 | 30 SP | 7 (30 SP) | — | — | — |
| 07 · Settings & Configuration | 11 | 31 SP | 11 (31 SP) | — | — | — |
| 08 · Privacy & Security | 11 | 36 SP | 11 (36 SP) | — | — | — |
| 09 · Platform Compatibility | 7 | 27 SP | 7 (27 SP) | — | — | — |
| 10 · Performance & Reliability | 11 | 48 SP | 10 (40 SP) | 1 (8 SP) | — | — |
| **Total** | **91** | **356 SP** | **90 (348 SP)** | **1 (8 SP)** | **0** | **0** |

---

### By Project (App Layer)

Each story is attributed to the project where its primary implementation lives.

| Project | Primary Scope | Stories | Total SP | ✅ Done | ⚠️ Superseded | 🔴 Yet to Start |
|---------|--------------|---------|----------|---------|--------------|-----------------|
| Chum.Audio | Epic 01 + US-08-02 | 9 | 35 SP | 9 (35 SP) | — | — |
| Chum.Transcription | Epic 02 + US-08-03 | 10 | 44 SP | 10 (44 SP) | — | — |
| Chum.Llm | Epic 03 | 8 | 30 SP | 8 (30 SP) | — | — |
| Chum.App | Epics 04–07 + US-08-04…08 + US-10-01/02/04/05 | 44 | 163 SP | 44 (163 SP) | — | — |
| Chum.Service + Chum.Installer | US-08-09, 08-10, 08-11 | 3 | 12 SP | 3 (12 SP) | — | — |
| Cross-cutting | US-08-01 + Epics 09–10 (excl. US-10-01/02/04/05) | 14 | 54 SP | 13 (46 SP) | 1 (8 SP) | — |
| **Total (approx. attribution)** | | **91** | **356 SP** | **90 (348 SP)** | **1 (8 SP)** | **0** |

---

### By Priority

| Priority | Stories | Total SP | ✅ Done | ⚠️ Superseded | 🔴 Yet to Start |
|----------|---------|----------|---------|--------------|-----------------|
| P0 — MVP Blockers | 18 | 97 SP | 18 (97 SP) | — | — |
| P1 — High | 36 | 136 SP | 35 (128 SP) | 1 (8 SP) | — |
| P2 — Medium | 27 | 91 SP | 27 (91 SP) | — | — |
| P3 — Low | 10 | 32 SP | 10 (32 SP) | — | — |
| **Total** | **91** | **356 SP** | **90 (348 SP)** | **1 (8 SP)** | **0** |

---

## Epic 01 — Core Audio Engine
*[Full spec: EPIC-01-audio-engine.md](EPIC-01-audio-engine.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-01-01 | Capture System Audio Loopback | P0 | ✅ Done (Built & Tested) | 5 | LoopbackCapture.cs (WasapiLoopbackCapture) |
| US-01-02 | Capture Microphone Audio | P0 | ✅ Done (Built & Tested) | 3 | MicCapture.cs (WasapiCapture) |
| US-01-03 | Audio Device Selection | P1 | ✅ Done (Built & Tested) | 3 | AudioDeviceEnumerator.cs (MMDeviceEnumerator); LoopbackCapture + MicCapture already accept deviceId |
| US-01-04 | Voice Activity Detection | P0 | ✅ Done (Built & Tested) | 8 | EnergyVad.cs (RMS + hysteresis, configurable threshold via VadThresholdDb). SileroVad removed in S59 — its ONNX runtime conflicted with sherpa-onnx; noise suppression covers noisy rooms instead |
| US-01-05 | Audio Ring Buffer | P0 | ✅ Done (Built & Tested) | 5 | AudioPipeline.cs: pre-buffer + Channel<AudioChunk>; + VAD-independent raw recording for press-to-record (S59) |
| US-01-06 | Real-time Audio Level Meters | P2 | ✅ Done (Built & Tested) | 2 | RMS dBFS computed in AudioPipeline; compact LB/MIC ProgressBar meters in overlay |
| US-01-07 | Automatic Device Failover | P1 | ✅ Done (Built & Tested) | 3 | Disconnected event on IAudioCapture/AudioPipeline; DeviceDisconnected event on MeetingOrchestrator; FallbackToDefaultAudioAsync in App |
| US-01-08 | Noise Suppression | P1 | ✅ Done (Built & Tested) | 3 | NoiseSuppressor.cs (S59): one-pole high-pass + adaptive noise gate; applied to recorded clips and rolling segments; EnableNoiseSuppression setting; 9 unit tests |

**Epic 01 Total:** 32 SP · 32 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 02 — Transcription & Context Management
*[Full spec: EPIC-02-transcription.md](EPIC-02-transcription.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-02-01 | Local Whisper Transcription | P0 | ✅ Done (Built & Tested) | 8 | WhisperSttEngine.cs (Whisper.net, model auto-download) — now the fallback engine; sherpa streaming (US-02-09) is the default |
| US-02-02 | Cloud STT Fallback (Azure) | P2 | ✅ Done (Built & Tested) | 5 | OpenAI Whisper API |
| US-02-03 | Rolling Transcript Buffer | P0 | ✅ Done (Built & Tested) | 5 | TranscriptBuffer.cs (LinkedList + retention window) |
| US-02-04 | Speaker Label Assignment (Me/Remote) | P1 | ✅ Done (Built & Tested) | 3 | TranscriptSegment.SpeakerLabel (Mic→"Me", Loopback→"Remote") |
| US-02-05 | Language Detection & Multi-language | P2 | ✅ Done (Built & Tested) | 3 | |
| US-02-06 | Transcript Cleanup & Formatting | P1 | ✅ Done (Built & Tested) | 2 | TranscriptCleaner.cs: noise tag stripping, music notation removal, word repetition reduction; expanded IsHallucination list |
| US-02-07 | Transcript Export | P3 | ✅ Done (Built & Tested) | 2 | "↓" button in overlay header + tray "Export Transcript…"; SaveFileDialog → File.WriteAllText; delegates to MeetingOrchestrator.GetTranscriptExportText() |
| US-02-08 | Context Window Preparation for LLM | P0 | ✅ Done (Built & Tested) | 5 | ContextExtractor.cs (token-budget-aware, 30s recency priority); now opt-in per query via IncludeTranscriptContext (S59) |
| US-02-09 | Streaming STT (sherpa-onnx Zipformer) | P0 | ✅ Done (Built & Tested) | 8 | SherpaOnnxSttEngine.cs (S59): streaming Zipformer transducer, RTF ~0.1 on CPU (~10× real-time), no Whisper-style noise hallucinations; replaces ONNX DirectML Whisper; default STT engine |

**Epic 02 Total:** 41 SP · 41 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 03 — LLM Integration & Response Engine
*[Full spec: EPIC-03-llm-integration.md](EPIC-03-llm-integration.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-03-01 | Anthropic Claude API Integration | P0 | ✅ Done (Built & Tested) | 5 | AnthropicLlmProvider.cs (HttpClient + SSE streaming) |
| US-03-02 | OpenAI API Integration | P1 | ✅ Done (Built & Tested) | 3 | OpenAiLlmProvider.cs (SSE streaming, vision via image_url); provider inferred from model name in App.BuildLlmProvider |
| US-03-03 | Local LLM via Ollama | P2 | ✅ Done (Built & Tested) | 5 | OllamaLlmProvider.cs — implemented as part of US-08-01 (local-only mode); NDJSON streaming, vision via images array |
| US-03-04 | Meeting-Optimised System Prompt | P0 | ✅ Done (Built & Tested) | 5 | PromptBuilder.cs (≤150 word rule, no preamble) |
| US-03-05 | Streaming Response Display | P0 | ✅ Done (Built & Tested) | 3 | AppendResponseToken → OverlayViewModel → OverlayWindow |
| US-03-06 | Response History | P1 | ✅ Done (Built & Tested) | 3 | OverlayViewModel: _history ring buffer (max 20), NavigateBack/Forward, HasHistory/CanGoBack/CanGoForward; OverlayWindow: ◀/▶ nav strip in Row 2 |
| US-03-07 | Cost Estimation & Token Tracking | P2 | ✅ Done (Built & Tested) | 3 | UsageRecorded event on ILlmProvider; SessionCostTracker; cost hint in overlay status bar + About dialog; spend threshold warning |
| US-03-08 | Prompt Templates Library | P2 | ✅ Done (Built & Tested) | 3 | PromptTemplate record; TemplateService (JSON storage); 5 built-ins; Ctrl+Alt+1–5 hotkeys; settings CRUD; passed to BuildSystemPrompt |
| US-03-09 | OpenAI-Compatible Endpoint Support | P0 | ✅ Done (Built & Tested) | 4 | OpenAiLlmProvider accepts optional baseUrl; NVIDIA NIM at integrate.api.nvidia.com/v1 and any custom endpoint supported |
| US-03-10 | Document Context in LLM Requests | P1 | ✅ Done (Built & Tested) | 4 | DocumentContextService loads PDF/TXT/MD; builds formatted context block injected into LLM system prompt |

**Epic 03 Total:** 38 SP · 38 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 04 — Hotkey & Trigger System
*[Full spec: EPIC-04-hotkeys.md](EPIC-04-hotkeys.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-04-01 | Global Hold-to-Ask Hotkey | P0 | ✅ Done (Built & Tested) | 8 | HotkeyService.cs (WH_KEYBOARD_LL, hold/release events, 300ms debounce) |
| US-04-02 | Screen Capture Hotkey | P1 | ✅ Done (Built & Tested) | 3 | HotkeyTapped "ScreenCapture" → HandleScreenCaptureQueryAsync (DXGI + clipboard + drop all wired) |
| US-04-03 | Privacy Pause Hotkey | P1 | ✅ Done (Built & Tested) | 2 | HotkeyTapped "PrivacyPause" → TogglePause() → AudioPipeline.Pause/Resume + IsPaused banner |
| US-04-04 | Overlay Hide/Show Hotkey | P1 | ✅ Done (Built & Tested) | 2 | HotkeyTapped "HideOverlay" → OverlayViewModel.ToggleVisibility() |
| US-04-05 | Hotkey Configuration UI | P1 | ✅ Done (Built & Tested) | 5 | TextBoxes in SettingsWindow.xaml; saved via SettingsService |
| US-04-06 | Visual & Audio Feedback for Hotkey State | P1 | ✅ Done (Built & Tested) | 3 | Pulsing green ring (IsListening) + audio beeps: 880 Hz/60ms on hold start, 660 Hz/80ms on release — both on Task.Run to keep hook callback <1ms |
| US-04-07 | Action Items Hotkey | P2 | ✅ Done (Built & Tested) | 3 | Ctrl+Alt+A registered; HandleActionItemsQueryAsync sends full transcript to LLM with "extract action items" prompt |
| US-04-08 | Press-to-Record Query Modes | P0 | ✅ Done (Built & Tested) | 5 | S59: toggle records raw audio (VAD-independent); on stop, QueryMode picks LocalTranscribeToLlm (sherpa → text → LLM, shows "Q:/A:") or AudioToLlm (WAV → multimodal LLM); rolling transcript no longer auto-sent |

**Epic 04 Total:** 31 SP · 31 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 05 — Overlay UI & Display
*[Full spec: EPIC-05-overlay-ui.md](EPIC-05-overlay-ui.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-05-01 | Transparent Always-on-Top Window | P0 | ✅ Done (Built & Tested) | 5 | OverlayWindow.xaml (AllowsTransparency, Topmost, WindowStyle=None) |
| US-05-02 | Response Display Panel | P0 | ✅ Done (Built & Tested) | 8 | ScrollViewer + TextBlock bound to OverlayViewModel.ResponseText |
| US-05-03 | Live Transcript Strip | P2 | ✅ Done (Built & Tested) | 5 | Collapsible Expander + ObservableCollection<string> TranscriptLines |
| US-05-04 | Status Indicators | P1 | ✅ Done (Built & Tested) | 3 | StatusDot with StatusColor binding; OverlayStatus enum |
| US-05-05 | System Tray Integration | P1 | ✅ Done (Built & Tested) | 5 | NotifyIcon in App.xaml.cs with context menu (Show/Settings/Start/Stop/Quit) |
| US-05-06 | Overlay Theme & Opacity Config | P2 | ✅ Done (Built & Tested) | 3 | Opacity slider in SettingsWindow; live-bound in App.xaml.cs |
| US-05-07 | Auto-hide During Screen Share Detection | P1 | ✅ Done (Built & Tested) | 8 | WDA_EXCLUDEFROMCAPTURE on overlay HWND + ScreenShareDetector polling |
| US-05-08 | Multi-monitor Support | P2 | ✅ Done (Built & Tested) | 3 | |
| US-05-09 | Response Copy & Share | P2 | ✅ Done (Built & Tested) | 2 | "⧉" Copy button in overlay header; CopyResponse_Click copies ResponseText to clipboard |

**Epic 05 Total:** 42 SP · 42 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 06 — Screen Capture & Visual Analysis
*[Full spec: EPIC-06-screen-capture.md](EPIC-06-screen-capture.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-06-01 | Primary Screen Capture (DXGI Duplication) | P1 | ✅ Done (Built & Tested) | 8 | DxgiScreenCapture.cs — captures primary monitor at GPU output level; Teams call tiles appear black (WDA_EXCLUDEFROMCAPTURE applies to DXGI too on Win10 2004+); all other content captured correctly |
| US-06-02 | Clipboard Image Monitoring | P1 | ✅ Done (Built & Tested) | 3 | ClipboardMonitor.cs — WM_CLIPBOARDUPDATE via HWND_MESSAGE; JPEG encoding; notification banner in overlay; hotkey path prefers clipboard over DXGI |
| US-06-03 | Image File Drop Target | P2 | ✅ Done (Built & Tested) | 3 | AllowDrop on OverlayWindow; ImageFileDropped event; HandleDroppedImageQueryAsync in MeetingOrchestrator — GDI+ Bitmap load on Task.Run, ImagePreprocessor.ToJpegBase64, same LLM vision path as clipboard |
| US-06-04 | Region Selection / Snip Mode | P2 | ✅ Done (Built & Tested) | 5 | SnipOverlayWindow (full-screen transparent, Canvas rectangle selection); DxgiScreenCapture.CaptureRegionAsJpegBase64 (crops BGRA frame); MeetingOrchestrator.HandleSnipCaptureAsync; Ctrl+Alt+Shift+S hotkey |
| US-06-05 | UIA Text Extraction (Teams Captions) | P2 | ✅ Done (Built & Tested) | 5 | TeamsCaptionsReader polls Teams window via UIAutomation (AutomationId/ClassName/Name "caption" search, depth-8 tree walk, 500ms interval); CaptionLineReceived feeds transcript buffer |
| US-06-06 | Image Preprocessing Pipeline | P1 | ✅ Done (Built & Tested) | 3 | ImagePreprocessor.cs — shared WPF BitmapSource + GDI+ Bitmap → JPEG base64 encoder; replaces inline logic in ClipboardMonitor + DxgiScreenCapture |
| US-06-07 | Multimodal LLM Vision Request | P1 | ✅ Done (Built & Tested) | 3 | HandleScreenCaptureQueryAsync in MeetingOrchestrator — DXGI frame → base64 JPEG → AnthropicLlmProvider with image + transcript context |

**Epic 06 Total:** 30 SP · 30 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 07 — Settings & Configuration
*[Full spec: EPIC-07-settings.md](EPIC-07-settings.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-07-01 | API Key Management (Credential Manager) | P0 | ✅ Done (Built & Tested) | 5 | CredentialService.cs (AdysTech DPAPI) + SettingsWindow save/test |
| US-07-02 | Audio Device Configuration | P1 | ✅ Done (Built & Tested) | 3 | LoopbackDeviceCombo + MicDeviceCombo in SettingsWindow; live apply via MeetingOrchestrator.ReplaceAudio |
| US-07-03 | LLM Provider & Model Selection | P1 | ✅ Done (Built & Tested) | 3 | ModelCombo in SettingsWindow; persisted via SettingsService |
| US-07-04 | Transcription Configuration | P1 | ✅ Done (Built & Tested) | 3 | WhisperModelCombo in SettingsWindow |
| US-07-05 | Hotkey Configuration | P1 | ✅ Done (Built & Tested) | 3 | HoldToAsk/ScreenCap/PrivacyPause TextBoxes in SettingsWindow |
| US-07-06 | Overlay Appearance Settings | P2 | ✅ Done (Built & Tested) | 2 | Opacity slider saved and applied live |
| US-07-07 | Startup & Run Behavior | P2 | ✅ Done (Built & Tested) | 2 | StartWithWindows/StartCapturing checkboxes + App.xaml.cs logic |
| US-07-08 | Data Retention & Privacy Settings | P1 | ✅ Done (Built & Tested) | 2 | PRIVACY section in SettingsWindow: retention slider (1–120 min, snaps to 5), live label, saved on "Save Settings" |
| US-07-09 | Settings Import & Export | P3 | ✅ Done (Built & Tested) | 2 | "Export Settings…" + "Import Settings…" buttons in SettingsWindow BACKUP section; JSON backup contains settings + templates; import writes files then calls Settings.Load() + LoadCurrentSettings() |
| US-07-10 | About & Diagnostics Panel | P2 | ✅ Done (Built & Tested) | 2 | |
| US-07-11 | Flexible Provider Settings UI | P0 | ✅ Done (Built & Tested) | 4 | Provider dropdown (Anthropic/OpenAI/NVIDIA/Ollama/Custom) + API URL, key, model, test button; dynamic panel visibility per provider |

**Epic 07 Total:** 31 SP · 31 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 08 — Privacy, Security & Compliance
*[Full spec: EPIC-08-privacy-security.md](EPIC-08-privacy-security.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-08-01 | Local-only Processing Mode | P1 | ✅ Done (Built & Tested) | 5 | OllamaLlmProvider.cs (NDJSON streaming); LocalOnlyMode setting; bypasses cloud key check |
| US-08-02 | Audio Buffer Auto-Purge | P0 | ✅ Done (Built & Tested) | 3 | Array.Clear() in AudioPipeline.FlushSegment() + WhisperSttEngine |
| US-08-03 | Transcript Retention Controls | P1 | ✅ Done (Built & Tested) | 3 | TranscriptBuffer auto-eviction by retention window |
| US-08-04 | Meeting Participant Disclosure Reminder | P2 | ✅ Done (Built & Tested) | 2 | Amber banner on capture start; "Got it" auto-clears and saves setting |
| US-08-05 | Privacy Pause Mode | P1 | ✅ Done (Built & Tested) | 3 | IsPaused bool on OverlayViewModel; red "⏸ Audio capture PAUSED" banner in overlay (same row as clipboard notification, stacked via StackPanel); dismisses automatically on Resume() |
| US-08-06 | Secure API Key Storage | P0 | ✅ Done (Built & Tested) | 3 | CredentialService via Windows Credential Manager (DPAPI) |
| US-08-07 | Screen Capture Privacy Safeguards | P1 | ✅ Done (Built & Tested) | 2 | ConfirmScreenCapture setting; TryHandleScreenCaptureAsync two-press confirmation with 5s timer + overlay banner |
| US-08-08 | Network Traffic Transparency | P2 | ✅ Done (Built & Tested) | 3 | Status bar shows provider+model during each query; Serilog logs provider/model/type for every LLM call |
| US-08-09 | Audit Log (Enterprise) | P3 | ✅ Done (Built & Tested) | 2 | AuditLogger.cs — JSON-Lines to %PROGRAMDATA%\Chum\audit.jsonl |
| US-08-10 | Windows Service Installer (Admin-Elevated) | P1 | ✅ Done (Built & Tested) | 5 | Chum.Installer/ WiX v4 project (Package.wxs, License.rtf); scripts/Install-Chum.ps1 + Uninstall-Chum.ps1 PowerShell fallback |
| US-08-11 | Process Identity — Run as Named System Service | P1 | ✅ Done (Built & Tested) | 5 | ChumHostSvc worker + IPC server/client; tray↔service named pipe |

**Epic 08 Total:** 36 SP · 36 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 09 — Meeting Platform Compatibility
*[Full spec: EPIC-09-platform-compatibility.md](EPIC-09-platform-compatibility.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-09-01 | Meeting Platform Auto-Detection | P1 | ✅ Done (Built & Tested) | 5 | MeetingPlatformDetector.cs: polls processes 5s, detects Teams/Zoom/Meet/WebEx; wired in MeetingOrchestrator; FriendlyName passed to PromptBuilder |
| US-09-02 | Teams-Specific Audio Device Handling | P2 | ✅ Done (Built & Tested) | 3 | AudioSessionHelper.TryFindProcessRenderDevice enumerates WASAPI sessions by PID; MeetingOrchestrator fires AudioDeviceMismatchDetected; overlay shows Switch/Keep banner |
| US-09-03 | Zoom Audio Device Handling | P2 | ✅ Done (Built & Tested) | 3 | AudioSessionHelper.TryFindRenderDeviceByName("Zoom Audio Device") as primary check; PID-session fallback if virtual device absent |
| US-09-04 | Screen Share Detection per Platform | P3 | ✅ Done (Built & Tested) | 5 | ZPControlBar added; 2s restore delay; IsSharing property for delayed check |
| US-09-05 | Teams Auto-Captions Integration | P2 | ✅ Done (Built & Tested) | 5 | UseTeamsCaptions setting (default off) + Settings UI toggle; MeetingOrchestrator starts/stops TeamsCaptionsReader on platform change; caption text merged into transcript buffer |
| US-09-06 | Meeting Start & End Lifecycle | P2 | ✅ Done (Built & Tested) | 3 | Auto-start/stop capture on meeting open/close |
| US-09-07 | Platform Compatibility Testing Matrix | P3 | ✅ Done (Built & Tested) | 3 | PLATFORM-COMPAT-TEST-MATRIX.md — 10-section test matrix, regression checklist, bug labels |

**Epic 09 Total:** 27 SP · 27 Done · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Epic 10 — Performance & Reliability
*[Full spec: EPIC-10-performance.md](EPIC-10-performance.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-10-01 | Audio Pipeline Latency Profiling | P1 | ✅ Done (Built & Tested) | 3 | PipelineLatencyTracker.cs: rolling 1000-segment buffer, p50/p90/p99; Stopwatch around TranscribeAsync; 5-min percentile log; slow alert (>15s × 3 consecutive) shown in overlay |
| US-10-02 | CPU Usage Optimisation | P2 | ✅ Done (Built & Tested) | 5 | SileroVad: IntraOpNumThreads=2; Silero download deferred 5s post-launch; OverlayWindow root Grid: RenderOptions.BitmapScalingMode=NearestNeighbor |
| US-10-03 | GPU Acceleration for Whisper | P2 | ✅ Done (Built & Tested) | 5 | DXGI adapter enumeration detects dedicated GPU ≥500MB VRAM in App.xaml.cs; WhisperSttEngine.TryCudaDetect() probes nvcuda.dll for CUDA driver presence; AccelerationMode property reports CPU/GPU status; About panel shows "Whisper acceleration"; GPU runtime enabled by swapping to Whisper.net.Runtime.Cuda NuGet package |
| US-10-04 | Memory Management for Long Meetings | P1 | ✅ Done (Built & Tested) | 5 | Response history capped 20→10; periodic GC.Collect(Gen2) every 10 min in MeetingOrchestrator with WorkingSet logging; transcript/audio buffers already bounded |
| US-10-05 | Graceful Error Recovery | P1 | ✅ Done (Built & Tested) | 5 | Global exception handlers (AppDomain/Dispatcher/UnobservedTask); LLM retry with backoff (1s/2s/4s, 3 attempts); transcription loop auto-restart; emergency transcript export on crash |
| US-10-06 | End-to-End Latency Benchmark | P2 | ✅ Done (Built & Tested) | 3 | LLM first-token latency tracking |
| US-10-07 | App Startup Performance | P2 | ✅ Done (Built & Tested) | 3 | Stopwatch from OnStartup; overlay shown early; VAD ONNX loads parallelised |
| US-10-08 | Crash Reporting (Opt-in) | P3 | ✅ Done (Built & Tested) | 3 | CrashReporter.cs — local JSON dump, opt-in toggle in settings |
| US-10-09 | Auto-Update Mechanism | P2 | ✅ Done (Built & Tested) | 5 | UpdateChecker.cs — GitHub Releases API + SHA256 + tray balloon |
| US-10-10 | Low-Power Mode | P3 | ✅ Done (Built & Tested) | 3 | PowerMonitor.cs + SetLowPowerMode() + settings checkboxes; overlay badge |
| US-10-11 | ONNX DirectML Transcription (iGPU) | P1 | ⚠️ Superseded | 8 | S59: REMOVED. OnnxWhisperSttEngine deleted — its onnxruntime (DirectML 1.20.1) conflicted with sherpa-onnx's bundled runtime (1.24.4), and it caused a 7-min lag (batch Whisper, no KV cache). Replaced by US-02-09 (sherpa streaming, CPU, ~10× real-time). MelSpectrogram.cs retained + still tested |

**Epic 10 Total:** 48 SP · 10 Done · 1 Superseded (US-10-11) · 0 Built · 0 Scaffolded · 0 Yet to Start

---

## Overall Progress

| Metric | Value |
|--------|-------|
| Total Stories | 91 (90 active + 1 superseded) |
| Total Story Points | 356 SP |
| P0 Stories | 18 stories · 97 SP · **all ✅ Done** |
| P1 Stories | 36 stories · 136 SP · **35 ✅ Done, 1 superseded (US-10-11)** |
| P2 Stories | 27 stories · 91 SP · **all ✅ Done** |
| P3 Stories | 10 stories · 32 SP · **all ✅ Done** |
| ✅ Done | 90 stories · 348 SP (98%) |
| ⚠️ Superseded | 1 story · 8 SP (US-10-11 → US-02-09) |
| 🔵 Built | 0 stories · 0 SP (0%) |
| 🟡 Scaffolded | 0 stories · 0 SP (0%) |
| 🔴 Yet to Start | 0 stories · 0 SP (0%) |

*Last updated: 2026-06-28 — Session 59: STT reworked to sherpa-onnx streaming + press-to-record query modes + noise suppression; 195 unit tests passing; added US-01-08/US-02-09/US-04-08, superseded US-10-11*
