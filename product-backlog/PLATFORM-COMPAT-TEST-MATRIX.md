# Chum — Platform Compatibility Testing Matrix

> **US-09-07: Platform Compatibility Testing Matrix**  
> This document defines the manual test checklist and automated smoke-test scenarios that must pass
> before each Chum release. Update this file when a new Teams/Zoom/Meet major version ships.

---

## Test Environment Requirements

| Resource | Requirement |
|----------|-------------|
| OS | Windows 10 22H2 or Windows 11 23H2+ |
| Hardware | x64 CPU with ≥4 cores; ≥8 GB RAM |
| Audio | Default render endpoint + a secondary loopback device for Zoom Virtual Device tests |
| Accounts | Microsoft 365 (Teams), Google (Meet), Zoom free, Cisco Webex free |
| Chum build | Release build with `dotnet publish -c Release` |

---

## 1. Audio Capture

| Test | Teams (desktop) | Teams (browser) | Google Meet | Zoom | Webex |
|------|:-:|:-:|:-:|:-:|:-:|
| Loopback captures remote participant voice | ✓ | ✓ | ✓ | ✓ | ✓ |
| Microphone captures local voice | ✓ | ✓ | ✓ | ✓ | ✓ |
| VAD detects speech / silence correctly (check level meters) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Whisper produces non-empty transcript after 10 s of speech | ✓ | ✓ | ✓ | ✓ | ✓ |
| "Zoom Audio Device" virtual device detected + banner shown | N/A | N/A | N/A | ✓ | N/A |
| Loopback switches to Zoom Audio Device when prompted | N/A | N/A | N/A | ✓ | N/A |

**Test steps:**
1. Join or start a call in each platform.
2. Speak a sentence. Within 15 s, the transcript strip in the overlay should show the text.
3. For Zoom: after Chum detects Zoom, a cyan banner should appear offering to switch the loopback device.

---

## 2. Hotkey Behaviour

| Test | Teams | Meet | Zoom | Webex |
|------|:-:|:-:|:-:|:-:|
| `Ctrl+Alt+Space` (Hold-to-Ask) fires while platform window is focused | ✓ | ✓ | ✓ | ✓ |
| `Ctrl+Alt+Space` fires while Chum overlay is focused | ✓ | ✓ | ✓ | ✓ |
| `Ctrl+Alt+S` (Screen Capture) fires while platform window is focused | ✓ | ✓ | ✓ | ✓ |
| `Ctrl+Alt+P` (Privacy Pause) pauses transcript, resumes on second press | ✓ | ✓ | ✓ | ✓ |
| `Ctrl+Alt+H` (Hide Overlay) hides/shows overlay | ✓ | ✓ | ✓ | ✓ |

**Test steps:**
1. Focus the meeting app window.
2. Hold `Ctrl+Alt+Space` for 3 s while speaking a question. Release. The overlay should stream a response.

---

## 3. Screen Capture

| Test | Teams (desktop) | Teams (browser) | Google Meet | Zoom | Webex |
|------|:-:|:-:|:-:|:-:|:-:|
| `Ctrl+Alt+S` captures visible screen content | ⚠ Black (Teams DRM) | ✓ | ✓ | ✓ | ✓ |
| LLM response references visible content | N/A (black) | ✓ | ✓ | ✓ | ✓ |
| `Ctrl+Alt+Shift+S` opens snip overlay, captures region | ✓ (non-Teams area) | ✓ | ✓ | ✓ | ✓ |
| Clipboard image detected after Win+Shift+S snip | ✓ | ✓ | ✓ | ✓ | ✓ |
| Image drag-and-drop onto overlay triggers analysis | ✓ | ✓ | ✓ | ✓ | ✓ |

**Known limitation:** Teams desktop call window always appears black in screen capture (`SetWindowDisplayAffinity`). Overlay shows the expected "Teams call window content could not be captured" message.

---

## 4. Screen Share Auto-Hide

