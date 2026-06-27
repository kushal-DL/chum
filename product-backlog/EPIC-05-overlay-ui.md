# EPIC-05: Overlay UI & Display

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-05-01 | Transparent Always-on-Top Window | P0 — MVP | 🔵 Built | 5 |
| US-05-02 | Response Display Panel | P0 — MVP | 🔵 Built | 8 |
| US-05-03 | Live Transcript Strip | P2 — Medium | 🔵 Built | 5 |
| US-05-04 | Status Indicators | P1 — High | 🔵 Built | 3 |
| US-05-05 | System Tray Integration | P1 — High | 🔵 Built | 5 |
| US-05-06 | Overlay Theme & Opacity Config | P2 — Medium | 🔵 Built | 3 |
| US-05-07 | Auto-hide During Screen Share | P1 — High | 🔵 Built | 8 |
| US-05-08 | Multi-monitor Support | P2 — Medium | 🔴 Yet to Start | 3 |
| US-05-09 | Response Copy & Share | P2 — Medium | 🔵 Built | 2 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

The Chum overlay is a transparent, always-on-top WPF window that floats above your meeting app. It displays the AI response in real-time as it streams, shows a compact transcript strip, and provides status indicators (capture state, hotkey state, network state). It must be:
- **Invisible to others by default** when not screen-sharing
- **Quick to hide** when screen-sharing
- **Readable at a glance** — you can't stare at it during a meeting
- **Non-intrusive** — should not block the meeting app

---

## Technical Background

### WPF Transparent Always-on-Top Window

```xml
<Window AllowsTransparency="True"
        WindowStyle="None"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False">
```

Key properties:
- `AllowsTransparency="True"` enables per-pixel alpha blending
- `WindowStyle="None"` removes the title bar and borders
- `Topmost="True"` keeps the window above all others
- `ShowInTaskbar="False"` prevents it from appearing in the taskbar

**Performance note**: `AllowsTransparency` on WPF uses software rendering by default on some systems. Use `WindowChrome` with custom chrome + `RenderOptions.ProcessRenderMode = RenderMode.Default` to ensure hardware acceleration.

### Overlay Positioning
The overlay should default to a corner of the screen (bottom-right recommended) and be draggable. Position persists between sessions.

### Screen Share Visibility Problem
When the user shares their entire screen (not just a window), the overlay IS visible to remote participants. Solutions:
1. **Manual hide hotkey** (EPIC-04) — quickest, requires user action
2. **Auto-detect screen share** and hide automatically (see US-05-07)
3. **Window-specific share**: instruct users to share only the meeting window, not full screen

---

## Stories

### US-05-01: Transparent Always-on-Top Window
**Story Points: 5**

**As a** user in a meeting,  
**I want** a small transparent panel floating above my meeting window,  
**so that** I can see AI responses without switching applications.

**Acceptance Criteria:**
- [ ] Window is transparent (non-content areas are fully see-through)
- [ ] Window stays above all other windows including Teams (`Topmost = true`)
- [ ] Window does not appear in taskbar or Alt+Tab switcher
- [ ] Window can be dragged to any position by clicking and dragging its header area
- [ ] Window position persists across restarts
- [ ] Minimum window size: 320×200px; default: 400×300px
- [ ] Window resizable by dragging edges
- [ ] Size persists across restarts

**Technical Notes:**
- Drag: handle `MouseLeftButtonDown` on header, call `DragMove()`
- Position/size persistence: store in `%APPDATA%\Chum\window_state.json`
- `Topmost` may be briefly overridden by other apps; restore it via a 500ms polling timer as fallback

---

### US-05-02: Response Display Panel
**Story Points: 8**

**As a** user reading the AI response,  
**I want** the response text to appear clearly formatted and readable,  
**so that** I can consume the answer quickly during a meeting.

**Acceptance Criteria:**
- [ ] Response text renders in a scrollable panel
- [ ] Markdown rendered: bold, italic, `code`, bullet lists, numbered lists
- [ ] Text size configurable (default: 13px, range: 10–18px)
- [ ] Font: monospace for code blocks; proportional sans-serif for prose
- [ ] Background: semi-transparent dark panel (default: black at 75% opacity); configurable
- [ ] Response text colour: white/light grey on dark background
- [ ] New response replaces old response in panel (not appended)
- [ ] Response panel auto-scrolls to bottom as tokens stream in
- [ ] Copy-to-clipboard button visible on hover

