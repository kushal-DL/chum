# EPIC-01: Core Audio Engine

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-01-01 | Capture System Audio Loopback | P0 — MVP | ✅ Done (Built & Tested) | 5 |
| US-01-02 | Capture Microphone Audio | P0 — MVP | ✅ Done (Built & Tested) | 3 |
| US-01-03 | Audio Device Selection | P1 — High | ✅ Done (Built & Tested) | 3 |
| US-01-04 | Voice Activity Detection | P0 — MVP | ✅ Done (Built & Tested) | 8 |
| US-01-05 | Audio Ring Buffer | P0 — MVP | ✅ Done (Built & Tested) | 5 |
| US-01-06 | Real-time Audio Level Meters | P2 — Medium | ✅ Done (Built & Tested) | 2 |
| US-01-07 | Automatic Device Failover | P1 — High | ✅ Done (Built & Tested) | 3 |
| US-01-08 | Noise Suppression | P1 — High | ✅ Done (Built & Tested) | 3 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

The audio engine is the foundation of Chum. It captures two audio streams simultaneously:
- **System loopback** — everything playing through your speakers (remote meeting participants)
- **Microphone** — your own voice

Streams are filtered through Voice Activity Detection (VAD), timestamped, and placed in a shared rolling buffer consumed by the transcription engine.

---

## Technical Background

### Windows Audio API Landscape

| API | Latency | Loopback Support | Recommendation |
|-----|---------|-----------------|----------------|
| MME | ~100ms | No | Avoid (legacy) |
| DirectSound | ~50ms | No | Avoid (legacy) |
| WASAPI Shared | ~10ms | Yes (loopback flag) | **Use this** |
| WASAPI Exclusive | ~3ms | No — app owns device | Cannot use for loopback |
| ASIO | <1ms | No | Pro audio only |

### WASAPI Loopback Capture
WASAPI supports loopback via `AUDCLNT_STREAMFLAGS_LOOPBACK`. This captures everything routed to the selected render endpoint — including Teams/Meet audio. Library: **NAudio** (`WasapiLoopbackCapture`).

**Critical constraint**: If Teams uses a specific output device (not the Windows default), loopback on the default device misses it. The app must detect and follow the active meeting audio device.

### Audio Format for STT
Whisper expects: **16 kHz, 16-bit, mono PCM**. NAudio's `MediaFoundationResampler` handles format conversion from whatever WASAPI delivers (typically 48 kHz float32 stereo).

---

## Stories

### US-01-01: Capture System Audio Loopback
**Story Points: 5**

**As a** user participating in a video call,  
**I want** the app to capture all audio playing through my speakers,  
**so that** remote participants' speech is included in my AI context without me typing anything.

**Acceptance Criteria:**
- [ ] App captures WASAPI loopback from the Windows default audio output device
- [ ] Captured audio is resampled to 16 kHz / 16-bit / mono PCM
- [ ] Audio capture starts on app launch (configurable in settings)
- [ ] Buffer receives data in ≤100ms chunks
- [ ] Capture survives audio dropouts ≤2s with automatic reconnect
- [ ] Loopback capture CPU usage ≤1% on modern hardware

**Technical Implementation:**
```csharp
var capture = new WasapiLoopbackCapture(); // default render device
capture.WaveFormat = new WaveFormat(48000, 16, 2); // capture at device native rate
capture.DataAvailable += (s, e) => {
    var resampled = Resample(e.Buffer, e.BytesRecorded, to: WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
    audioBuffer.Enqueue(new AudioChunk(resampled, AudioSource.Loopback, DateTime.UtcNow));
};
capture.StartRecording();
```

**Challenges & Workarounds:**

| Challenge | Workaround |
|-----------|------------|
| Exclusive mode audio driver — `AUDCLNT_E_DEVICE_IN_USE` | Detect on startup; prompt user to install VB-Cable (free virtual audio device); route output through it |
| Teams using non-default output device | Enumerate active sessions via `IAudioSessionManager2`; detect which device the meeting app is using |
| App crashes leave audio handle open | Use `IDisposable` + finalizer; write device ID to registry; on next launch, check and release stale handle |

---

### US-01-02: Capture Microphone Audio
**Story Points: 3**

**As a** user speaking in a meeting,  
**I want** the app to capture my voice independently,  
**so that** questions I ask are also included in the AI's context.

