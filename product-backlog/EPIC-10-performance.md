# EPIC-10: Performance & Reliability

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-10-01 | Audio Pipeline Latency Profiling | P1 — High | 🔴 Yet to Start | 3 |
| US-10-02 | CPU Usage Optimisation | P1 — High | 🔴 Yet to Start | 5 |
| US-10-03 | GPU Acceleration for Whisper | P2 — Medium | 🔴 Yet to Start | 5 |
| US-10-04 | Memory Management for Long Meetings | P1 — High | 🔴 Yet to Start | 5 |
| US-10-05 | Graceful Error Recovery | P1 — High | 🔴 Yet to Start | 5 |
| US-10-06 | End-to-End Latency Benchmark | P2 — Medium | 🔴 Yet to Start | 3 |
| US-10-07 | App Startup Performance | P2 — Medium | 🔴 Yet to Start | 3 |
| US-10-08 | Crash Reporting (Opt-in) | P3 — Low | 🔴 Yet to Start | 3 |
| US-10-09 | Auto-Update Mechanism | P2 — Medium | 🔴 Yet to Start | 5 |
| US-10-10 | Low-Power Mode | P3 — Low | 🔴 Yet to Start | 3 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

Chum runs continuously in the background during meetings that can last hours. It must not:
- Noticeably slow down the user's machine
- Drain the battery on a laptop
- Introduce latency between pressing the hotkey and seeing a response
- Crash and lose context mid-meeting

This epic defines performance targets, profiling strategies, and reliability hardening.

---

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| CPU (idle, capturing) | ≤3% | Background capture + VAD running |
| CPU (active transcription) | ≤15% | Whisper `small` model on CPU |
| CPU (GPU transcription) | ≤5% | GPU offloads Whisper; CPU stays low |
| RAM baseline | ≤150MB | App idle, no transcript |
| RAM during 1h meeting | ≤300MB | With 30-min rolling buffer + transcript |
| Audio pipeline latency | ≤50ms | Capture → VAD → queue; no perceptible lag |
| Transcription latency (p50) | ≤5s | VAD segment end → text appears in transcript |
| Transcription latency (p90) | ≤10s | For longer segments |
| LLM first token (p50) | ≤2s | Hotkey release → first word in overlay |
| LLM first token (p90) | ≤4s | Under normal network conditions |
| Screen capture latency | ≤500ms | Hotkey → captured image ready for LLM |
| App startup time | ≤3s | Cold start to overlay visible |

---

## Stories

### US-10-01: Audio Pipeline Latency Profiling
**Story Points: 3**

**As a** developer,  
**I want** instrumented latency measurements through the audio pipeline,  
**so that** I can identify and eliminate bottlenecks between audio capture and transcript output.

**Acceptance Criteria:**
- [ ] Each stage tagged with timestamp: `Captured → VAD → Queued → TranscriptionStart → TranscriptionEnd → TranscriptAppended`
- [ ] Latency percentiles (p50, p90, p99) reported in Diagnostics panel
- [ ] Latency data logged to a rolling buffer (last 1000 segments); accessible via diagnostics
- [ ] Alert in overlay if transcription latency exceeds 15s consistently (may indicate model overload)

**Technical Notes:**
```csharp
record PipelineEvent(string Stage, DateTimeOffset Timestamp, Guid SegmentId);
// Correlate events by SegmentId; compute stage-to-stage deltas
```

---

### US-10-02: CPU Usage Optimisation
**Story Points: 5**

**As a** user on a mid-range laptop,  
**I want** Chum to use minimal CPU in the background,  
**so that** my machine stays responsive and my battery lasts through a full day.

**Acceptance Criteria:**
- [ ] Whisper `small` model transcription CPU ≤15% on Intel Core i5-11th gen or equivalent
- [ ] VAD processing CPU ≤0.5% continuously
- [ ] Audio capture threads use `Thread.Sleep(5)` or `WaitHandle` instead of busy-wait
- [ ] UI rendering: WPF overlay ≤1% CPU when idle (no response streaming)
- [ ] Background model warm-up deferred to 5 seconds after launch (not on UI startup critical path)

**Optimisation Techniques:**
- Process Whisper on a `ThreadPool` thread; never on UI thread
- VAD runs on dedicated capture threads; results dispatched via `Channel`
- WPF overlay: use `RenderOptions.BitmapScalingMode = NearestNeighbor` for icons; avoid layout triggers in hot paths
- Consider `System.Threading.Thread.Priority.BelowNormal` for transcription thread (background, non-time-critical)