**Streaming Animation:**
- Cursor blink indicator at end of streaming text
- Cursor disappears when streaming completes
- Smooth text append — no flash or re-render of prior text

**Markdown Rendering:**
```csharp
// Use Markdig to convert markdown to FlowDocument
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
var doc = Markdig.Wpf.Markdown.ToFlowDocument(markdownText, pipeline);
responseViewer.Document = doc;
```

---

### US-05-03: Live Transcript Strip (Compact View)
**Story Points: 5**

**As a** user monitoring what Chum is hearing,  
**I want** a compact scrolling strip of the live transcript,  
**so that** I can verify Chum understood the conversation correctly.

**Acceptance Criteria:**
- [ ] Compact strip shows last 3–5 lines of transcript, auto-scrolling
- [ ] Each line prefixed with speaker label (Me / Remote) and relative time ("30s ago")
- [ ] Font is small (10–11px) and muted colour to not distract
- [ ] Strip can be collapsed/expanded by clicking a toggle arrow
- [ ] Strip collapsed by default; expanded state persists

**Technical Notes:**
- Bind to `ObservableCollection<TranscriptLine>` — WPF `ItemsControl` with auto-scroll
- Auto-scroll: attach `ScrollViewer.ScrollToEnd()` to `CollectionChanged` event
- "Streaming" text: final word of the current in-progress segment shown in italic until segment is committed

---

### US-05-04: Status Indicators
**Story Points: 3**

**As a** user,  
**I want** to see the current state of Chum at a glance,  
**so that** I know if it is capturing, processing, paused, or encountering errors.

**Acceptance Criteria:**
- [ ] Status indicator with colour coding:
  - Green dot: capturing and transcribing normally
  - Blue pulse: LLM query in progress
  - Yellow dot: warning (e.g., slow transcription, rate limit approaching)
  - Red dot: paused (privacy mode) or error
  - Grey dot: idle (no audio detected for >30s)
- [ ] Short status text: "Listening", "Thinking...", "Paused", "Error: [reason]"
- [ ] Audio level micro-bars visible (optional; toggleable)

---

### US-05-05: System Tray Integration
**Story Points: 5**

**As a** user who wants Chum out of the way,  
**I want** Chum to live in the system tray,  
**so that** it runs unobtrusively and I can access it from the tray.

**Acceptance Criteria:**
- [ ] Chum icon in system tray when running
- [ ] Tray icon reflects capture state (coloured icon variants: green/red/yellow)
- [ ] Right-click tray menu: Show/Hide overlay, Pause/Resume capture, Export transcript, Settings, Quit
- [ ] Double-click tray icon: Show/hide overlay
- [ ] Chum starts minimised to tray by default (configurable)
- [ ] Close button on overlay minimises to tray rather than exiting (X button = tray; Quit from tray = exit)

**Technical Notes:**
- WPF does not have native tray icon; use `Hardcoresharp.Taskbar` or `NotifyIcon` from `System.Windows.Forms` (both WinForms and WPF can coexist)
- Icon: custom `.ico` with multiple sizes (16x16, 24x24, 32x32, 48x48, 256x256)
- Coloured icon states: ship separate `.ico` files for each state

---

### US-05-06: Overlay Theme and Opacity Configuration
**Story Points: 3**

**As a** user who works in different lighting environments,  
**I want** to configure the overlay's appearance,  
**so that** it is readable and unobtrusive in my specific setup.

**Acceptance Criteria:**
- [ ] Background opacity: slider from 20% to 95% (default: 75%)
- [ ] Color themes: Dark (default), Light, High Contrast
- [ ] Response text size: 10–18px slider
- [ ] Overlay corner radius: 0 (sharp) to 12px (rounded) — default 8px
- [ ] Changes applied live (preview as user adjusts sliders)
- [ ] Position on screen: drag-to-position + snap-to-corners option

---

### US-05-07: Auto-Hide During Screen Share Detection
**Story Points: 8**

**As a** user who frequently shares their full screen,  
**I want** the overlay to automatically hide when I start sharing,  
**so that** meeting participants don't see my AI assistant.

**Acceptance Criteria:**
- [ ] App detects when Teams or Google Meet screen share is active
- [ ] On detection: overlay fades out in 500ms
- [ ] When share ends: overlay fades back in
- [ ] User can disable auto-hide in settings
- [ ] Manual hide hotkey still works regardless of auto-detection state
- [ ] A toast notification on auto-hide: "Overlay hidden — screen share detected"

