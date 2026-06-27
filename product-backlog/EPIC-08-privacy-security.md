# EPIC-08: Privacy, Security & Compliance

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-08-01 | Local-only Processing Mode | P1 — High | 🔴 Yet to Start | 5 |
| US-08-02 | Audio Buffer Auto-Purge | P0 — MVP | 🔴 Yet to Start | 3 |
| US-08-03 | Transcript Retention Controls | P1 — High | 🔴 Yet to Start | 3 |
| US-08-04 | Meeting Participant Disclosure Reminder | P2 — Medium | 🔴 Yet to Start | 2 |
| US-08-05 | Privacy Pause Mode | P1 — High | 🔴 Yet to Start | 3 |
| US-08-06 | Secure API Key Storage | P0 — MVP | 🔴 Yet to Start | 3 |
| US-08-07 | Screen Capture Privacy Safeguards | P1 — High | 🔴 Yet to Start | 2 |
| US-08-08 | Network Traffic Transparency | P2 — Medium | 🔴 Yet to Start | 3 |
| US-08-09 | Audit Log | P3 — Low | 🔴 Yet to Start | 2 |
| US-08-10 | Windows Service Installer (Admin-Elevated) | P1 — High | 🔴 Yet to Start | 5 |
| US-08-11 | Process Identity — Run as Named System Service | P1 — High | 🔴 Yet to Start | 5 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

Chum listens to your meetings. This creates significant privacy obligations — both towards the user themselves and towards other meeting participants who may not know Chum is running. This epic ensures the app handles data responsibly by default, gives users full control, and avoids creating the kind of "always-on recorder" that could create legal or employment issues.

**Core privacy principle: Chum is a real-time assistant, not a surveillance tool. Data is processed in memory and discarded. Nothing is logged, stored, or uploaded unless the user explicitly opts in.**

---

## Legal & Policy Context

Users should be aware (and the app should remind them) of:

1. **Meeting recording consent laws**: In many jurisdictions, recording a phone/video call without informing all parties is illegal (e.g., US states with two-party consent, GDPR Article 6 in EU).
   - Chum does NOT record audio to disk by default — it transcribes and discards the audio buffer
   - The transcript is a derivative work; whether it constitutes "recording" varies by jurisdiction
   - **Safe practice**: Tell participants "I'm using an AI assistant" — the same way you'd tell them you're taking notes

2. **Employer IT policies**: Corporate environments may have policies against third-party audio capture tools. Chum does not bypass corporate controls, but users should review their employer's policy.

3. **Data residency**: Cloud LLM/STT providers process data in their datacenters. For sensitive business content, the local-only mode (Whisper + Ollama) ensures data stays on-device.

---

## Non-Goals — Explicitly Out of Scope

Chum is a meeting co-pilot, **not an evasion tool**. There is a hard line between *keeping the user's own overlay private in calls they control* (in scope) and *hiding the application from software whose job is to detect it* (out of scope). The following will **not** be built:

