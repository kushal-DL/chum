# EPIC-04: Hotkey & Trigger System

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-04-01 | Global Hold-to-Ask Hotkey | P0 — MVP | 🔴 Yet to Start | 8 |
| US-04-02 | Screen Capture Hotkey | P1 — High | 🔴 Yet to Start | 3 |
| US-04-03 | Privacy Pause Hotkey | P1 — High | 🔴 Yet to Start | 2 |
| US-04-04 | Overlay Hide/Show Hotkey | P1 — High | 🔴 Yet to Start | 2 |
| US-04-05 | Hotkey Configuration UI | P1 — High | 🔴 Yet to Start | 5 |
| US-04-06 | Visual and Audio Feedback | P1 — High | 🔴 Yet to Start | 3 |
| US-04-07 | Action Items Hotkey | P2 — Medium | 🔴 Yet to Start | 3 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

Hotkeys are how the user interacts with Chum without disrupting the meeting. The core interaction is **hold-to-ask**: while the user holds a key combination, Chum marks a window in the transcript ("what was said while I was holding this key"), releases the key → Chum constructs a query and fires the LLM. A separate hotkey triggers a screen capture + visual analysis.

This must work globally — the hotkey must fire even when Teams, Chrome, or any other app has focus.

---

## Technical Background

### Windows Hotkey Mechanisms

| Mechanism | Works when app not focused | Blocks key from reaching other apps | Notes |
|-----------|--------------------------|-------------------------------------|-------|
| `RegisterHotKey` (Win32) | Yes | Yes | Simple; only single keystrokes; conflicts with system hotkeys |
| Low-Level Keyboard Hook (`SetWindowsHookEx` + `WH_KEYBOARD_LL`) | Yes | Optional | Full control; can intercept any key combo; need to be careful about blocking |
| `RawInput` API | Yes | No | Good for monitoring; cannot intercept (app still receives key) |

**Recommended approach: Low-Level Keyboard Hook (`WH_KEYBOARD_LL`)**
- Installed via `SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, hInstance, 0)`
- Fires for every keystroke system-wide
- Can choose to block or pass through each keystroke
- For "hold-to-query": on `WM_KEYDOWN` for the hotkey, set a flag; on `WM_KEYUP`, fire the query. Pass the keystroke through so the app still receives it normally (less disruptive).

**RegisterHotKey Alternative:**
- Easier to implement; fewer pitfalls
- But: limited to single modifier + key combinations; some combos are reserved by Windows

---

## Stories

### US-04-01: Global Hold-to-Ask Hotkey
**Story Points: 8**

**As a** user in a meeting,  
**I want** to hold a keyboard shortcut to mark the question being asked,  
**so that** when I release it, Chum knows exactly what context to send to the LLM.

**Acceptance Criteria:**
- [ ] Default hotkey: `Ctrl + Alt + Space` (configurable)
- [ ] On `KeyDown`: overlay shows "Listening..." indicator; a timestamp is recorded as the query window start
- [ ] On `KeyUp`: query is triggered — LLM receives transcript from (start_timestamp - 30s) to key_up_timestamp
- [ ] Hotkey works regardless of which application has focus
- [ ] Holding for <300ms is treated as accidental press and ignored (debounce)
- [ ] Holding for >60s auto-releases and fires query (safety valve)
- [ ] Hotkey works correctly on all keyboard layouts (AZERTY, QWERTZ, etc.)

**Timing Window Logic:**
```
Hold window: the 30s before KeyDown + everything up to KeyUp
This captures:
  - The question being asked BY others (heard before press)
  - The question being asked BY the user (said while holding)
  - Any context immediately preceding the question
```

**Technical Implementation:**
```csharp
IntPtr hookId = SetWindowsHookEx(WH_KEYBOARD_LL, LowLevelKeyboardProc, GetModuleHandle(null), 0);

IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam) {
    var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
    bool isHotkey = IsTargetCombo(info.vkCode, IsKeyDown(VK_CONTROL), IsKeyDown(VK_MENU));

    if (wParam == WM_KEYDOWN && isHotkey && !holdActive) {
        holdActive = true;
        holdStartTime = DateTimeOffset.UtcNow;
        overlayViewModel.SetListeningState(true);
    } else if (wParam == WM_KEYUP && isHotkey && holdActive) {
        holdActive = false;
        FireQuery(holdStartTime);
    }
    return CallNextHookEx(hookId, nCode, wParam, lParam); // pass through
}
```

**Challenges & Workarounds:**

| Challenge | Workaround |
|-----------|------------|
| Hook callback must run on same thread that installed it (Windows requirement) | Install hook on dedicated STA thread with message loop (`Thread.SetApartmentState(ApartmentState.STA)`) |
| Key-repeat: `WM_KEYDOWN` fires repeatedly while held | Track `holdActive` flag; ignore repeat WM_KEYDOWN events |
| Ctrl+Alt+Space conflicts with some IME (input method editors) for non-English keyboards | Allow user to change binding; detect if IME is active and warn |
| Hook slows entire system if callback takes too long | Hook callback must return in <1ms; all work dispatched to async queue |

---

### US-04-02: Screen Capture Hotkey
**Story Points: 3**

**As a** user looking at a whiteboard or shared document,  
**I want** to press a hotkey to capture the screen and ask the AI about it,  
**so that** I can get an answer to a visual question without typing.

**Acceptance Criteria:**
- [ ] Default hotkey: `Ctrl + Alt + S` (configurable)
- [ ] On press: screen capture triggered immediately; captured image sent to LLM with the current question from transcript
- [ ] Visual flash on overlay to confirm capture was taken
- [ ] Capture hotkey is momentary (tap, not hold)
- [ ] Capture and transcript query combined into single LLM call