---

### US-10-03: GPU Acceleration for Whisper
**Story Points: 5**

**As a** user with a discrete GPU,  
**I want** Whisper to use my GPU for transcription,  
**so that** transcription is fast and CPU stays available for other work.

**Acceptance Criteria:**
- [ ] App detects CUDA-capable GPU (NVIDIA) and DirectML-capable GPU (AMD, Intel Arc, any DX12 GPU)
- [ ] Whisper.NET uses GPU automatically if detected
- [ ] Fallback to CPU if GPU is busy or VRAM insufficient (Whisper `small` needs ~500MB VRAM)
- [ ] Settings show: "GPU: [Name] | Mode: CUDA/DirectML/CPU"
- [ ] GPU mode reduces transcription time to ≤2s for a 20s segment (vs. ≤8s CPU)

**Technical Notes:**
- `Whisper.NET` supports CUDA and CoreML (Mac); for DirectML, `OnnxRuntime` with `DirectMLExecutionProvider`
- VRAM check: query `IDXGIAdapter` for `DedicatedVideoMemory`; warn if <1GB available
- CUDA: requires CUDA Toolkit installed; check `nvml.dll` presence; fail gracefully if absent

---

### US-10-04: Memory Management for Long Meetings
**Story Points: 5**

**As a** user in a 3-hour meeting,  
**I want** Chum to maintain stable memory usage throughout,  
**so that** the app does not bloat or crash during long sessions.

**Acceptance Criteria:**
- [ ] Memory usage after 3h of continuous meeting ≤400MB
- [ ] Audio buffer bounded (see EPIC-01); old chunks evicted on schedule
- [ ] Transcript bounded by rolling window; old segments evicted when limit exceeded
- [ ] Response history bounded: keep last 10 responses only
- [ ] No memory leaks in audio capture/transcription loops (verified over 4h test)
- [ ] `GC.Collect()` called periodically (every 10 min) after heavy transcription burst

**Testing:**
- Automated test: run Chum in silence for 4h; measure memory every 60s; assert ≤ target
- Profiling: use `dotMemory` or `PerfView` to identify allocation hot paths

---

### US-10-05: Graceful Error Recovery
**Story Points: 5**

**As a** user in a 2-hour meeting,  
**I want** Chum to recover automatically from non-fatal errors,  
**so that** a temporary API outage or audio glitch does not end my session.

**Acceptance Criteria:**
- [ ] Audio capture thread restart on failure: automatic, within 2s, logged, user notified via status bar (not modal)
- [ ] LLM API failure: retry with exponential backoff (1s, 2s, 4s); show "Retrying..." in overlay; fail gracefully after 3 attempts
- [ ] Transcription thread crash: restart from fresh state; log previous model state
- [ ] Screen capture failure: log and show "Capture failed: [reason]" — do not crash app
- [ ] All catch blocks log exception type + message (never swallow silently)
- [ ] Global unhandled exception handler: show "Chum encountered an error — transcript preserved; click to restart" rather than crashing to Windows error dialog

**Technical Notes:**
```csharp
// Global handler
AppDomain.CurrentDomain.UnhandledException += (s, e) => {
    var ex = (Exception)e.ExceptionObject;
    logger.Error(ex, "Unhandled exception");
    ExportEmergencyTranscript(); // Save transcript to temp file before crash
    ShowRestartDialog();
};
```

---

### US-10-06: End-to-End Latency Benchmark
**Story Points: 3**

**As a** developer,  
**I want** an automated end-to-end latency test,  
**so that** I can detect regressions in the "hotkey → response" pipeline.

**Acceptance Criteria:**
- [ ] Automated test plays a known audio clip through a virtual audio device
- [ ] Test measures: audio play start → transcript appears → hotkey simulated → first LLM token in overlay
- [ ] Test runs with mocked LLM (returns immediately) to isolate pipeline latency from network
- [ ] Test runs with real LLM to measure true end-to-end (nightly CI job)
- [ ] Regression alert: if pipeline latency increases by >20% vs. baseline, CI fails

---

### US-10-07: App Startup Performance
**Story Points: 3**

