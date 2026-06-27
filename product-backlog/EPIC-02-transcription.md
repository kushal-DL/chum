# EPIC-02: Transcription & Context Management

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-02-01 | Local Whisper Transcription | P0 — MVP | 🔴 Yet to Start | 8 |
| US-02-02 | Cloud STT Fallback | P2 — Medium | 🔴 Yet to Start | 5 |
| US-02-03 | Rolling Transcript Buffer | P0 — MVP | 🔴 Yet to Start | 5 |
| US-02-04 | Speaker Label Assignment | P1 — High | 🔴 Yet to Start | 3 |
| US-02-05 | Language Detection & Multi-language | P2 — Medium | 🔴 Yet to Start | 3 |
| US-02-06 | Transcript Cleanup & Formatting | P1 — High | 🔵 Built | 2 |
| US-02-07 | Transcript Export | P3 — Low | 🔴 Yet to Start | 2 |
| US-02-08 | Context Window Preparation for LLM | P0 — MVP | 🔴 Yet to Start | 5 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

The transcription engine converts audio buffers into text and maintains a rolling transcript that serves as conversational context for the LLM. It must be fast enough to stay ahead of real-time audio, accurate enough for technical discussions, and smart enough to manage what context gets sent to the LLM without blowing the token budget.

---

## Technical Background

### Whisper Model Options

| Model | Size | Speed (CPU) | Speed (GPU) | WER (English) | Recommendation |
|-------|------|------------|------------|----------------|----------------|
| tiny | 39MB | ~10x realtime | ~30x | ~10% | Dev/testing only |
| base | 74MB | ~7x realtime | ~20x | ~7% | Acceptable for MVP |
| small | 244MB | ~4x realtime | ~12x | ~5% | **Recommended default** |
| medium | 769MB | ~2x realtime | ~6x | ~4% | High-quality option |
| large-v3 | 1.5GB | ~1x realtime | ~3x | ~3% | Quality mode (needs GPU) |

"Realtime" means ratio of processing time to audio duration. `small` model processes 30s of audio in ~8s on a modern CPU — adequate for chunked processing.

### Chunked vs. Streaming Transcription
Whisper is not natively streaming — it transcribes a complete audio segment. The best real-time approach:
1. VAD detects a speech segment end
2. That segment (typically 5–30s) is queued for transcription
3. Whisper processes it in a background thread (2–5s for `small` model on CPU)
4. Result appended to rolling transcript
5. Net perceived latency: segment_duration + processing_time ≈ 7–35s

For lower latency: use `base` model (faster) or GPU acceleration (DirectML or CUDA).

### .NET Binding Options for Whisper
- **Whisper.NET** — C# bindings for `whisper.cpp`; maintained, NuGet available
- **WhisperSharp** — alternative binding
- **Azure Cognitive Services Speech** — cloud STT; fast, very accurate, costs ~$1/hour of audio
- **Google Cloud STT** — streaming support; costs ~$0.006/15s chunk

---

## Stories

### US-02-01: Local Whisper Transcription
**Story Points: 8**

**As a** user concerned about privacy,  
**I want** speech-to-text to run entirely on my PC,  
**so that** my meeting conversations are never sent to any third-party transcription service.

**Acceptance Criteria:**
- [ ] Whisper `small` model runs locally via Whisper.NET
- [ ] Model downloaded on first launch with progress indicator (not bundled in installer)
- [ ] Transcription accuracy ≥90% on clear English speech in a typical video call
- [ ] Transcription processes VAD-gated audio segments (not raw continuous audio)
- [ ] Transcription queue is processed on a background thread; never blocks UI or hotkey response
- [ ] GPU acceleration (CUDA or DirectML) used automatically if available
- [ ] Fallback to CPU if no compatible GPU found

**Technical Implementation:**
```csharp
var whisperFactory = WhisperFactory.FromPath("ggml-small.bin");
var processor = whisperFactory.CreateBuilder()
    .WithLanguage("auto")
    .WithSegmentEventHandler(seg => AppendToTranscript(seg))
    .Build();

// Called from transcription queue consumer
await processor.ProcessAsync(audioSamples_float32_16kHz);
```