**Acceptance Criteria:**
- [ ] App captures from default Windows microphone input
- [ ] Mic and loopback streams captured independently on separate threads
- [ ] Both streams use the same timestamp reference (`Environment.TickCount64`)
- [ ] App fails gracefully if microphone access is denied in Windows Privacy Settings (shows clear error)
- [ ] Capturing via Chum does NOT affect the Teams/Meet mic — they are independent WASAPI sessions

**Technical Notes:**
- `WasapiCapture` (not loopback) for microphone
- Request mic permission: check `AppCapabilityAccessStatus` via Windows Runtime APIs
- Both capture threads write to the same `Channel<AudioChunk>` with `Source` tag
- Chum uses a separate WASAPI session; Teams uses its own — no interference

**Challenges & Workarounds:**

| Challenge | Workaround |
|-----------|------------|
| Echo: mic picks up speakers (no headset) | Apply Windows AEC DSP via `Voice Capture DSP` COM object; or detect duplicate content between loopback and mic transcript within ±2s window |
| Bluetooth HFP mode drops mic to 8 kHz | Detect `WaveFormat.SampleRate < 16000`; warn user "Bluetooth mic quality too low for transcription — use USB or 3.5mm headset" |
| Windows mutes mic via privacy policy | Detect `DeviceState` as `NotPresent` or `Disabled`; show actionable error linking to Privacy Settings |

---

### US-01-03: Audio Device Selection
**Story Points: 3**

**As a** power user with multiple audio interfaces,  
**I want** to choose which output and input devices Chum monitors,  
**so that** capture is correct when I'm not using Windows defaults.

**Acceptance Criteria:**
- [ ] Settings lists all active WASAPI render endpoints (for loopback)
- [ ] Settings lists all active WASAPI capture endpoints (for microphone)
- [ ] Selection persists across restarts (stored by device ID, not display name)
- [ ] If saved device is unavailable at startup, app falls back to Windows default and shows a notification
- [ ] Device list refreshes automatically on hot-plug (USB audio dongles, etc.)

**Technical Notes:**
- Enumerate: `new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)`
- Hot-plug: implement `IMMNotificationClient` (`OnDeviceAdded`, `OnDeviceRemoved`, `OnDefaultDeviceChanged`)
- Store `device.ID` (a GUID-like string) in settings — display names are localised and unstable

---

### US-01-04: Voice Activity Detection (VAD)
**Story Points: 8**

**As a** user who wants fast, low-cost operation,  
**I want** the app to process only speech segments,  
**so that** silence and noise don't fill the transcript with garbage or waste API calls.

**Acceptance Criteria:**
- [ ] VAD runs on both streams independently
- [ ] ≥95% speech-detection accuracy on typical office/home/call audio
- [ ] Segments shorter than 200ms are filtered (prevents false triggers on clicks, etc.)
- [ ] VAD latency ≤20ms (runs faster than real-time on CPU)
- [ ] VAD CPU usage ≤0.5% on a modern i5/Ryzen 5
- [ ] Pre-buffer: 300ms before VAD trigger is prepended (avoids clipping word starts)
- [ ] Post-buffer: 500ms of trailing silence appended (avoids clipping word ends)

**Technical Implementation:**
- Model: **Silero VAD v4** (ONNX, ~1.7MB)
- Runtime: `Microsoft.ML.OnnxRuntime` NuGet package
- Input: 30ms or 60ms PCM chunks at 16 kHz
- Output: float probability 0–1; threshold 0.5 for onset, 0.35 for offset (hysteresis)
- State: model is stateful — maintain separate `InferenceSession` per stream

```csharp
// Pseudo-code
var session = new InferenceSession("silero_vad.onnx");
float[] state = new float[2 * 1 * 128]; // h and c states
float prob = RunVad(session, chunk_16kHz, ref state);
bool isSpeech = prob > (currentlySpeaking ? 0.35f : 0.5f);
```

**Challenges & Workarounds:**

| Challenge | Workaround |
|-----------|------------|
| Silero requires contiguous 30ms chunks; WASAPI delivers variable-size buffers | Buffer incoming PCM into a `Queue<byte[]>`; dispatch exactly 30ms frames to VAD |
| ONNX Runtime cold-start (~100ms) blocks first transcription | Warm up model on background thread at startup; queue audio until ready |

