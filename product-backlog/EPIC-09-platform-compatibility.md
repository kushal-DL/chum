# EPIC-09: Meeting Platform Compatibility

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-09-01 | Meeting Platform Auto-Detection | P1 — High | 🔴 Yet to Start | 5 |
| US-09-02 | Teams-Specific Audio Device Handling | P2 — Medium | 🔴 Yet to Start | 3 |
| US-09-03 | Zoom Audio Device Handling | P2 — Medium | 🔴 Yet to Start | 3 |
| US-09-04 | Screen Share Detection per Platform | P3 — Low | 🔴 Yet to Start | 5 |
| US-09-05 | Teams Auto-Captions Integration | P2 — Medium | 🔴 Yet to Start | 5 |
| US-09-06 | Meeting Start & End Lifecycle | P2 — Medium | 🔴 Yet to Start | 3 |
| US-09-07 | Platform Compatibility Testing Matrix | P3 — Low | 🔴 Yet to Start | 3 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

Chum must work reliably across the major video conferencing platforms used in corporate environments: Microsoft Teams, Google Meet, Zoom, and Cisco Webex. Each platform has unique quirks around audio routing, screen capture permissions, and window management. This epic documents per-platform behaviour and ensures Chum gracefully handles each.

---

## Platform Compatibility Matrix

| Feature | Teams (desktop) | Teams (browser) | Google Meet | Zoom | Webex |
|---------|----------------|-----------------|-------------|------|-------|
| Audio loopback capture | ✅ Works | ✅ Works | ✅ Works | ✅ Works | ✅ Works |
| Microphone capture | ✅ Works | ✅ Works | ✅ Works | ✅ Works | ✅ Works |
| Screen capture (call window) | ❌ Blocked | ✅ Works | ✅ Works | ✅ Works | ✅ Works |
| UI Automation (captions) | ⚠️ Partial | N/A | ⚠️ Limited | ⚠️ Limited | ⚠️ Limited |
| Screen share detection | ✅ Detectable | ⚠️ Hard | ⚠️ Hard | ✅ Detectable | ⚠️ Hard |
| Meeting start detection | ✅ Process+Window | ✅ Browser tab | ✅ Browser tab | ✅ Process | ⚠️ Process |

---

## Platform-Specific Technical Notes

### Microsoft Teams (Desktop App)

**Process name**: `ms-teams.exe` (new Teams) or `Teams.exe` (classic Teams)

**Audio**: Teams uses WASAPI shared mode for audio output by default. Loopback capture from Windows default output works. However, Teams can be configured to use a specific audio device via Teams settings — if this differs from Windows default, capture device must follow Teams.

**Screen capture**: `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` blocks all standard capture. Full details in EPIC-06.

**Screen share detection**: New Teams window title changes when sharing. Also: `Teams.exe` spawns a `DesktopWindowXamlSource` child process for the sharing bar — detecting this process signals active share.

**Version detection**: Classic Teams vs New Teams behave differently; check for `ms-teams.exe` vs `Teams.exe`.

---

### Google Meet (Chrome Browser)

**Audio**: Audio plays through Chrome's audio session. WASAPI loopback captures it normally via the default output device.

**Screen capture**: Chrome (and therefore Google Meet) does NOT use `SetWindowDisplayAffinity`. Screen capture works fully.

**Browser tab detection**: Google Meet running in Chrome is detectable by:
1. Chrome accessibility tree: tab title contains "Google Meet" or "meet.google.com"
2. Process: `chrome.exe` is running
3. Not reliable alone; need URL detection via UIA

**Screen share in Google Meet**: When sharing, Chrome shows a screencasting indicator. Detectable via UIA on Chrome's window title bar ("Sharing your screen" string appears).

---

### Zoom (Desktop App)

**Process name**: `Zoom.exe`

**Audio**: Zoom injects a virtual audio device (`Zoom Audio Device`) that appears in WASAPI. Audio output through Zoom's virtual device must be captured from that device specifically, not just Windows default. Chum should detect Zoom Audio Device and offer it as the loopback source.

**Screen capture**: Zoom does NOT use `SetWindowDisplayAffinity` on Windows. Zoom meeting window is capturable normally.

**Zoom Noise Suppression**: Zoom applies heavy noise suppression to its audio. Transcription of Zoom audio may have more artifacts than Teams. Consider separate audio quality settings per detected platform.

---