**As a** user launching Chum before a meeting,  
**I want** the app to be ready within 3 seconds of clicking the icon,  
**so that** I am not waiting for it to initialise when I need it.

**Acceptance Criteria:**
- [ ] Time from process start to "overlay visible and hotkey active" ≤3s on a mid-range machine
- [ ] Whisper model load deferred: overlay shows "Warming up transcription..." for up to 10s after launch; hotkey still works (shows "Transcription starting...") 
- [ ] .NET 8 single-file publish with NativeAOT considered for startup improvement
- [ ] Splash screen shown during cold start if load exceeds 2s

**Technical Notes:**
- Measure startup: `Stopwatch` from `static void Main()` to `overlay.Show()`
- Parallelise startup tasks: audio init, model load, credential load can all run in parallel
- Use `Task.WhenAll(initAudio, loadModel, loadCredentials)`

---

### US-10-08: Crash Reporting (Opt-in)
**Story Points: 3**

**As a** developer,  
**I want** opt-in crash reporting,  
**so that** I can fix bugs I wouldn't otherwise know about.

**Acceptance Criteria:**
- [ ] On first launch: "Help improve Chum by sending anonymous crash reports?" Yes / No / Ask later
- [ ] If yes: unhandled exceptions sent to a crash reporting service (Sentry free tier or custom endpoint)
- [ ] Crash report contains: exception type, stack trace, OS version, app version, CPU/RAM info
- [ ] Crash report NEVER contains: transcript content, audio data, screen captures, API keys, file names
- [ ] User can withdraw consent at any time in Settings > Privacy
- [ ] "View what would be sent" link shows a sample crash report in plain text

---

### US-10-09: Auto-Update Mechanism
**Story Points: 5**

**As a** user,  
**I want** Chum to update itself when new versions are available,  
**so that** I always have the latest bug fixes and features without manual reinstallation.

**Acceptance Criteria:**
- [ ] Check for updates on launch (configurable: always / daily / never)
- [ ] Update available: non-intrusive notification in tray: "Chum v1.2 available — Update now"
- [ ] User-initiated update: downloads installer, verifies SHA256 signature, runs installer, Chum restarts
- [ ] Auto-update: optional; when enabled, downloads and installs during inactivity periods
- [ ] Update cannot proceed while a meeting is in progress (audio capture active)
- [ ] Update mechanism uses HTTPS; installer signed with Authenticode certificate
- [ ] Rollback: if new version fails to start, installer re-installs previous version

**Technical Notes:**
- Update check: `GET https://api.github.com/repos/{owner}/chum/releases/latest` (GitHub Releases)
- Installer download: verified against release asset SHA256
- Installer framework: WiX Toolset or MSIX for clean install/update/uninstall

---

### US-10-10: Low-Power Mode
**Story Points: 3**

**As a** user on a laptop running on battery,  
**I want** Chum to reduce its resource usage,  
**so that** it does not noticeably impact my battery life.

**Acceptance Criteria:**
- [ ] "Low power mode" automatically activates when on battery (detects via `System.Windows.Forms.SystemInformation.PowerStatus`)
- [ ] In low power mode: switch from `small` Whisper model to `base`; increase VAD segment size to reduce Whisper call frequency
- [ ] CPU usage target in low power mode: ≤5% total
- [ ] User can manually toggle low power mode in settings
- [ ] Status bar indicator: "Low power mode active" when on battery

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| Whisper model warm-up blocks perceived startup | High | Defer model load; accept hotkey with "transcription starting" UI state |
| Memory leaks in long-running audio capture loops | High | Bounded buffers; periodic GC; 4h stress test in CI |
| NVIDIA CUDA not installed on most end-user machines | Medium | DirectML as primary GPU path (requires only DX12 driver); CUDA optional |
| WPF rendering overhead on high-refresh-rate displays | Low | Set `RenderOptions` appropriately; overlay rarely needs >30fps |
| Auto-update breaking active meeting session | Medium | Block update check/install while audio capture active |
| Crash reporting privacy (transcript in stack trace) | High | Sanitise crash reports; never include string variables from transcript pipeline |

---

## Dependencies

- All epics — performance affects every pipeline component
- EPIC-02 (Transcription) — biggest CPU consumer; GPU acceleration here has largest impact
- EPIC-05 (Overlay UI) — WPF rendering performance
- EPIC-01 (Audio Engine) — audio thread stability
