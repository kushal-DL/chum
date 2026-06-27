# EPIC-06: Screen Capture & Visual Analysis

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-06-01 | Primary Screen Capture (DXGI Duplication) | P1 — High | 🔵 Built | 8 |
| US-06-02 | Clipboard Image Monitoring | P1 — High | 🔵 Built | 3 |
| US-06-03 | Image File Drop Target | P2 — Medium | 🔵 Built | 3 |
| US-06-04 | Region Selection (Snip Mode) | P2 — Medium | 🔴 Yet to Start | 5 |
| US-06-05 | UIA Text Extraction (Teams Captions) | P2 — Medium | 🔴 Yet to Start | 5 |
| US-06-06 | Image Preprocessing Pipeline | P1 — High | 🔵 Built | 3 |
| US-06-07 | Multimodal LLM Vision Request | P1 — High | 🔵 Built | 3 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

The screen capture feature lets users point Chum at a whiteboard, a shared document, a slide, or any visual element on their screen and ask the AI about it. This uses a multimodal LLM (Claude Sonnet or GPT-4o) that accepts both the image and the meeting transcript as context.

**The central challenge of this epic is Microsoft Teams' screenshot blocking.** Teams uses `SetWindowDisplayAffinity` to prevent its content from being captured. This file documents the exact nature of this restriction and all viable workarounds.

---

## Critical Technical Background: Teams Screenshot DRM

### How Teams Blocks Screen Capture

Microsoft Teams (and some other communication apps for compliance reasons) calls:
```
SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)  // Windows 10 2004+
// or older:
SetWindowDisplayAffinity(hwnd, WDA_MONITOR)
```

This instructs the Windows Display Driver Model (WDDM) to exclude that window from any Desktop Duplication API output. The result: **the Teams window appears as a solid black rectangle in all screen captures**, including:
- `BitBlt` / `PrintWindow` GDI methods
- DXGI Desktop Duplication API
- Windows.Graphics.Capture API
- OBS Studio
- ShareX
- Windows Snipping Tool (Win+Shift+S) — **also blocked in Teams meeting window**

**Scope of Blocking:**
- Applies only to the Teams window content
- Background (desktop, other windows) still captured normally
- The block is enforced by the GPU driver, not Teams process — even admin-elevated processes cannot bypass it via standard capture APIs
- Teams Enterprise policies can sometimes relax this, but you typically cannot control your employer's Teams policy

### What DOES Work (Workarounds)

The following approaches can extract information from a Teams call screen:

---

## Workaround Deep-Dive

### Workaround 1: Windows UI Automation (Accessibility API)
**Reliability: High for text | Cannot capture images/video**

Windows UI Automation (`IUIAutomation`) reads accessible text from controls without doing a screen capture. Many Teams UI elements (meeting chat, participant names, captions) are exposed via accessibility.

- Read Teams window tree with `IUIAutomation.ElementFromHandle`
- Extract `AutomationId="CaptionLabel"` for live captions text
- **Best use case**: Getting the question from a Teams caption (Teams has built-in auto-captions/transcription)

**Limitation**: Cannot read visual content like a shared whiteboard image or a slide. Text-only.

---

### Workaround 2: Clipboard-Based Capture (User-Initiated)
**Reliability: High | Requires one extra user step**

The user uses Windows built-in tools (which have special OS-level access) to capture the screen, and the content goes to clipboard. Chum then reads from clipboard.

**Workflow:**
1. User presses `Win+Shift+S` (Windows Snipping Tool) — **this bypasses Teams DRM for the desktop but not the Teams call window** (Teams call window still blacks out)
2. User selects an area on a second monitor, or a non-Teams window
3. Chum reads image from clipboard and sends to LLM

**For the Teams call window itself**: `Win+Shift+S` also shows a black Teams window. However, **if the user has a physical whiteboard or external camera showing**, this can capture that non-Teams content.

---

### Workaround 3: Physical Camera / Second Device OCR
**Reliability: High for physical whiteboards | Not for digital screens**