**Model Download:**
- Host model files on GitHub Releases or a CDN
- Verify SHA256 after download before use
- Store in `%LOCALAPPDATA%\Chum\Models\`

**Challenges & Workarounds:**

| Challenge | Workaround |
|-----------|------------|
| First-run latency: model cold start ~2–5s | Load model on app start in background; show "warming up" status; queue audio, don't drop it |
| GPU not available or incompatible | Detect via `DirectML` device enumeration; gracefully fall back to CPU |
| Model file corrupted | SHA256 check on each launch; re-download if mismatch |
| Long segments (>30s) cause Whisper to hallucinate | Cap VAD segments at 25s; force-flush if segment exceeds this |

---

### US-02-02: Cloud STT Fallback
**Story Points: 5**

**As a** user on a low-powered machine,  
**I want** the option to use cloud speech-to-text,  
**so that** Chum is usable even when local Whisper is too slow.

**Acceptance Criteria:**
- [ ] Azure Cognitive Services Speech SDK supported as cloud STT option
- [ ] Cloud STT can be selected in settings; requires API key
- [ ] Cloud STT uses streaming recognition (word-by-word results as they arrive)
- [ ] Clear indication in UI when cloud STT is active (privacy warning)
- [ ] Fallback: if cloud call fails, app falls back to local Whisper with notification

**Technical Notes:**
- Azure Streaming: `SpeechRecognizer` with `RecognizeOnceAsync()` in a loop, or use `StartContinuousRecognitionAsync()`
- Azure cost: ~$1 per hour of audio — surface estimated cost in settings UI
- Send loopback and mic as separate streams to Azure; merge by timestamp
- Store Azure key in Windows Credential Manager (never in settings file)

---

### US-02-03: Rolling Transcript Buffer
**Story Points: 5**

**As a** developer,  
**I want** a time-indexed transcript store,  
**so that** when the hotkey fires, I can extract the last N minutes of conversation as context.

**Acceptance Criteria:**
- [ ] Transcript stored as ordered list of `TranscriptSegment` with timestamps and speaker source
- [ ] Configurable retention window: 5, 10, 15, or 30 minutes
- [ ] Query API: `GetTranscriptSince(DateTimeOffset start)` returns formatted text
- [ ] Oldest segments evicted automatically when window exceeded
- [ ] Full transcript text for 30-min window ≤2MB in memory

**TranscriptSegment Model:**
```csharp
record TranscriptSegment(
    DateTimeOffset Timestamp,
    AudioSource Source,       // Loopback or Mic
    string Text,
    float Confidence          // 0-1 from Whisper
);
```

**Formatted output for LLM context:**
```
[14:23:05] Remote: "Can you walk us through the authentication flow?"
[14:23:18] Me: "Sure, so the token is issued by the identity service and..."
[14:23:45] Remote: "What happens when the token expires?"
```

---

### US-02-04: Speaker Label Assignment
**Story Points: 3**

**As a** user reading the transcript,  
**I want** segments labelled as "Me" vs. "Remote",  
**so that** the LLM and I can understand who said what.

**Acceptance Criteria:**
- [ ] Segments from microphone stream labelled "Me"
- [ ] Segments from loopback stream labelled "Remote" (or configurable name)
- [ ] Labels appear in transcript view and in context sent to LLM
- [ ] Multi-participant detection: if loopback segments are clearly from different voices, label as "Remote 1", "Remote 2" (stretch goal — requires diarization)

**Technical Notes:**
- Basic version: source stream determines label (mic = Me, loopback = Remote)
- Advanced diarization: use `pyannote.audio` via a local Python sidecar, or `NeMo` ONNX export — defer to v0.3
- Even basic source-based labelling significantly improves LLM response quality

---

### US-02-05: Language Detection and Multi-language Support
**Story Points: 3**

**As a** user in multilingual meetings,  
**I want** Whisper to detect language automatically,  
**so that** I don't need to configure the language when participants switch.

**Acceptance Criteria:**
- [ ] Whisper runs with `Language = "auto"` by default
- [ ] Detected language shown in status bar
- [ ] User can override with a fixed language in settings (improves accuracy for known monolingual calls)
- [ ] LLM query always sent in English regardless of meeting language (option: preserve original language)

**Technical Notes:**
- Whisper auto-detects language from first 30s of audio; subsequent segments inherit
- Setting `Language = "en"` forces English and improves speed ~10% by skipping detection
- For LLM query: add system prompt instruction "The transcript may contain [detected_language]; respond in English"

---

### US-02-06: Transcript Cleanup and Formatting
**Story Points: 2**

**As a** user sending transcript to the LLM,  
**I want** the transcript text to be clean and readable,  
**so that** the LLM gets high-quality context and responses are accurate.

**Acceptance Criteria:**
- [ ] Whisper hallucinations (repeated phrases, "[Music]", "[Applause]") are filtered
- [ ] Filler words ("um", "uh", "like") optionally filtered (off by default)
- [ ] Overlapping timestamps deduplicated (if mic and loopback overlap within 500ms)
- [ ] Transcript is normalised: proper capitalisation, basic punctuation preserved
- [ ] Maximum 3 consecutive blank lines compressed to 1

**Technical Notes:**
- Hallucination filter: regex patterns for common Whisper artifacts + similarity check (Levenshtein distance) against previous segment
- Deduplication: if `loopback_segment.Text` has cosine similarity >0.85 with `mic_segment.Text` within ±2s, keep mic version only

---

### US-02-07: Transcript Export
**Story Points: 2**

**As a** user who wants a record of a meeting,  
**I want** to export the transcript to a text or markdown file,  
**so that** I can share meeting notes or review them later.

**Acceptance Criteria:**
- [ ] Export as plain text (.txt) or Markdown (.md)
- [ ] Export includes timestamps and speaker labels
- [ ] Export triggered from system tray menu or settings
- [ ] File saved to user-chosen location (file picker dialog)
- [ ] Transcript cleared from memory after export if user opts in

**Note:** Transcript export is a separate user action — Chum never automatically saves transcripts to disk for privacy reasons (see EPIC-08).

---

### US-02-08: Context Window Preparation for LLM
**Story Points: 5**

**As a** developer,  
**I want** a smart context extraction function,  
**so that** the LLM receives the most relevant transcript without exceeding token limits.

**Acceptance Criteria:**
- [ ] Context extraction respects a configurable token budget (default: 8,000 tokens for context)
- [ ] Most recent segments always included; older segments included if budget allows
- [ ] If full transcript exceeds budget, oldest segments are summarised (via a cheap LLM call) and prepended as a summary
- [ ] The 30 seconds prior to hotkey press are always fully included (the "immediate context")
- [ ] Function completes in ≤200ms (does not block hotkey response)

**Summarisation Strategy:**
```
Full transcript → token count check
  If ≤ budget: send as-is
  If > budget:
    Split into "old" (>5min ago) and "recent" (<5min ago)
    Summarise "old" portion with LLM: "Summarise this meeting excerpt in 3 bullet points: ..."
    Cache summary (invalidated when old portion changes)
    Combine: [Summary] + [Recent full transcript]
```

**Token Estimation:** Rough estimate: 1 token ≈ 4 characters ≈ 0.75 words. Use `TiktokenSharp` or Claude's token counter for precise counts.

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| Whisper hallucinations on silence/noise | Medium | VAD gating prevents processing silent segments; post-filter for known hallucination patterns |
| Transcription latency (5–10s behind real-time on CPU) | High | Use GPU if available; use `base` model for lower latency; `small` for accuracy — make configurable |
| Long meeting context exceeds LLM token limit | High | Rolling window + background summarisation; see US-02-08 |
| Technical jargon / acronyms mistranscribed | Medium | Whisper supports initial prompt ("prompt engineering for Whisper"): provide domain vocabulary as initial context |
| Multiple languages in one call | Low | Whisper handles this well in auto mode; document limitation for code-switching within a sentence |

---

## Dependencies

- EPIC-01 (Audio Engine) provides audio buffers consumed here
- EPIC-03 (LLM Integration) consumes the formatted transcript context produced here
- EPIC-07 (Settings) provides model selection, cloud STT keys, retention window config
- EPIC-08 (Privacy) controls whether transcript is retained at all