### Cisco Webex (Desktop App)

**Process name**: `CiscoCollabHost.exe` or `Webex.exe`

**Audio**: Standard WASAPI loopback; no special virtual device.

**Screen capture**: No `SetWindowDisplayAffinity`. Capturable normally.

---

## Stories

### US-09-01: Meeting Platform Auto-Detection
**Story Points: 5**

**As a** user who joins calls on different platforms,  
**I want** Chum to automatically detect which meeting platform is active,  
**so that** it can apply the right capture strategy without manual configuration.

**Acceptance Criteria:**
- [ ] Chum detects active meeting platform by scanning running processes every 5s
- [ ] Detection covers: Teams (classic + new), Google Meet (Chrome), Zoom, Webex
- [ ] Detected platform shown in overlay status bar ("Teams call detected")
- [ ] Platform detection triggers any platform-specific audio device selection
- [ ] If multiple platforms active simultaneously, user prompted to select the primary one
- [ ] No platform detected: Chum operates in "generic" mode with default settings

**Technical Implementation:**
```csharp
string DetectActivePlatform() {
    var processes = Process.GetProcesses();
    if (processes.Any(p => p.ProcessName is "ms-teams" or "Teams")) return "Teams";
    if (processes.Any(p => p.ProcessName == "Zoom")) return "Zoom";
    if (processes.Any(p => p.ProcessName is "CiscoCollabHost" or "Webex")) return "Webex";
    if (IsGoogleMeetTabOpen()) return "GoogleMeet";
    return "Unknown";
}
```

For Google Meet detection via UIA:
```csharp
bool IsGoogleMeetTabOpen() {
    var chrome = Process.GetProcessesByName("chrome").FirstOrDefault();
    if (chrome == null) return false;
    var uia = new CUIAutomation();
    var chromeWnd = uia.ElementFromHandle(chrome.MainWindowHandle);
    // Navigate to tab strip; check tab titles for "meet.google.com"
    ...
}
```

---

### US-09-02: Teams-Specific Audio Device Handling
**Story Points: 3**

**As a** Teams user with audio configured to a non-default device,  
**I want** Chum to automatically capture from the device Teams is using,  
**so that** I don't miss audio when Teams is on a different output device.

**Acceptance Criteria:**
- [ ] When Teams detected: Chum reads Teams' audio device setting (if accessible)
- [ ] Fallback: list all WASAPI render endpoints; show which has active audio sessions from Teams
- [ ] If Teams audio device differs from Windows default, offer to switch Chum's loopback source
- [ ] Setting persists per Teams audio device

**Technical Notes:**
- Detect Teams audio sessions via `IAudioSessionManager2.GetSessionEnumerator()`
- Each session has a `process_id` and `session_identifier` — match against Teams PID
- The session's `AudioEndpoint` reveals which device Teams is using

---

### US-09-03: Zoom Audio Device Handling
**Story Points: 3**

**As a** Zoom user,  
**I want** Chum to capture from Zoom's virtual audio device when Zoom is active,  
**so that** I get high-quality capture of Zoom call audio.

**Acceptance Criteria:**
- [ ] When Zoom detected: Chum checks if "Zoom Audio Device" exists in WASAPI device list
- [ ] If found: offer to use Zoom Audio Device as loopback source
- [ ] If Zoom Audio Device not found: fall back to Windows default with notification
- [ ] "Zoom Audio Device" selection persists for Zoom sessions

**Technical Notes:**
- `Zoom Audio Device` is a WASAPI render endpoint installed by Zoom
- Enumerate endpoints: look for `FriendlyName` containing "Zoom Audio Device"

---

### US-09-04: Screen Share Detection per Platform
**Story Points: 5**

**As a** user sharing their screen during a meeting,  
**I want** Chum to detect screen share and auto-hide the overlay,  
**so that** other participants don't see the Chum overlay.

**Acceptance Criteria:**
- [ ] Teams (desktop): detect screen share via window title change + child process detection
- [ ] Google Meet (Chrome): detect via UIA on Chrome's address bar / window title
- [ ] Zoom: detect via Zoom's `ZPControlBar` window appearing (sharing toolbar)
- [ ] Generic: detect via `Windows.Graphics.Capture` session count increasing (another app started capturing the display)
- [ ] On detection: overlay auto-hides (if auto-hide setting enabled)
- [ ] On share end: overlay auto-shows after 2s delay