**Detection Approaches:**

**Option A: Process Window Title Monitoring**
- Poll active window titles every 2s looking for Teams "sharing" state
- Fragile: depends on Teams window title format which changes with updates

**Option B: Windows Graphics Capture API — Monitor Capture Session Detection**
- Detect if a capture session is active on the display (other apps capturing the screen)
- More reliable but complex

**Option C: Accessibility API on Teams Window**
- Read Teams UI state via `IUIAutomation` to detect "You're sharing" indicator
- Accurate but very Teams-specific

**Recommendation:** Implement Option A as baseline (simple, good enough), with Option C as a follow-up for Teams specifically.

**Stronger guarantee — capture exclusion (recommended belt-and-suspenders):**
- Apply `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` to the overlay's HWND. This is the same Win32 API Teams uses to block screenshots of its call window. Applied to *Chum's own overlay*, the window renders normally on the physical display but is excluded from all standard frame captures (screenshots, screen recording, Teams/Meet/Zoom screen share).
- Effect: even if auto-hide detection misses a share event, the overlay still does not appear in the shared/recorded frame.
- Scope/limits (important): this only hides *Chum's own overlay UI* from captures. It does **not** hide the Chum process, window handle, or network activity from the OS or from any monitoring software — see EPIC-08 "Non-Goals". WDA is a per-window display flag, not a stealth/anti-detection mechanism.
- Caveats: requires Windows 10 2004+ for `WDA_EXCLUDEFROMCAPTURE` (older builds only support `WDA_MONITOR`). May interact with `AllowsTransparency=true` + GPU compositing — test on target hardware. Provide a settings toggle in case a user *wants* the overlay visible in their own recordings (e.g., demos).

---

### US-05-08: Multi-Monitor Support
**Story Points: 3**

**As a** user with multiple monitors,  
**I want** the overlay to appear on the correct monitor,  
**so that** it is visible near my meeting application.

**Acceptance Criteria:**
- [ ] Overlay launches on the monitor where the meeting application is running
- [ ] If meeting app cannot be detected, launch on primary monitor
- [ ] User can drag overlay to any monitor; position remembered per monitor configuration
- [ ] When monitors are added/removed, overlay moves to primary monitor if its previous monitor is gone

**Technical Notes:**
- Detect meeting app monitor: find `System.Windows.Forms.Screen` containing the Teams/Meet window (`GetWindowRect` + `Screen.FromRectangle`)
- Monitor configuration identity: hash of all monitor names + resolutions + positions; keyed in settings

---

### US-05-09: Response Copy and Share
**Story Points: 2**

**As a** user who wants to share an AI response in the chat,  
**I want** to quickly copy the AI response to my clipboard,  
**so that** I can paste it into Teams chat or an email.

**Acceptance Criteria:**
- [ ] "Copy" button visible on hover over response panel
- [ ] `Ctrl+C` on overlay copies the current response
- [ ] Copies as plain text (markdown stripped)
- [ ] "Copied!" tooltip confirmation for 2s after copy
- [ ] Optional: "Copy as Markdown" option in right-click context menu

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| `AllowsTransparency` disables hardware acceleration on some GPU drivers | Medium | Use `WindowChrome` approach with layered window; or accept slight performance reduction |
| `Topmost` overridden by other apps that also use `Topmost` (rare) | Low | Periodic `SetWindowPos(HWND_TOPMOST)` Win32 call as fallback |
| Overlay visible to participants when user shares full screen | High | Auto-hide detection + manual hide hotkey + user education |
| WPF FlowDocument streaming append causes full re-render (performance) | Medium | Append `Run` elements to the last `Paragraph` incrementally; avoid full document replace |
| Accessibility: overlay not readable by screen readers | Low | Defer until v1.0; not required for meeting assistant use case |
| Overlay on high-DPI displays (150%, 200% scaling) | Medium | Use `UseLayoutRounding="True"` + `SnapsToDevicePixels="True"`; test on 4K monitor |

---

## Dependencies

- EPIC-03 (LLM Integration) streams response text to the overlay
- EPIC-04 (Hotkeys) drives overlay state transitions (listening, processing, hidden)
- EPIC-02 (Transcription) feeds the live transcript strip
- EPIC-07 (Settings) stores overlay preferences
- EPIC-08 (Privacy) triggers overlay pause indicator
