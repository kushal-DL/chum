# Chum — Master Backlog Status

> **This is the authoritative status tracker.** Update this file whenever code is written or a story status changes.  
> Also update the "Stories at a Glance" table in the corresponding epic file.

**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)  
**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low

---

## Epic 01 — Core Audio Engine
*[Full spec: EPIC-01-audio-engine.md](EPIC-01-audio-engine.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-01-01 | Capture System Audio Loopback | P0 | 🔴 Yet to Start | 5 | |
| US-01-02 | Capture Microphone Audio | P0 | 🔴 Yet to Start | 3 | |
| US-01-03 | Audio Device Selection | P1 | 🔴 Yet to Start | 3 | |
| US-01-04 | Voice Activity Detection (Silero VAD) | P0 | 🔴 Yet to Start | 8 | |
| US-01-05 | Audio Ring Buffer | P0 | 🔴 Yet to Start | 5 | |
| US-01-06 | Real-time Audio Level Meters | P2 | 🔴 Yet to Start | 2 | |
| US-01-07 | Automatic Device Failover | P1 | 🔴 Yet to Start | 3 | |

**Epic 01 Total:** 29 SP · 0 Done · 0 Built · 0 Scaffolded · 7 Yet to Start

---

## Epic 02 — Transcription & Context Management
*[Full spec: EPIC-02-transcription.md](EPIC-02-transcription.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-02-01 | Local Whisper Transcription | P0 | 🔴 Yet to Start | 8 | |
| US-02-02 | Cloud STT Fallback (Azure) | P2 | 🔴 Yet to Start | 5 | |
| US-02-03 | Rolling Transcript Buffer | P0 | 🔴 Yet to Start | 5 | |
| US-02-04 | Speaker Label Assignment (Me/Remote) | P1 | 🔴 Yet to Start | 3 | |
| US-02-05 | Language Detection & Multi-language | P2 | 🔴 Yet to Start | 3 | |
| US-02-06 | Transcript Cleanup & Formatting | P1 | 🔴 Yet to Start | 2 | |
| US-02-07 | Transcript Export | P3 | 🔴 Yet to Start | 2 | |
| US-02-08 | Context Window Preparation for LLM | P0 | 🔴 Yet to Start | 5 | |

**Epic 02 Total:** 33 SP · 0 Done · 0 Built · 0 Scaffolded · 8 Yet to Start

---

## Epic 03 — LLM Integration & Response Engine
*[Full spec: EPIC-03-llm-integration.md](EPIC-03-llm-integration.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-03-01 | Anthropic Claude API Integration | P0 | 🔴 Yet to Start | 5 | |
| US-03-02 | OpenAI API Integration | P1 | 🔴 Yet to Start | 3 | |
| US-03-03 | Local LLM via Ollama | P2 | 🔴 Yet to Start | 5 | |
| US-03-04 | Meeting-Optimised System Prompt | P0 | 🔴 Yet to Start | 5 | |
| US-03-05 | Streaming Response Display | P0 | 🔴 Yet to Start | 3 | |
| US-03-06 | Response History | P1 | 🔴 Yet to Start | 3 | |
| US-03-07 | Cost Estimation & Token Tracking | P2 | 🔴 Yet to Start | 3 | |
| US-03-08 | Prompt Templates Library | P2 | 🔴 Yet to Start | 3 | |

**Epic 03 Total:** 30 SP · 0 Done · 0 Built · 0 Scaffolded · 8 Yet to Start

---

## Epic 04 — Hotkey & Trigger System
*[Full spec: EPIC-04-hotkeys.md](EPIC-04-hotkeys.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-04-01 | Global Hold-to-Ask Hotkey | P0 | 🔴 Yet to Start | 8 | |
| US-04-02 | Screen Capture Hotkey | P1 | 🔴 Yet to Start | 3 | |
| US-04-03 | Privacy Pause Hotkey | P1 | 🔴 Yet to Start | 2 | |
| US-04-04 | Overlay Hide/Show Hotkey | P1 | 🔴 Yet to Start | 2 | |
| US-04-05 | Hotkey Configuration UI | P1 | 🔴 Yet to Start | 5 | |
| US-04-06 | Visual & Audio Feedback for Hotkey State | P1 | 🔴 Yet to Start | 3 | |
| US-04-07 | Action Items Hotkey | P2 | 🔴 Yet to Start | 3 | |

**Epic 04 Total:** 26 SP · 0 Done · 0 Built · 0 Scaffolded · 7 Yet to Start

---

## Epic 05 — Overlay UI & Display
*[Full spec: EPIC-05-overlay-ui.md](EPIC-05-overlay-ui.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-05-01 | Transparent Always-on-Top Window | P0 | 🔴 Yet to Start | 5 | |
| US-05-02 | Response Display Panel | P0 | 🔴 Yet to Start | 8 | |
| US-05-03 | Live Transcript Strip | P2 | 🔴 Yet to Start | 5 | |
| US-05-04 | Status Indicators | P1 | 🔴 Yet to Start | 3 | |
| US-05-05 | System Tray Integration | P1 | 🔴 Yet to Start | 5 | |
| US-05-06 | Overlay Theme & Opacity Config | P2 | 🔴 Yet to Start | 3 | |
| US-05-07 | Auto-hide During Screen Share Detection | P1 | 🔴 Yet to Start | 8 | |
| US-05-08 | Multi-monitor Support | P2 | 🔴 Yet to Start | 3 | |
| US-05-09 | Response Copy & Share | P2 | 🔴 Yet to Start | 2 | |

**Epic 05 Total:** 42 SP · 0 Done · 0 Built · 0 Scaffolded · 9 Yet to Start

---

## Epic 06 — Screen Capture & Visual Analysis
*[Full spec: EPIC-06-screen-capture.md](EPIC-06-screen-capture.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-06-01 | Primary Screen Capture (WGC API) | P1 | 🔴 Yet to Start | 8 | |
| US-06-02 | Clipboard Image Monitoring | P1 | 🔴 Yet to Start | 3 | |
| US-06-03 | Image File Drop Target | P2 | 🔴 Yet to Start | 3 | |
| US-06-04 | Region Selection / Snip Mode | P2 | 🔴 Yet to Start | 5 | |
| US-06-05 | UIA Text Extraction (Teams Captions) | P2 | 🔴 Yet to Start | 5 | |
| US-06-06 | Image Preprocessing Pipeline | P1 | 🔴 Yet to Start | 3 | |
| US-06-07 | Multimodal LLM Vision Request | P1 | 🔴 Yet to Start | 3 | |

**Epic 06 Total:** 30 SP · 0 Done · 0 Built · 0 Scaffolded · 7 Yet to Start

---

## Epic 07 — Settings & Configuration
*[Full spec: EPIC-07-settings.md](EPIC-07-settings.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-07-01 | API Key Management (Credential Manager) | P0 | 🔴 Yet to Start | 5 | |
| US-07-02 | Audio Device Configuration | P1 | 🔴 Yet to Start | 3 | |
| US-07-03 | LLM Provider & Model Selection | P1 | 🔴 Yet to Start | 3 | |
| US-07-04 | Transcription Configuration | P1 | 🔴 Yet to Start | 3 | |
| US-07-05 | Hotkey Configuration | P1 | 🔴 Yet to Start | 3 | |
| US-07-06 | Overlay Appearance Settings | P2 | 🔴 Yet to Start | 2 | |
| US-07-07 | Startup & Run Behavior | P2 | 🔴 Yet to Start | 2 | |
| US-07-08 | Data Retention & Privacy Settings | P1 | 🔴 Yet to Start | 2 | |
| US-07-09 | Settings Import & Export | P3 | 🔴 Yet to Start | 2 | |
| US-07-10 | About & Diagnostics Panel | P2 | 🔴 Yet to Start | 2 | |

**Epic 07 Total:** 27 SP · 0 Done · 0 Built · 0 Scaffolded · 10 Yet to Start

---

## Epic 08 — Privacy, Security & Compliance
*[Full spec: EPIC-08-privacy-security.md](EPIC-08-privacy-security.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-08-01 | Local-only Processing Mode | P1 | 🔴 Yet to Start | 5 | |
| US-08-02 | Audio Buffer Auto-Purge | P0 | 🔴 Yet to Start | 3 | |
| US-08-03 | Transcript Retention Controls | P1 | 🔴 Yet to Start | 3 | |
| US-08-04 | Meeting Participant Disclosure Reminder | P2 | 🔴 Yet to Start | 2 | |
| US-08-05 | Privacy Pause Mode | P1 | 🔴 Yet to Start | 3 | |
| US-08-06 | Secure API Key Storage | P0 | 🔴 Yet to Start | 3 | |
| US-08-07 | Screen Capture Privacy Safeguards | P1 | 🔴 Yet to Start | 2 | |
| US-08-08 | Network Traffic Transparency | P2 | 🔴 Yet to Start | 3 | |
| US-08-09 | Audit Log (Enterprise) | P3 | 🔴 Yet to Start | 2 | |

**Epic 08 Total:** 26 SP · 0 Done · 0 Built · 0 Scaffolded · 9 Yet to Start

---

## Epic 09 — Meeting Platform Compatibility
*[Full spec: EPIC-09-platform-compatibility.md](EPIC-09-platform-compatibility.md)*

| Story ID | Title | Priority | Status | SP | Notes |
|----------|-------|----------|--------|----|-------|
| US-09-01 | Meeting Platform Auto-Detection | P1 | 🔴 Yet to Start | 5 | |
| US-09-02 | Teams-Specific Audio Device Handling | P1 | 🔴 Yet to Start | 3 | |
| US-09-03 | Zoom Audio Device Handling | P1 | 🔴 Yet to Start | 3 | |
| US-09-04 | Screen Share Detection per Platform | P1 | 🔴 Yet to Start | 5 | |
| US-09-05 | Teams Auto-Captions Integration | P2 | 🔴 Yet to Start | 5 | |
| US-09-06 | Meeting Start & End Lifecycle | P2 | 🔴 Yet to Start | 3 | |
| US-09-07 | Platform Compatibility Testing Matrix | P2 | 🔴 Yet to Start | 3 | |

**Epic 09 Total:** 27 SP · 0 Done · 0 Built · 0 Scaffolded · 7 Yet to Start

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

**Epic 10 Total:** 40 SP · 0 Done · 0 Built · 0 Scaffolded · 10 Yet to Start

---

## Overall Progress

| Metric | Value |
|--------|-------|
| Total Story Points | 310 SP |
| P0 Stories | 16 stories · 78 SP |
| P1 Stories | 32 stories · 124 SP |
| P2 Stories | 22 stories · 87 SP |
| P3 Stories | 5 stories · 21 SP |
| Done | 0 SP (0%) |
| Built | 0 SP (0%) |
| Scaffolded | 0 SP (0%) |
| Yet to Start | 310 SP (100%) |

*Last updated: 2026-06-27 — Initial backlog creation*
