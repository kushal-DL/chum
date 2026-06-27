# Chum — AI Meeting Co-Pilot: Product Backlog

## App Name Suggestions

| Name | Tagline | Reasoning |
|------|---------|-----------|
| **Chum** *(recommended — your repo name)* | Your smartest meeting buddy | "Chum" means close companion. Your AI pal in the trenches of every call. Short, memorable, human. |
| **Whispr** | The answer in your ear | Evokes whispered guidance during a call. Clean, modern spelling. |
| **ARIA** | Adaptive Real-time Intelligent Assistant | Professional acronym; sounds like the word *aria* (a musical highlight moment). |
| **Sidekick** | Every hero needs one | Friendly, approachable. Positions AI as support, not replacement. |
| **Prompter** | Your invisible stage hand | References the theatrical prompter who feeds lines to actors from the wings. |
| **Sage** | Wisdom on demand | Short, premium feel. |
| **Brief** | Stay briefed, always | Doubles as noun + verb; implies conciseness. |

> **Recommendation: Chum** — it is already your repo name, it is warm and human, and "your meeting chum" is instantly understood.

---

## Vision

Chum is a real-time AI co-pilot for professionals in video calls. It listens passively to all meeting audio, maintains a rolling transcript, and surfaces LLM-powered answers when you hold a hotkey — staying invisible until you need it. A second hotkey lets you capture your screen (or a whiteboard photo) and get visual analysis instantly. Chum works around platform restrictions like Teams' screenshot blocking through a layered capture strategy.

---

## Recommended Technology Stack

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| Platform | .NET 8 / C# | Best Windows native API access; strong audio ecosystem |
| UI | WPF + Win32 interop | Transparent always-on-top windows; system tray; hardware accelerated |
| Audio Capture | NAudio (WASAPI) | Mature library; supports loopback and mic capture in shared mode |
| Voice Activity Detection | Silero VAD (ONNX Runtime) | Local, MIT license, ~1ms latency, high accuracy |
| Speech-to-Text | Whisper.NET (whisper.cpp bindings) | Runs fully local; no API cost; GPU-accelerated via CUDA/DirectML |
| LLM | Anthropic SDK (Claude) + OpenAI SDK | Pluggable providers; Claude for long context (200K tokens) |
| Vision / Multimodal | Claude 3.5 Sonnet / GPT-4o | Whiteboard OCR and screen analysis |
| Screen Capture | Windows.Graphics.Capture API | Most compatible modern Windows capture API |
| Global Hotkeys | Win32 `RegisterHotKey` + Low-Level KeyboardHook | Reliable cross-application hotkey handling |
| Secure Storage | Windows Credential Manager (DPAPI) | OS-managed encrypted secrets; no plaintext on disk |
| Logging | Serilog + rolling file sink | Structured diagnostics without performance hit |
| Packaging | .NET single-file publish / MSIX | Easy distribution; self-contained or store-deployable |

---

