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
| ✅ Done | 0 | 0 SP | 0% |
| 🔵 Built | 33 | 150 SP | 47% |
| 🟡 Scaffolded | 11 | 31 SP | 10% |
| 🔴 Yet to Start | 40 | 139 SP | 43% |
| **Total** | **84** | **320 SP** | |

> **All 16 P0 (MVP) stories are 🔵 Built — MVP is code-complete, pending first end-to-end test run.**

---

### By Epic

| Epic | Stories | Total SP | ✅ Done | 🔵 Built | 🟡 Scaffolded | 🔴 Yet to Start |
|------|---------|----------|---------|----------|--------------|-----------------|
| 01 · Core Audio Engine | 7 | 29 SP | — | 4 (21 SP) | 1 (3 SP) | 2 (5 SP) |
| 02 · Transcription & Context | 8 | 33 SP | — | 4 (21 SP) | 1 (2 SP) | 3 (10 SP) |
| 03 · LLM Integration | 8 | 30 SP | — | 3 (13 SP) | — | 5 (17 SP) |
| 04 · Hotkey & Trigger System | 7 | 26 SP | — | 2 (13 SP) | 5 (13 SP) | — |
| 05 · Overlay UI & Display | 9 | 42 SP | — | 7 (37 SP) | — | 2 (5 SP) |
| 06 · Screen Capture & Vision | 7 | 30 SP | — | 2 (11 SP) | — | 5 (19 SP) |
| 07 · Settings & Configuration | 10 | 27 SP | — | 6 (18 SP) | 2 (5 SP) | 2 (4 SP) |
| 08 · Privacy & Security | 11 | 36 SP | — | 5 (16 SP) | 2 (8 SP) | 4 (12 SP) |
| 09 · Platform Compatibility | 7 | 27 SP | — | — | — | 7 (27 SP) |
| 10 · Performance & Reliability | 10 | 40 SP | — | — | — | 10 (40 SP) |
| **Total** | **84** | **320 SP** | **0** | **33 (150 SP)** | **11 (31 SP)** | **40 (139 SP)** |

---

### By Project (App Layer)

Each story is attributed to the project where its primary implementation lives.

| Project | Primary Scope | Stories | Total SP | 🔵 Built | 🟡 Scaffolded | 🔴 Yet to Start |
|---------|--------------|---------|----------|----------|--------------|-----------------|
| Chum.Audio | Epic 01 + US-08-02 | 8 | 32 SP | 5 (24 SP) | 1 (3 SP) | 2 (5 SP) |
| Chum.Transcription | Epic 02 + US-08-03 | 9 | 36 SP | 5 (24 SP) | 1 (2 SP) | 3 (10 SP) |
| Chum.Llm | Epic 03 | 8 | 30 SP | 3 (13 SP) | — | 5 (17 SP) |
| Chum.App | Epics 04–07 + US-08-04…08 | 38 | 138 SP | 18 (82 SP) | 8 (21 SP) | 12 (35 SP) |
| Chum.Service | US-08-09, 08-10, 08-11 | 3 | 12 SP | 2 (7 SP) | 1 (5 SP) | — |
| Cross-cutting | US-08-01 + Epics 09–10 | 18 | 72 SP | — | — | 18 (72 SP) |
| **Total** | | **84** | **320 SP** | **33 (150 SP)** | **11 (31 SP)** | **40 (139 SP)** |

---

### By Priority

| Priority | Stories | Total SP | 🔵 Built | 🟡 Scaffolded | 🔴 Yet to Start |
|----------|---------|----------|----------|--------------|-----------------|
| P0 — MVP Blockers | 16 | 84 SP | 16 (84 SP) | — | — |
| P1 — High | 34 | 125 SP | 12 (52 SP) | 10 (28 SP) | 12 (45 SP) |
| P2 — Medium | 27 | 91 SP | 4 (12 SP) | 1 (3 SP) | 22 (76 SP) |
| P3 — Low | 7 | 20 SP | 1 (2 SP) | — | 6 (18 SP) |
| **Total** | **84** | **320 SP** | **33 (150 SP)** | **11 (31 SP)** | **40 (139 SP)** |