---

### US-01-05: Audio Ring Buffer
**Story Points: 5**

**As a** developer,  
**I want** a thread-safe ring buffer holding recent audio,  
**so that** when the hotkey is pressed, the transcription engine has up to 10 minutes of context available.

**Acceptance Criteria:**
- [ ] Configurable retention: 5, 10, 15, or 30 minutes
- [ ] Memory usage for 10-min buffer ≤30MB (16 kHz / 16-bit / mono = ~1.9MB/min)
- [ ] Thread-safe: multiple readers (STT engine), one writer per stream (capture threads)
- [ ] Buffer overflow drops oldest chunks, never newest
- [ ] Supports query: "give me all chunks from the last N seconds"

**Technical Notes:**
- Use `System.Threading.Channels.Channel<AudioChunk>` (bounded, `BoundedChannelFullMode.DropOldest`)
- `AudioChunk` record: `{ ReadOnlyMemory<float> Samples, AudioSource Source, DateTimeOffset Timestamp }`
- Separate channels for loopback and mic; merged by timestamp at query time
- Background eviction not needed — `Channel` drops oldest automatically when bounded capacity reached
- Capacity = `(sampleRate * windowSeconds * sizeof(float))` bytes / `chunkSizeBytes`

---

### US-01-06: Real-time Audio Level Meters
**Story Points: 2**

**As a** user setting up before a meeting,  
**I want** to see live audio level bars for both streams,  
**so that** I can confirm capture is working before the call starts.

**Acceptance Criteria:**
- [ ] Two level meters (loopback and mic) visible in the main settings panel and a compact overlay indicator
- [ ] Meters update at ≥20fps
- [ ] Meters show RMS level in dBFS; typical speech range −30 to −6 dBFS is marked green
- [ ] VAD state shown as a separate indicator (speech detected vs. silence)
- [ ] Clipping indicator (>0 dBFS) shown in red

**Technical Notes:**
- RMS calculation: `sqrt(sum(sample^2) / count)` over 50ms windows
- dBFS: `20 * log10(rms)` — clamp to −60 dBFS floor for display
- WPF: use a `DispatcherTimer` + `Canvas`-drawn bar or a dedicated `DrawingVisual`; avoid re-layout per frame

---

### US-01-07: Automatic Device Failover
**Story Points: 3**

**As a** user who connects/disconnects headsets mid-meeting,  
**I want** Chum to automatically follow the new default audio device,  
**so that** I do not miss any conversation when my audio setup changes.

**Acceptance Criteria:**
- [ ] Device change detected within 1 second via `IMMNotificationClient`
- [ ] Old capture session torn down cleanly (no orphaned COM handles)
- [ ] New session started within 500ms of detection
- [ ] Brief overlap (≤1s) in captured audio is acceptable; gaps are not
- [ ] Non-intrusive toast notification in overlay: "Switched audio capture to [device name]"

**Technical Notes:**
- 500ms debounce on `OnDefaultDeviceChanged` — Windows fires multiple events per switch
- Use `SemaphoreSlim(1,1)` to prevent concurrent restart attempts
- Dispose `WasapiLoopbackCapture` before creating new instance (WASAPI doesn't allow two sessions on same device from same process)

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Workaround |
|-----------|----------|------------|
| WASAPI Exclusive Mode (`AUDCLNT_E_DEVICE_IN_USE`) | High — capture impossible | Detect; prompt to install VB-Cable; redirect audio through virtual device |
| Teams using dedicated audio device (not Windows default) | Medium — misses Teams audio | Enumerate all active audio sessions; follow Teams' device |
| Bluetooth HFP mic (8 kHz) | Medium — poor transcript quality | Detect sample rate; warn user; suggest USB/3.5mm |
| Corporate VPN virtual audio driver | Low — rare; may intercept loopback | Detect via audio quality metrics; fallback to mic-only mode with user notification |
| Echo when user is on speakers | Medium — duplicates remote speech | Apply AEC or detect duplicate content in merged transcript |

---

## Dependencies

- EPIC-02 (Transcription) consumes the audio buffer produced here
- EPIC-07 (Settings) provides device selection configuration
- EPIC-08 (Privacy) provides the "pause capture" signal