**Technical Notes:**
- Capture implementation: see EPIC-06
- LLM call includes both image and last 30s of transcript
- User prompt: "The user captured their screen during a meeting. Analyse the image and answer the most recent question from this transcript."

---

### US-04-03: Privacy Pause Hotkey
**Story Points: 2**

**As a** user who needs to take a private call or discuss something confidential,  
**I want** a hotkey to instantly pause all capture,  
**so that** Chum never records sensitive conversations.

**Acceptance Criteria:**
- [ ] Default hotkey: `Ctrl + Alt + P` (configurable)
- [ ] On press: all audio capture and transcription paused immediately; overlay shows "PAUSED" in red
- [ ] Second press resumes capture
- [ ] System tray icon changes color during pause (e.g., red)
- [ ] Any audio captured in the 500ms before pause is discarded from buffer
- [ ] Pause state persists until explicitly resumed (not auto-resumed)

---

### US-04-04: Overlay Hide/Show Hotkey
**Story Points: 2**

**As a** user sharing my screen,  
**I want** a hotkey to instantly hide/show the Chum overlay,  
**so that** meeting participants don't see my AI assistant responses.

**Acceptance Criteria:**
- [ ] Default hotkey: `Ctrl + Alt + H` (configurable)
- [ ] Toggle: first press hides overlay; second press shows it
- [ ] Hide is immediate (window opacity to 0 or `Visibility.Collapsed`)
- [ ] Capture and transcription continue running while overlay is hidden
- [ ] System tray icon reflects hidden state

---

### US-04-05: Hotkey Configuration UI
**Story Points: 5**

**As a** user whose hotkey conflicts with another application,  
**I want** to remap all Chum hotkeys,  
**so that** I can use Chum without interfering with my other tools.

**Acceptance Criteria:**
- [ ] Settings panel shows all configurable hotkeys with current assignments
- [ ] "Record" button: click it, then press desired key combo; combo recorded
- [ ] Recorded combo validated: no single-key bindings (must have at least one modifier); max 3 keys
- [ ] Duplicate bindings detected and flagged with error
- [ ] Known conflicts with common apps detected and warned (Teams: `Ctrl+Shift+M`, `Ctrl+Shift+K`; Windows: `Win+*`)
- [ ] "Reset to defaults" button
- [ ] Changes saved immediately and applied without restart

**Known Conflict List to Warn About:**
| Combo | Conflicts With |
|-------|---------------|
| Ctrl+Shift+M | Teams: mute/unmute |
| Ctrl+Shift+K | Teams: raise hand |
| Ctrl+Shift+E | Teams: share screen |
| Alt+Tab | Windows: app switcher |
| Win+* | Windows system shortcuts |
| Ctrl+Alt+Del | Cannot be hooked (Windows intercepts) |
| Ctrl+Alt+F4 | Cannot be hooked reliably |

---

### US-04-06: Visual and Audio Feedback for Hotkey State
**Story Points: 3**

**As a** user holding the query hotkey,  
**I want** clear visual feedback that Chum is in "listening" mode,  
**so that** I know the question is being captured and I don't release the key too early.

**Acceptance Criteria:**
- [ ] Overlay border or badge changes colour on hotkey hold (green = listening, blue = processing)
- [ ] Small audio cue (optional, configurable): soft click/tone on hotkey press and release
- [ ] Animated pulse indicator while hold is active
- [ ] "Query sent" indicator shown briefly after key release
- [ ] All feedback is subtle and unobtrusive — cannot be seen by meeting participants unless screen is shared

**Technical Notes:**
- Colour animation: WPF `ColorAnimation` on border brush; 200ms transition
- Audio cue: play embedded WAV resource via `SoundPlayer`; volume configurable (including mute)
- Pulsing animation: `DoubleAnimation` on `ScaleTransform` — keeps GPU on UI thread, no CPU spike

---

### US-04-07: Action Items Hotkey (Shortcut Mode)
**Story Points: 3**

**As a** user near the end of a meeting,  
**I want** a dedicated hotkey to extract action items from the full session transcript,  
**so that** I get a quick summary without composing a question.

**Acceptance Criteria:**
- [ ] Default hotkey: `Ctrl + Alt + A` (configurable)
- [ ] On press: sends full session transcript to LLM with fixed prompt: "Extract all action items, decisions, and owners from this meeting transcript. Format as a bulleted list."
- [ ] Response displayed in overlay with option to copy to clipboard
- [ ] Also callable from system tray right-click menu

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| LowLevel hook must return fast or Windows kills it | High | Dispatch all work to async queue; hook callback does nothing except signal |
| Hook installed from non-STA thread causes unreliable firing | High | Always install on dedicated STA thread with `Application.Run()` message loop |
| Hotkey conflicts in corporate environments (locked-down Teams shortcuts) | Medium | Configurable bindings; detection/warning for known conflicts |
| Key events missed during high system load | Low | Low-level hook has priority; rarely missed; add telemetry to detect |
| IME users (CJK keyboards): Ctrl+Alt may be used for kanji input | Medium | Detect IME active state; warn user; suggest `Win` key as modifier instead |
| Hotkey eaten by fullscreen application (game, video player) | Low | Not applicable to typical meeting use case; document as known limitation |

---

## Dependencies

- EPIC-01 (Audio Engine) — hold start/end timestamps used to extract audio window
- EPIC-02 (Transcription) — transcript query window extracted based on hotkey timestamps
- EPIC-03 (LLM Integration) — hotkey release triggers LLM query
- EPIC-05 (Overlay UI) — hotkey state drives overlay visual states
- EPIC-06 (Screen Capture) — screen capture hotkey triggers capture pipeline
- EPIC-07 (Settings) — hotkey bindings configured and stored
- EPIC-08 (Privacy) — pause hotkey interacts with capture engine pause signal