---

## Epic 01 — Core Audio Engine
*[Full spec: EPIC-01-audio-engine.md](EPIC-01-audio-engine.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-01-01 | Capture System Audio Loopback | P0 | 🔵 Built | 5 | LoopbackCapture.cs (WasapiLoopbackCapture) |
| US-01-02 | Capture Microphone Audio | P0 | 🔵 Built | 3 | MicCapture.cs (WasapiCapture) |
| US-01-03 | Audio Device Selection | P1 | 🟡 Scaffolded | 3 | DeviceId in AppSettings; no device-picker UI yet |
| US-01-04 | Voice Activity Detection (Silero VAD) | P0 | 🔵 Built | 8 | SileroVad.cs (ONNX, stateful LSTM, 512-sample chunks); EnergyVad kept as fallback when model not downloaded |
| US-01-05 | Audio Ring Buffer | P0 | 🔵 Built | 5 | AudioPipeline.cs: pre-buffer + Channel<AudioChunk> |
| US-01-06 | Real-time Audio Level Meters | P2 | 🔴 Yet to Start | 2 | |
| US-01-07 | Automatic Device Failover | P1 | 🔴 Yet to Start | 3 | |

**Epic 01 Total:** 29 SP · 0 Done · 21 Built · 3 Scaffolded · 5 Yet to Start

---

## Epic 02 — Transcription & Context Management
*[Full spec: EPIC-02-transcription.md](EPIC-02-transcription.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-02-01 | Local Whisper Transcription | P0 | 🔵 Built | 8 | WhisperSttEngine.cs (Whisper.net, model auto-download) |
| US-02-02 | Cloud STT Fallback (Azure) | P2 | 🔴 Yet to Start | 5 | |
| US-02-03 | Rolling Transcript Buffer | P0 | 🔵 Built | 5 | TranscriptBuffer.cs (LinkedList + retention window) |
| US-02-04 | Speaker Label Assignment (Me/Remote) | P1 | 🔵 Built | 3 | TranscriptSegment.SpeakerLabel (Mic→"Me", Loopback→"Remote") |
| US-02-05 | Language Detection & Multi-language | P2 | 🔴 Yet to Start | 3 | |
| US-02-06 | Transcript Cleanup & Formatting | P1 | 🟡 Scaffolded | 2 | Hallucination filter in WhisperSttEngine; no full cleanup pass |
| US-02-07 | Transcript Export | P3 | 🔴 Yet to Start | 2 | |
| US-02-08 | Context Window Preparation for LLM | P0 | 🔵 Built | 5 | ContextExtractor.cs (token-budget-aware, 30s recency priority) |

**Epic 02 Total:** 33 SP · 0 Done · 21 Built · 2 Scaffolded · 10 Yet to Start

---

## Epic 03 — LLM Integration & Response Engine
*[Full spec: EPIC-03-llm-integration.md](EPIC-03-llm-integration.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-03-01 | Anthropic Claude API Integration | P0 | 🔵 Built | 5 | AnthropicLlmProvider.cs (HttpClient + SSE streaming) |
| US-03-02 | OpenAI API Integration | P1 | 🔴 Yet to Start | 3 | |
| US-03-03 | Local LLM via Ollama | P2 | 🔴 Yet to Start | 5 | |
| US-03-04 | Meeting-Optimised System Prompt | P0 | 🔵 Built | 5 | PromptBuilder.cs (≤150 word rule, no preamble) |
| US-03-05 | Streaming Response Display | P0 | 🔵 Built | 3 | AppendResponseToken → OverlayViewModel → OverlayWindow |
| US-03-06 | Response History | P1 | 🔴 Yet to Start | 3 | |
| US-03-07 | Cost Estimation & Token Tracking | P2 | 🔴 Yet to Start | 3 | |
| US-03-08 | Prompt Templates Library | P2 | 🔴 Yet to Start | 3 | |

**Epic 03 Total:** 30 SP · 0 Done · 13 Built · 0 Scaffolded · 17 Yet to Start

---

## Epic 04 — Hotkey & Trigger System
*[Full spec: EPIC-04-hotkeys.md](EPIC-04-hotkeys.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-04-01 | Global Hold-to-Ask Hotkey | P0 | 🔵 Built | 8 | HotkeyService.cs (WH_KEYBOARD_LL, hold/release events, 300ms debounce) |
| US-04-02 | Screen Capture Hotkey | P1 | 🟡 Scaffolded | 3 | Registered in HotkeyService; capture logic (EPIC-06) not yet built |
| US-04-03 | Privacy Pause Hotkey | P1 | 🟡 Scaffolded | 2 | Registered; AudioPipeline.Pause() wired in MeetingOrchestrator |
| US-04-04 | Overlay Hide/Show Hotkey | P1 | 🟡 Scaffolded | 2 | Registered in HotkeyService; handler in MeetingOrchestrator |
| US-04-05 | Hotkey Configuration UI | P1 | 🔵 Built | 5 | TextBoxes in SettingsWindow.xaml; saved via SettingsService |
| US-04-06 | Visual & Audio Feedback for Hotkey State | P1 | 🟡 Scaffolded | 3 | Pulsing indicator in OverlayWindow.xaml; no audio beep yet |
| US-04-07 | Action Items Hotkey | P2 | 🟡 Scaffolded | 3 | Registered; summary handler stub in MeetingOrchestrator |

**Epic 04 Total:** 26 SP · 0 Done · 13 Built · 13 Scaffolded · 0 Yet to Start

---

## Epic 05 — Overlay UI & Display
*[Full spec: EPIC-05-overlay-ui.md](EPIC-05-overlay-ui.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-05-01 | Transparent Always-on-Top Window | P0 | 🔵 Built | 5 | OverlayWindow.xaml (AllowsTransparency, Topmost, WindowStyle=None) |
| US-05-02 | Response Display Panel | P0 | 🔵 Built | 8 | ScrollViewer + TextBlock bound to OverlayViewModel.ResponseText |
| US-05-03 | Live Transcript Strip | P2 | 🔵 Built | 5 | Collapsible Expander + ObservableCollection<string> TranscriptLines |
| US-05-04 | Status Indicators | P1 | 🔵 Built | 3 | StatusDot with StatusColor binding; OverlayStatus enum |
| US-05-05 | System Tray Integration | P1 | 🔵 Built | 5 | NotifyIcon in App.xaml.cs with context menu (Show/Settings/Start/Stop/Quit) |
| US-05-06 | Overlay Theme & Opacity Config | P2 | 🔵 Built | 3 | Opacity slider in SettingsWindow; live-bound in App.xaml.cs |
| US-05-07 | Auto-hide During Screen Share Detection | P1 | 🔵 Built | 8 | WDA_EXCLUDEFROMCAPTURE on overlay HWND + ScreenShareDetector polling |
| US-05-08 | Multi-monitor Support | P2 | 🔴 Yet to Start | 3 | |
| US-05-09 | Response Copy & Share | P2 | 🔴 Yet to Start | 2 | |

**Epic 05 Total:** 42 SP · 0 Done · 37 Built · 0 Scaffolded · 5 Yet to Start

---

## Epic 06 — Screen Capture & Visual Analysis
*[Full spec: EPIC-06-screen-capture.md](EPIC-06-screen-capture.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-06-01 | Primary Screen Capture (DXGI Duplication) | P1 | 🔵 Built | 8 | DxgiScreenCapture.cs — captures primary monitor at GPU output level; Teams call tiles appear black (WDA_EXCLUDEFROMCAPTURE applies to DXGI too on Win10 2004+); all other content captured correctly |
| US-06-02 | Clipboard Image Monitoring | P1 | 🔴 Yet to Start | 3 | |
| US-06-03 | Image File Drop Target | P2 | 🔴 Yet to Start | 3 | |
| US-06-04 | Region Selection / Snip Mode | P2 | 🔴 Yet to Start | 5 | |
| US-06-05 | UIA Text Extraction (Teams Captions) | P2 | 🔴 Yet to Start | 5 | |
| US-06-06 | Image Preprocessing Pipeline | P1 | 🔴 Yet to Start | 3 | |
| US-06-07 | Multimodal LLM Vision Request | P1 | 🔵 Built | 3 | HandleScreenCaptureQueryAsync in MeetingOrchestrator — DXGI frame → base64 JPEG → AnthropicLlmProvider with image + transcript context |

**Epic 06 Total:** 30 SP · 0 Done · 11 Built · 0 Scaffolded · 19 Yet to Start

---

## Epic 07 — Settings & Configuration
*[Full spec: EPIC-07-settings.md](EPIC-07-settings.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-07-01 | API Key Management (Credential Manager) | P0 | 🔵 Built | 5 | CredentialService.cs (AdysTech DPAPI) + SettingsWindow save/test |
| US-07-02 | Audio Device Configuration | P1 | 🟡 Scaffolded | 3 | DeviceId in AppSettings; no device-picker combo in UI yet |
| US-07-03 | LLM Provider & Model Selection | P1 | 🔵 Built | 3 | ModelCombo in SettingsWindow; persisted via SettingsService |
| US-07-04 | Transcription Configuration | P1 | 🔵 Built | 3 | WhisperModelCombo in SettingsWindow |
| US-07-05 | Hotkey Configuration | P1 | 🔵 Built | 3 | HoldToAsk/ScreenCap/PrivacyPause TextBoxes in SettingsWindow |
| US-07-06 | Overlay Appearance Settings | P2 | 🔵 Built | 2 | Opacity slider saved and applied live |
| US-07-07 | Startup & Run Behavior | P2 | 🔵 Built | 2 | StartWithWindows/StartCapturing checkboxes + App.xaml.cs logic |
| US-07-08 | Data Retention & Privacy Settings | P1 | 🟡 Scaffolded | 2 | TranscriptRetentionMinutes in AppSettings; no Settings UI section yet |
| US-07-09 | Settings Import & Export | P3 | 🔴 Yet to Start | 2 | |
| US-07-10 | About & Diagnostics Panel | P2 | 🔴 Yet to Start | 2 | |

**Epic 07 Total:** 27 SP · 0 Done · 18 Built · 5 Scaffolded · 4 Yet to Start

---

## Epic 08 — Privacy, Security & Compliance
*[Full spec: EPIC-08-privacy-security.md](EPIC-08-privacy-security.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-08-01 | Local-only Processing Mode | P1 | 🔴 Yet to Start | 5 | |
| US-08-02 | Audio Buffer Auto-Purge | P0 | 🔵 Built | 3 | Array.Clear() in AudioPipeline.FlushSegment() + WhisperSttEngine |
| US-08-03 | Transcript Retention Controls | P1 | 🔵 Built | 3 | TranscriptBuffer auto-eviction by retention window |
| US-08-04 | Meeting Participant Disclosure Reminder | P2 | 🔴 Yet to Start | 2 | |
| US-08-05 | Privacy Pause Mode | P1 | 🟡 Scaffolded | 3 | AudioPipeline.Pause()/Resume(); hotkey wired; no visual indicator yet |
| US-08-06 | Secure API Key Storage | P0 | 🔵 Built | 3 | CredentialService via Windows Credential Manager (DPAPI) |
| US-08-07 | Screen Capture Privacy Safeguards | P1 | 🔴 Yet to Start | 2 | |
| US-08-08 | Network Traffic Transparency | P2 | 🔴 Yet to Start | 3 | |
| US-08-09 | Audit Log (Enterprise) | P3 | 🔵 Built | 2 | AuditLogger.cs — JSON-Lines to %PROGRAMDATA%\Chum\audit.jsonl |
| US-08-10 | Windows Service Installer (Admin-Elevated) | P1 | 🟡 Scaffolded | 5 | Chum.Service project created; installer (WiX/NSIS) not yet written |
| US-08-11 | Process Identity — Run as Named System Service | P1 | 🔵 Built | 5 | ChumHostSvc worker + IPC server/client; tray↔service named pipe |

**Epic 08 Total:** 36 SP · 0 Done · 16 Built · 8 Scaffolded · 12 Yet to Start

---

## Epic 09 — Meeting Platform Compatibility
*[Full spec: EPIC-09-platform-compatibility.md](EPIC-09-platform-compatibility.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-09-01 | Meeting Platform Auto-Detection | P1 | 🔴 Yet to Start | 5 | Useful for prompt context + lifecycle; kept at P1 |
| US-09-02 | Teams-Specific Audio Device Handling | P2 | 🔴 Yet to Start | 3 | Downgraded: WASAPI loopback already platform-agnostic; this is an edge-case config fix |
| US-09-03 | Zoom Audio Device Handling | P2 | 🔴 Yet to Start | 3 | Downgraded: same reason as US-09-02 |
| US-09-04 | Screen Share Detection per Platform | P3 | 🔴 Yet to Start | 5 | Downgraded: ScreenShareDetector already polls generically; platform-specific detection adds little |
| US-09-05 | Teams Auto-Captions Integration | P2 | 🔴 Yet to Start | 5 | Useful fallback when Whisper struggles; Teams-only |
| US-09-06 | Meeting Start & End Lifecycle | P2 | 🔴 Yet to Start | 3 | Auto-start/stop capture on meeting open/close |
| US-09-07 | Platform Compatibility Testing Matrix | P3 | 🔴 Yet to Start | 3 | Downgraded: DXGI+WASAPI is platform-agnostic; matrix is a QA task not a feature |

**Epic 09 Total:** 27 SP · 0 Done · 0 Built · 0 Scaffolded · 27 Yet to Start

---

## Epic 10 — Performance & Reliability
*[Full spec: EPIC-10-performance.md](EPIC-10-performance.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-10-01 | Audio Pipeline Latency Profiling | P1 | 🔴 Yet to Start | 3 | |
| US-10-02 | CPU Usage Optimisation | P1 | 🔴 Yet to Start | 5 | |
| US-10-03 | GPU Acceleration for Whisper | P2 | 🔴 Yet to Start | 5 | |
| US-10-04 | Memory Management for Long Meetings | P1 | 🔴 Yet to Start | 5 | |
| US-10-05 | Graceful Error Recovery | P1 | 🔴 Yet to Start | 5 | |
| US-10-06 | End-to-End Latency Benchmark | P2 | 🔴 Yet to Start | 3 | |
| US-10-07 | App Startup Performance | P2 | 🔴 Yet to Start | 3 | |
| US-10-08 | Crash Reporting (Opt-in) | P3 | 🔴 Yet to Start | 3 | |
| US-10-09 | Auto-Update Mechanism | P2 | 🔴 Yet to Start | 5 | |
| US-10-10 | Low-Power Mode | P3 | 🔴 Yet to Start | 3 | |

**Epic 10 Total:** 40 SP · 0 Done · 0 Built · 0 Scaffolded · 40 Yet to Start

---

## Overall Progress

| Metric | Value |
|--------|-------|
| Total Stories | 84 |
| Total Story Points | 320 SP |
| P0 Stories | 16 stories · 84 SP · **all 🔵 Built** |
| P1 Stories | 34 stories · 125 SP |
| P2 Stories | 27 stories · 91 SP |
| P3 Stories | 7 stories · 20 SP |
| ✅ Done | 0 stories · 0 SP (0%) |
| 🔵 Built | 33 stories · 150 SP (47%) |
| 🟡 Scaffolded | 11 stories · 31 SP (10%) |
| 🔴 Yet to Start | 40 stories · 139 SP (43%) |

*Last updated: 2026-06-27 — Session 7: first successful build (0 errors); corrected SP counts throughout (Epic 08: 38→36 SP, overall: 310→320 SP, P0: 78→84 SP)*