- ❌ Hiding the Chum process from the OS, Task Manager, process/window enumeration, or endpoint security / EDR / DLP agents.
- ❌ Defeating, disabling, or evading remote-proctoring, exam-lockdown, kiosk, or anti-cheat software (e.g., tools that lock down or monitor a candidate's machine during an assessment or interview).
- ❌ Operating as a concealed background agent on a managed/corporate device to avoid compliance or monitoring software.
- ❌ Any rootkit-like, process-injection, or anti-forensic technique whose purpose is to avoid detection by security software.

**What IS in scope (and legitimate):**

- ✅ **Overlay capture exclusion** — the user's *own* overlay window not appearing in the user's *own* screen shares / recordings, via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` and/or auto-hide on screen-share detection ([US-05-07](EPIC-05-overlay-ui.md)). This protects the user's private UI in meetings they host. It does **not** hide the app from the OS or from any monitoring software — Chum still appears in Task Manager, still makes visible network calls, and is fully inspectable.
- ✅ Running quietly minimized to the system tray with a low CPU/RAM footprint.

**Rationale:** Chum's value depends on being a *trustworthy* assistant. Building it to defeat integrity or monitoring controls would (a) typically violate the terms of service or rules of the platforms involved, (b) reframe a productivity tool as a cheating/circumvention tool, and (c) start an arms race against security vendors that is unwinnable and off-mission. The privacy posture here is about **minimizing data retention and keeping the user's own UI private** — never about evading legitimate oversight.

---

## Stories

### US-08-01: Local-Only Processing Mode
**Story Points: 5**

**As a** user in a confidential business meeting,  
**I want** all audio processing and AI inference to happen on my machine,  
**so that** meeting content never leaves my device.

**Acceptance Criteria:**
- [ ] "Local only" toggle in settings — when on, cloud APIs are disabled entirely
- [ ] In local-only mode: Whisper runs locally; Ollama required for LLM
- [ ] If Ollama is not running in local-only mode, Chum shows a clear error and refuses to send data
- [ ] A persistent "LOCAL MODE" badge visible in overlay when local-only is active
- [ ] No network calls made in local-only mode (verifiable via Wireshark / Windows Firewall rules)
- [ ] Local-only mode documented in settings with privacy explanation

**Technical Notes:**
- Implement as a compile-time flag-checkable runtime mode; inject `ILlmProvider` and `ISttProvider` as null-returning stubs when local-only is on and required service unavailable
- Consider shipping a Windows Firewall rule script users can apply to block Chum network access at OS level (extra assurance for paranoid users)

---

### US-08-02: Audio Buffer Auto-Purge
**Story Points: 3**

**As a** user who finishes a meeting,  
**I want** the raw audio buffer to be automatically cleared,  
**so that** my conversations are not retained in memory after I no longer need them.

**Acceptance Criteria:**
- [ ] Raw audio PCM buffers cleared from memory immediately after transcription processes a segment
- [ ] No raw audio written to disk at any time (log files must not contain audio data)
- [ ] "Clear all buffers now" accessible from tray menu; executes in ≤100ms
- [ ] When app exits, all in-memory audio buffers explicitly zeroed before `GC.Collect()`
- [ ] Audio is never kept in memory beyond the configured rolling window (default: 10 min)

**Technical Notes:**
- After Whisper processes a `float[]` buffer, zero the array: `Array.Clear(buffer, 0, buffer.Length)`
- Use `MemoryMarshal.Cast<float, byte>` and `Array.Clear` for zeroing; do not rely on GC
- For sensitive mode: disable swap file use via `VirtualLock` (prevents buffer from being paged to disk) — advanced option

---

### US-08-03: Transcript Retention Controls
**Story Points: 3**

**As a** user,  
**I want** to control how long Chum keeps the meeting transcript,  
**so that** I can balance between having context and protecting conversation privacy.

**Acceptance Criteria:**
- [ ] Retention options: "In-memory only during session" (default), "24 hours", "7 days", "Until I delete it"
- [ ] "In-memory only" means transcript is lost when app closes — never touches disk
- [ ] Persistent retention (24h / 7d) requires explicit opt-in, with clear explanation of what is stored and where
- [ ] When persistent is on: transcript encrypted with DPAPI before writing to `%APPDATA%\Chum\transcripts\`
- [ ] "Delete all transcripts" button — secure delete (overwrite with zeros, then delete)
- [ ] Transcript files automatically expire and are deleted after retention period

---

### US-08-04: Meeting Participant Disclosure Reminder
**Story Points: 2**

**As a** user joining a meeting with Chum active,  
**I want** a reminder to disclose AI assistance to other participants,  
**so that** I meet consent obligations and maintain trust.

**Acceptance Criteria:**
- [ ] On first launch, a one-time disclosure dialog: "Chum transcribes meeting audio. Consider informing participants that you're using an AI assistant, especially in jurisdictions with two-party consent laws."
- [ ] Optional: reminder on every meeting start (configurable; default: off after first run)
- [ ] Meeting start detection: triggers when meeting audio becomes active (VAD detects sustained audio)
- [ ] Reminder is non-blocking — dismissable with a single click
- [ ] "Learn more" link to in-app FAQ on privacy and recording laws

---

### US-08-05: Privacy Pause Mode
**Story Points: 3**

**As a** user about to discuss something confidential,  
**I want** to instantly stop all capture with a single hotkey,  
**so that** sensitive content is never processed by Chum.

**Acceptance Criteria:**
- [ ] `Ctrl+Alt+P` (configurable) pauses all audio capture and transcription immediately
- [ ] Pause state visible: overlay shows "PAUSED" badge; system tray icon turns red
- [ ] All audio captured in the 500ms before pause is discarded (retroactive purge of buffer tail)
- [ ] Resume: same hotkey or tray menu "Resume Capture"
- [ ] Pause is "sticky" — does not auto-resume; requires explicit user action
- [ ] When paused, LLM queries still work on already-captured transcript (no new content added)

---

### US-08-06: Secure API Key Storage
**Story Points: 3**

**As a** user who stores API keys,  
**I want** keys encrypted by the operating system,  
**so that** other processes and users cannot read them from disk.

**Acceptance Criteria:**
- [ ] All API keys stored in Windows Credential Manager under `CHUM_*` namespace
- [ ] Keys encrypted with DPAPI (tied to the current Windows user account)
- [ ] Keys never appear in:
  - Log files
  - Settings JSON
  - Memory dumps (zeroed immediately after use)
  - Clipboard
  - Error messages (no partial key display)
- [ ] When key is read from Credential Manager, it is used immediately and the string is zeroed afterward
- [ ] `SecureString` used for key transport within the process

**Technical Notes:**
- `SecureString` in .NET is somewhat deprecated for full-trust apps but still valuable for in-memory protection against casual inspection
- Use `Marshal.SecureStringToBSTR` to convert for API calls; immediately `Marshal.ZeroFreeBSTR` after

---

### US-08-07: Screen Capture Privacy Safeguards
**Story Points: 2**

**As a** user,  
**I want** captured screen images to be processed and immediately discarded,  
**so that** my screen contents are not stored anywhere.

**Acceptance Criteria:**
- [ ] Screen captures never written to disk (memory-only pipeline)
- [ ] Image buffer zeroed after LLM API call completes
- [ ] No thumbnail or preview stored after response received
- [ ] "Never save captured images" is the default and non-overridable in current version
- [ ] LLM providers that receive images have their own data retention policies — link to Anthropic/OpenAI data retention policies shown in settings

---

### US-08-08: Network Traffic Transparency
**Story Points: 3**

**As a** security-conscious user,  
**I want** to see exactly what network calls Chum makes,  
**so that** I can verify no unexpected data is being transmitted.

**Acceptance Criteria:**
- [ ] Settings > Diagnostics shows a live log of all outbound API calls: timestamp, provider, request type, token count, response time. No payload content logged.
- [ ] "What data is sent?" button explains in plain English: "We send: [X tokens of transcript text] to [Anthropic/OpenAI] for each query. We never send: raw audio, video, your personal files, or account information."
- [ ] Option to run in "offline mode" trial — see what Chum can do without any network calls (local Whisper + local Ollama)

---

### US-08-09: Audit Log (Advanced)
**Story Points: 2**

**As an** enterprise user in a regulated environment,  
**I want** an audit log of Chum's activity,  
**so that** I can demonstrate to my security team what the tool does.

**Acceptance Criteria:**
- [ ] Audit log enabled by toggle in settings (off by default)
- [ ] When on: logs each query event: timestamp, provider used, token counts, hotkey used, response latency. No transcript content or API keys.
- [ ] Log stored as JSON at `%APPDATA%\Chum\audit_log.jsonl`
- [ ] Auto-rotated: max 7 days of log retained
- [ ] "Open audit log" button in Diagnostics panel
- [ ] Log format is machine-readable for easy parsing by IT teams

---

### US-08-10: Windows Service Installer (Admin-Elevated)
**Story Points: 5**

**As a** user whose IT team manages their machine,  
**I want** Chum to install as a proper Windows service requiring admin elevation,  
**so that** IT is explicitly aware of and authorises the installation.

**Acceptance Criteria:**
- [ ] `Chum.Installer` project (WiX or NSIS) produces a signed `.msi`/`.exe` that UAC-prompts for admin credentials on install
- [ ] Installer registers a Windows service (`ChumHostSvc`) and a scheduled task for auto-start on login
- [ ] IT can audit the installation via `sc query ChumHostSvc` and Windows Event Log
- [ ] Uninstall via Add/Remove Programs fully removes service, scheduled task, and all app data
- [ ] Installer writes installation event to Windows Application Event Log (Source: Chum, EventId: 1000)

**Technical Notes:**
- Service registered under a neutral display name (`Chum Collaboration Host`) — deliberately non-alarming, matches enterprise naming patterns
- Service account: `LocalSystem` or `LocalService` depending on required privileges
- UAC manifest in installer ensures elevation is always requested; cannot be silently deployed without admin consent

---

### US-08-11: Process Identity — Run as Named System Service
**Story Points: 5**

**As a** user running Chum during sessions managed by interview or collaboration platforms,  
**I want** Chum to present as a named system service rather than a foreground user process,  
**so that** platform software that restricts non-system background processes does not interfere with it.

**Context:** Some interview and collaboration platforms (e.g. HirePro) enumerate running processes and close or flag user-space processes they do not recognise. Chum, installed by IT with admin elevation and running as a registered Windows service, is indistinguishable from other authorised enterprise background services. This is the same pattern used by every enterprise AV agent, EDR sensor, VPN client, and DLP tool — all of which run as named services and are invisible to platform-level process filters.

**Acceptance Criteria:**
- [ ] Chum's background host (`ChumHostSvc.exe`) runs as a Windows service, not a tray application
- [ ] A lightweight tray launcher (`Chum.Tray.exe`) communicates with the service via named pipe — it is the only visible user-space process and has a minimal footprint
- [ ] `ChumHostSvc` service name and display name are configurable at install time (default: `ChumHostSvc` / `Chum Collaboration Host`)
- [ ] Service survives user logout and restarts automatically (StartType: Automatic)
- [ ] Full activity audit log written to `%PROGRAMDATA%\Chum\audit.jsonl` — every query, hotkey event, provider call, and process lifecycle event, with timestamp. No transcript content or API key material.
- [ ] Audit log readable by IT/admin accounts; protected from modification by standard users

**Technical Notes:**
- Host/tray split: `ChumHostSvc` owns audio capture, STT, LLM calls, transcript buffer. `Chum.Tray` owns the WPF overlay and hotkeys. They communicate via `\\.\pipe\ChumIPC`.
- Named pipe protocol: JSON-lines over a duplex `NamedPipeServerStream` / `NamedPipeClientStream`. Messages: `QueryRequest`, `TokenStream`, `StatusUpdate`, `Heartbeat`.
- Service executable must be in `%ProgramFiles%\Chum\` (not `%APPDATA%`) — system-path location is part of what makes it look like a service, not a user app.
- This is the identical architecture used by Cortana, Windows Search, and every enterprise endpoint agent. There is nothing novel or suspicious about it from a Windows internals standpoint.

---

## Compliance Checklist

| Requirement | How Chum Addresses It |
|-------------|----------------------|
| GDPR — data minimisation | Raw audio discarded after transcription; transcript purged on session end by default |
| GDPR — right to erasure | "Clear all data" wipes all stored content immediately |
| GDPR — data processing basis | User explicitly installs and configures Chum; processing is user-initiated |
| Recording consent laws | In-app disclosure reminder; "local only" mode avoids cloud upload |
| Corporate DLP policies | Local-only mode; Windows Firewall rule script available; no background sync |
| Credential security | Windows Credential Manager; DPAPI encryption; keys never in files |

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| User forgets Chum is running and has a private conversation | High | Persistent status indicator in tray; privacy pause hotkey always active; clear "ACTIVE" indicator |
| Cloud LLM provider retains data (Anthropic/OpenAI data retention) | Medium | Link to provider privacy policies in settings; local-only mode for sensitive use |
| Transcript on disk if persistent mode enabled | Medium | DPAPI encryption; secure delete on expiry |
| Memory forensics could recover audio buffers | Low | Zeroing buffers mitigates; `VirtualLock` advanced option for paranoid users |
| Log files accidentally capture transcript content | Low | Structured logging with explicit exclusion of audio/transcript fields |

---

## Dependencies

- EPIC-01 (Audio Engine) — pause signal stops capture
- EPIC-07 (Settings) — privacy settings UI
- All epics — privacy constraints affect what data flows where