**Teams Screen Share Detection:**
```csharp
// New Teams: sharing toolbar is a separate window
bool isTeamsSharing = Process.GetProcessesByName("ms-teams")
    .SelectMany(p => EnumerateChildWindows(p.MainWindowHandle))
    .Any(hwnd => GetWindowText(hwnd).Contains("Stop sharing"));
```

**Zoom Screen Share Detection:**
```csharp
// Zoom sharing toolbar window appears during screen share
bool isZoomSharing = FindWindow("ZPControlBar", null) != IntPtr.Zero;
```

---

### US-09-05: Teams Auto-Captions Integration
**Story Points: 5**

**As a** Teams user who has enabled meeting captions,  
**I want** Chum to optionally read Teams' own captions as a transcript source,  
**so that** I get Teams' high-quality transcription instead of Chum's local Whisper.

**Acceptance Criteria:**
- [ ] When Teams detected with captions active: option to use Teams captions as transcript source
- [ ] Teams caption text extracted via UI Automation from the captions panel
- [ ] Captions merged with audio-based transcript (or replace it if user prefers)
- [ ] "Use Teams captions when available" toggle in settings (default: off — some users may not have captions enabled)
- [ ] Fallback to Whisper if Teams captions not detected

**Challenge**: Teams' UIA AutomationIds for the captions panel change between versions. Implementation requires version-aware ID mapping and graceful fallback.

---

### US-09-06: Meeting Start and End Lifecycle
**Story Points: 3**

**As a** user who wants Chum to be hands-free,  
**I want** Chum to automatically activate when I join a meeting and settle down when it ends,  
**so that** I don't need to manually start/stop capture.

**Acceptance Criteria:**
- [ ] Meeting start detection: sustained audio on loopback (>10s of non-silence) while meeting app is in foreground
- [ ] On start: Chum overlay appears (if hidden), capture confirmed active, optional notification
- [ ] Meeting end detection: meeting app window closes OR prolonged silence (>60s)
- [ ] On end: optional transcript export prompt; audio buffers cleared; overlay returns to idle state
- [ ] Detection works for all supported platforms
- [ ] Auto-detect meeting lifecycle is a configurable option (default: on)

---

### US-09-07: Platform Compatibility Testing Matrix
**Story Points: 3**

**As a** developer,  
**I want** a documented test plan for each supported platform,  
**so that** I can verify Chum works correctly across all platforms before each release.

**Acceptance Criteria:**
- [ ] Documented test scenarios per platform: audio capture, hotkey behaviour, screen capture, screen share detection
- [ ] Automated smoke tests where possible (mock audio devices, virtual display)
- [ ] Manual test checklist for platform-specific features
- [ ] Compatibility matrix updated with each Teams/Zoom/Meet major update
- [ ] Bug tracker label per platform: `compat:teams`, `compat:meet`, `compat:zoom`

**Test Scenarios per Platform:**

| Scenario | Teams | Meet | Zoom | Webex |
|----------|-------|------|------|-------|
| Audio loopback captures remote participant | ✓ | ✓ | ✓ | ✓ |
| Mic capture works independently of app | ✓ | ✓ | ✓ | ✓ |
| Hotkey fires with Teams/Meet in focus | ✓ | ✓ | ✓ | ✓ |
| Screen capture hotkey (non-call window) | ✓ | ✓ | ✓ | ✓ |
| Screen share auto-hides overlay | ✓ | ✓ | ✓ | ✓ |
| App audio device switch handled | ✓ | N/A | ✓ | N/A |

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| Teams updates change window titles/process names | Medium | Version detection; configurable process name list; community updates |
| Zoom Virtual Audio Device not always installed | Low | Fallback to Windows default; warn user to check Zoom audio settings |
| Google Meet URL-based detection is fragile | Medium | Multiple detection signals: tab title + UIA + audio session activity |
| Corporate Teams managed by IT — may restrict features | Low | Document as known limitation; local-only mode unaffected by Teams policy |
| Teams Web App (browser Teams) has no DRM | Low | Browser-based Teams captured normally; document this for users on browser Teams |

---

## Dependencies

- EPIC-01 (Audio Engine) — platform-specific device handling
- EPIC-05 (Overlay UI) — auto-hide triggered by screen share detection
- EPIC-06 (Screen Capture) — platform awareness affects capture strategy
- EPIC-07 (Settings) — platform detection settings, per-platform preferences