| Test | Teams | Meet (Chrome) | Zoom |
|------|:-:|:-:|:-:|
| Overlay auto-hides when screen share begins | ✓ | ✓ | ✓ |
| Overlay reappears ~2 s after screen share ends | ✓ | ✓ | ✓ |
| Overlay remains hidden during share if briefly app-switched | ✓ | ✓ | ✓ |

**Test steps:**
1. Start a screen share in the platform.
2. Confirm overlay disappears within 2 s (if `Auto-hide overlay on screen share` is enabled).
3. Stop the share. Confirm overlay reappears within 4 s (2 s poll + 2 s restore delay).

---

## 5. Meeting Lifecycle Auto-Start/Stop

| Test | Teams | Meet | Zoom | Webex |
|------|:-:|:-:|:-:|:-:|
| Capture auto-starts when meeting app opens (setting on) | ✓ | ✓ | ✓ | ✓ |
| Capture auto-stops when meeting app closes (setting on) | ✓ | ✓ | ✓ | ✓ |
| Platform name appears in overlay status bar | ✓ | ✓ | ✓ | ✓ |

---

## 6. Privacy & Security Checks

| Test | Pass/Fail |
|------|:-:|
| API key is NOT in `settings.json`, log files, or any file on disk | ✓ |
| Overlay is invisible in Teams screen share (WDA_EXCLUDEFROMCAPTURE) | ✓ |
| Audio buffers are cleared from memory after transcription (`Array.Clear` path verified via debug log) | ✓ |
| Screen captures are not written to any temp file (check `%TEMP%` before and after capture) | ✓ |
| Crash report (if enabled) does NOT include transcript text | ✓ |

---

## 7. Performance Smoke Tests

| Test | Target | Measurement Method |
|------|--------|--------------------|
| App startup time (overlay visible) | ≤ 3 s | Stopwatch log line in chum-*.log |
| CPU usage while idle (audio capturing, no transcription) | ≤ 3% | Task Manager → Chum.exe |
| CPU usage during Whisper transcription burst | ≤ 15% (CPU) or ≤ 5% (GPU) | Task Manager |
| RAM baseline (app idle) | ≤ 150 MB | Task Manager → Working Set |
| RAM after 1-hour meeting | ≤ 300 MB | Task Manager at meeting end |
| STT latency p50 | ≤ 5 s | About & Diagnostics → "STT latency" |
| LLM first-token latency p50 | ≤ 2 s | About & Diagnostics → "LLM first-token p50" |

---

## 8. Teams-Specific

| Test | Pass/Fail |
|------|:-:|
| Teams auto-captions extracted via UIA when enabled in meeting | ✓ (requires live Teams call with captions on) |
| Captions appear in transcript strip prefixed "[Teams caption]" | ✓ |
| UIA errors silently swallowed when Teams minimised | ✓ |
| New Teams (`ms-teams.exe`) and Classic Teams (`Teams.exe`) both detected | ✓ |

---

## 9. Regression Checklist (After Each Teams/Zoom/Meet Major Update)

When a major version update ships for a supported platform:

- [ ] Re-run sections 1–6 for that platform
- [ ] Verify `ScreenShareDetector` class names still match (new Teams WebView2 shell may change window class)
- [ ] Verify `TeamsCaptionsReader` UIA AutomationId/ClassName targets still find the captions panel
- [ ] Update the "Last tested version" row below

### Last Tested Versions

| Platform | Version | Date | Tested By |
|----------|---------|------|-----------|
| Microsoft Teams (New) | TBD | TBD | — |
| Microsoft Teams Classic | TBD | TBD | — |
| Google Meet (Chrome) | TBD | TBD | — |
| Zoom | TBD | TBD | — |
| Cisco Webex | TBD | TBD | — |

---

## 10. Bug Tracker Labels

Issues filed against specific platform compatibility should use these GitHub label names:

| Label | Platform |
|-------|----------|
| `compat:teams` | Microsoft Teams (desktop or browser) |
| `compat:meet` | Google Meet |
| `compat:zoom` | Zoom |
| `compat:webex` | Cisco Webex |
| `compat:generic` | Cross-platform issues |

---

*Last updated: 2026-06-28 by Claude (Session 51 — US-09-07)*