## High-Level Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                        CHUM APPLICATION                      │
│                                                              │
│  ┌─────────────────┐      ┌──────────────────┐              │
│  │  Audio Engine   │─────▶│  STT Engine      │              │
│  │  (WASAPI)       │      │  (Whisper local) │              │
│  │  · Loopback     │      │                  │              │
│  │  · Microphone   │      └────────┬─────────┘              │
│  │  · VAD filter   │               │                        │
│  └─────────────────┘      ┌────────▼─────────┐              │
│                            │  Context Buffer  │              │
│                            │  (Rolling ~5min) │              │
│                            └────────┬─────────┘              │
│                                     │                        │
│  ┌─────────────────┐      ┌────────▼─────────┐              │
│  │  Hotkey Layer   │─────▶│  LLM Engine      │              │
│  │  · Hold-to-ask  │      │  · Claude / GPT  │              │
│  │  · Screen cap   │      │  · Streaming     │              │
│  │  · Privacy pause│      └────────┬─────────┘              │
│  └─────────────────┘               │                        │
│                            ┌────────▼─────────┐              │
│  ┌─────────────────┐       │  Overlay UI      │              │
│  │  Screen Capture │──────▶│  · Response panel│              │
│  │  · WGC API      │       │  · Transcript    │              │
│  │  · Clipboard    │       │  · System tray   │              │
│  │  · A11y API     │       └──────────────────┘              │
│  └─────────────────┘                                         │
└──────────────────────────────────────────────────────────────┘
```

---

## Epics

| # | Epic | Description | Priority | File |
|---|------|-------------|----------|------|
| 01 | Core Audio Engine | WASAPI loopback + mic capture, VAD, buffer management | Must-Have | [EPIC-01-audio-engine.md](EPIC-01-audio-engine.md) |
| 02 | Transcription & Context | Local Whisper STT, rolling transcript, context management | Must-Have | [EPIC-02-transcription.md](EPIC-02-transcription.md) |
| 03 | LLM Integration | Claude/GPT integration, prompt engineering, streaming | Must-Have | [EPIC-03-llm-integration.md](EPIC-03-llm-integration.md) |
| 04 | Hotkey & Trigger System | Global hotkeys, push-to-ask trigger, visual feedback | Must-Have | [EPIC-04-hotkeys.md](EPIC-04-hotkeys.md) |
| 05 | Overlay UI | Transparent always-on-top window, response display | Must-Have | [EPIC-05-overlay-ui.md](EPIC-05-overlay-ui.md) |
| 06 | Screen Capture & Vision | Screen cap, Teams DRM workaround, multimodal LLM | High | [EPIC-06-screen-capture.md](EPIC-06-screen-capture.md) |
| 07 | Settings & Configuration | API keys, device selection, hotkey config, preferences | High | [EPIC-07-settings.md](EPIC-07-settings.md) |
| 08 | Privacy & Security | Local mode, data retention, consent, secure storage | High | [EPIC-08-privacy-security.md](EPIC-08-privacy-security.md) |
| 09 | Platform Compatibility | Teams, Google Meet, Zoom audio routing validation | Medium | [EPIC-09-platform-compatibility.md](EPIC-09-platform-compatibility.md) |
| 10 | Performance & Reliability | Latency, CPU/memory, crash recovery, auto-update | Medium | [EPIC-10-performance.md](EPIC-10-performance.md) |

---

## Key Technical Challenges Summary

| Challenge | Description | See |
|-----------|-------------|-----|
| Teams screenshot DRM | Teams calls `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, blacking out all screen captures | EPIC-06 |
| WASAPI exclusive mode | Some drivers lock audio in exclusive mode, preventing loopback capture | EPIC-01 |
| Transcription latency | Real-time Whisper has 1–3s lag; chunked VAD-gated processing is required | EPIC-02 |
| Context window cost | 5-min rolling transcript = ~10,000 tokens; smart summarization required | EPIC-03 |
| Global hotkey conflicts | Teams/Zoom/Windows use common combos; configurable bindings essential | EPIC-04 |
| Overlay visible during screen share | When you share your screen, others see the overlay; needs quick hide | EPIC-05 |
| Bluetooth audio degradation | HSP/HFP mic profile drops to 8 kHz, hurting transcription quality | EPIC-01 |
| Corporate VPN audio interception | VPN virtual audio drivers can intercept loopback audio | EPIC-09 |

---

## Suggested MVP Scope (v0.1)

Focus on the core "push-to-ask" loop:

1. WASAPI loopback + mic capture (EPIC-01)
2. Whisper STT with VAD gating (EPIC-02)
3. Claude API with meeting context prompt (EPIC-03)
4. Single configurable hold hotkey (EPIC-04)
5. Minimal overlay showing last response (EPIC-05)
6. Basic settings panel for API key + device (EPIC-07)

Screen capture / vision (EPIC-06) adds significant complexity and can be a v0.2 feature.