For an in-person meeting with a physical whiteboard:
1. User points their phone camera at the whiteboard (doesn't need to capture through Teams)
2. User photos and shares image to PC (AirDrop, email, Teams files)
3. Chum can process images dragged into the overlay

This is the most reliable path for physical whiteboard content.

---

### Workaround 4: Virtual Display Mirror (Advanced)
**Reliability: Medium | Complex setup | Not user-friendly**

1. Install a virtual display driver (e.g., IddSampleDriver or parsec-vdd)
2. Clone/mirror the primary display to the virtual display
3. Teams applies DRM to the Teams window on the primary display but the virtual display clone is NOT registered with Teams — DXGI Desktop Duplication of the virtual display captures everything including Teams

**Why it works**: `SetWindowDisplayAffinity` applies to the specific monitor on which the window renders. A clone/mirror that the GPU manages at a hardware level bypasses the per-monitor restriction.

**Limitations**: 
- Requires admin rights to install virtual display driver
- Some GPU drivers handle cloning at software level (where DRM still applies)
- Teams may detect this and block it in future updates

---

### Workaround 5: Remote Desktop / VM Capture
**Reliability: High | Not practical for daily use**

Run Teams inside a virtual machine (Hyper-V, VMware). Capture the VM's virtual display from the host. The VM's Teams cannot apply `SetWindowDisplayAffinity` to the host's display.

Not recommended for daily use due to complexity, but documents the attack surface.

---

### Workaround 6: Meeting Recording + Frame Extraction
**Reliability: High | Latency is high (post-meeting)**

Start a local Teams recording. Extract frames from the recording video after the meeting. Not useful for real-time assistance.

---

### Recommended Chum Implementation Strategy

Given the above, Chum should implement a layered capture strategy:

| Layer | What it captures | When to use |
|-------|-----------------|-------------|
| Windows.Graphics.Capture API | Everything except Teams call window | Default; works for Google Meet (Chrome), shared apps, non-Teams content |
| Clipboard Monitor | Whatever user copies to clipboard (images, text) | Works when user initiates `Win+Shift+S` on non-Teams areas |
| Image Drop Target | User drags an image file into overlay | Universal; handles phone photos of whiteboards |
| UIA Text Extraction | Text content from Teams captions/chat | Limited to text; no visual capture |

The app should be **transparent** with the user about what is and isn't capturable, with a clear message like "Teams call window content cannot be captured due to Microsoft's privacy policy. Try capturing a different area, or drop an image into Chum."

---

## Stories

### US-06-01: Primary Screen Capture via Windows.Graphics.Capture
**Story Points: 8**

**As a** user pressing the screen capture hotkey,  
**I want** the app to capture a screenshot of my screen,  
**so that** the AI can analyse what I'm looking at.

**Acceptance Criteria:**
- [ ] Hotkey triggers capture of the current display
- [ ] Capture uses `Windows.Graphics.Capture` API (WinRT; available on Windows 10 1903+)
- [ ] Full display captured by default; region selection available (see US-06-04)
- [ ] Capture completes in ≤500ms
- [ ] Captured image passed to LLM vision API along with current transcript context
- [ ] If a Teams call window is in the capture area, it appears black — overlay shows: "Note: Teams call window content could not be captured"
- [ ] Captured image NOT saved to disk; held in memory only, discarded after LLM response

**Technical Implementation:**
```csharp
// WinRT Windows.Graphics.Capture
var picker = new GraphicsCaptureItemInterop();
var item = GraphicsCaptureItem.CreateFromDisplayId(displayId);
var framePool = Direct3D11CaptureFramePool.Create(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
var session = framePool.CreateCaptureSession(item);
session.StartCapture();
// Get frame, convert to bitmap, compress to JPEG for LLM
```

**Note on WinRT from .NET**: Use `Microsoft.Windows.SDK.Contracts` NuGet to access WinRT APIs from .NET 8.

---

### US-06-02: Clipboard Image Monitoring
**Story Points: 3**

**As a** user who has taken a screenshot with Windows Snipping Tool,  
**I want** Chum to automatically detect the image on my clipboard and offer to analyse it,  
**so that** I can get AI analysis with just one extra step.

**Acceptance Criteria:**
- [ ] Chum monitors clipboard for new image content
- [ ] When image detected on clipboard, overlay shows: "Image detected in clipboard — press [hotkey] to analyse, or dismiss"
- [ ] On confirmation, image sent to LLM with current transcript context
- [ ] Clipboard monitoring is optional and off by default (privacy)
- [ ] Text content also detectable: if user copies a question from Teams chat, Chum can answer it

**Technical Notes:**
- Clipboard monitoring: subscribe to `System.Windows.Clipboard` changed event via `HwndSource` + `WM_CLIPBOARDUPDATE`
- Check `Clipboard.ContainsImage()` on each change
- Throttle: ignore clipboard changes within 500ms of each other

---

### US-06-03: Image File Drop Target
**Story Points: 3**

**As a** user with a photo of a whiteboard on my phone,  
**I want** to drag the image file onto the Chum overlay to analyse it,  
**so that** I can get AI analysis of physical whiteboard content.

**Acceptance Criteria:**
- [ ] Chum overlay accepts drag-and-drop of image files (.png, .jpg, .jpeg, .bmp, .webp)
- [ ] On drop: overlay shows thumbnail preview of the image + "Analyse this image?"
- [ ] Confirmed: image + transcript context sent to LLM
- [ ] Cancelled: image discarded; no LLM call
- [ ] Drop also works for copying image URL from Teams chat and dropping onto overlay
- [ ] Image not saved to disk; processed in memory only

**Technical Notes:**
- WPF drag-and-drop: `AllowDrop="True"` + `Drop` event handler
- Read dropped files: `e.Data.GetData(DataFormats.FileDrop)` as `string[]`
- Validate image format and max size (limit: 10MB before compression)

---

### US-06-04: Region Selection (Snip Mode)
**Story Points: 5**

**As a** user who only needs to capture a specific part of the screen,  
**I want** to draw a selection rectangle over the area I want captured,  
**so that** I send only the relevant part of the screen to the AI (less token waste, better focus).

**Acceptance Criteria:**
- [ ] Alternative capture mode: tap hotkey twice quickly (or Ctrl+Alt+Shift+S) to enter snip mode
- [ ] Screen dims and user draws a selection rectangle with the mouse
- [ ] Selection rectangle shows dimensions in px and a preview thumbnail
- [ ] On mouse release: selected area captured and sent to LLM
- [ ] ESC cancels snip mode without capture
- [ ] Snip mode works on any monitor

**Technical Notes:**
- Snip mode overlay: full-screen transparent window (one per monitor) with custom drawing
- Draw rectangle using WPF `Canvas` with `Rectangle` element following mouse
- Capture the selected region: crop the full-screen capture to the selection bounds

---

### US-06-05: UI Automation Text Extraction (Teams Captions)
**Story Points: 5**

**As a** user in a Teams call with auto-captions enabled,  
**I want** Chum to read the Teams caption text directly,  
**so that** I don't need audio capture at all for remote participants if captions are running.

**Acceptance Criteria:**
- [ ] Chum detects if Teams is running and auto-captions are active
- [ ] Caption text extracted via Windows UIA (`IUIAutomation`)
- [ ] Caption text treated as supplementary transcript source (merged with audio transcript)
- [ ] Works even when audio loopback fails (exclusive mode audio, corporate restrictions)
- [ ] UIA extraction runs on background thread; ≤100ms polling interval

**Technical Notes:**
```csharp
var automation = new CUIAutomation();
var teamsWnd = automation.ElementFromHandle(teamsHwnd);
// Navigate to caption panel — requires UI tree exploration
var captionCondition = automation.CreatePropertyCondition(UIA_AutomationIdPropertyId, "CaptionText");
var captionElement = teamsWnd.FindFirst(TreeScope.Descendants, captionCondition);
string captionText = captionElement.GetCurrentPropertyValue(UIA_NamePropertyId);
```

**Note**: Teams' UI automation tree changes with every update. This approach requires maintenance on Teams major version updates. The `AutomationId` values need to be discovered via Inspect.exe or similar tools.

---

### US-06-06: Image Preprocessing Pipeline
**Story Points: 3**

**As a** developer,  
**I want** captured images pre-processed before sending to the LLM,  
**so that** costs are minimised and accuracy is maximised.

**Acceptance Criteria:**
- [ ] Images compressed to JPEG before sending (target: ≤1MB per image)
- [ ] Images resized if larger than 1920×1080 (LLM vision APIs have resolution limits)
- [ ] Whiteboard detection: if image is predominantly white with dark marks, apply contrast enhancement
- [ ] Screenshot of a code editor: detect monospace text region; apply sharpening
- [ ] Processing pipeline takes ≤300ms
- [ ] Original uncompressed image never saved to disk

**Technical Notes:**
- Image processing: `SkiaSharp` (cross-platform, fast) or `System.Drawing.Imaging`
- JPEG quality: 85% (good balance of quality vs. size for OCR/vision tasks)
- Resize: bicubic downscale if max dimension >1920px
- Whiteboard detection: histogram analysis — if >60% of pixels are near-white, apply `AutoLevels` + contrast boost

---

### US-06-07: Multimodal LLM Vision Request
**Story Points: 3**

**As a** user who has captured a whiteboard or slide,  
**I want** the AI response to answer based on both the image and the meeting audio,  
**so that** the answer is contextually aware of the ongoing discussion.

**Acceptance Criteria:**
- [ ] LLM request includes: system prompt + transcript context + captured image
- [ ] Image encoded as base64 and included in the `content` array per API spec
- [ ] System prompt addition for vision mode: "The user has captured a screen/whiteboard. Analyse the image and answer the most recent question from the transcript."
- [ ] If image contains unreadable text, LLM instructed to describe what it sees
- [ ] Response displayed in overlay same as text-only queries
- [ ] Model used: Claude Sonnet 4.6 or GPT-4o (vision-capable models only)

**Technical Notes (Anthropic):**
```csharp
var message = new Message {
    Role = "user",
    Content = new ContentBlock[] {
        new ImageContentBlock {
            Source = new Base64ImageSource { MediaType = "image/jpeg", Data = base64Image }
        },
        new TextContentBlock {
            Text = $"Meeting transcript:\n{transcriptContext}\n\nPlease answer the most recent question considering the image above."
        }
    }
};
```

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| Teams `SetWindowDisplayAffinity` blocks all capture of call window | Critical | Layered strategy: WGC for non-Teams areas + Clipboard + Image Drop + UIA text |
| Windows.Graphics.Capture requires Windows 10 1903+ | Low | Show compatibility warning on older Windows; document minimum requirement |
| LLM vision token cost (images are expensive — ~1,700 tokens for 1MP image) | Medium | Compress and resize images; offer "text extraction only" mode to reduce cost |
| User privacy: captured images may contain sensitive data | High | Never auto-save images to disk; memory-only; clear after response received |
| Snip mode overlay on multi-monitor setup | Medium | Create one full-screen overlay window per monitor; coordinate mouse position across windows |
| Teams UI Automation IDs change on app updates | Medium | Abstract UIA queries; version-detect Teams; community-sourced ID mappings; fallback gracefully |

---

## Dependencies

- EPIC-04 (Hotkeys) triggers capture
- EPIC-03 (LLM Integration) sends vision requests; multimodal providers required
- EPIC-05 (Overlay UI) displays capture confirmation and results
- EPIC-02 (Transcription) provides transcript context combined with image
- EPIC-08 (Privacy) — image capture subject to privacy mode; images never persisted
