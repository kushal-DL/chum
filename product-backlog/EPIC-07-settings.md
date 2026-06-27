# EPIC-07: Settings & Configuration

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-07-01 | API Key Management | P0 — MVP | 🔵 Built | 5 |
| US-07-02 | Audio Device Configuration | P1 — High | 🔵 Built | 3 |
| US-07-03 | LLM Provider & Model Selection | P1 — High | 🔵 Built | 3 |
| US-07-04 | Transcription Configuration | P1 — High | 🔵 Built | 3 |
| US-07-05 | Hotkey Configuration | P1 — High | 🔵 Built | 3 |
| US-07-06 | Overlay Appearance Settings | P2 — Medium | 🔵 Built | 2 |
| US-07-07 | Startup & Run Behavior | P2 — Medium | 🔵 Built | 2 |
| US-07-08 | Data Retention & Privacy Settings | P1 — High | 🔵 Built | 2 |
| US-07-09 | Settings Import & Export | P3 — Low | 🔴 Yet to Start | 2 |
| US-07-10 | About & Diagnostics Panel | P2 — Medium | 🔴 Yet to Start | 2 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

Settings is the control panel for Chum. It covers API key management, audio device selection, LLM model choice, hotkey bindings, overlay appearance, and data retention. Because Chum handles sensitive audio and requires API keys, security and safe defaults are as important as usability.

---

## Design Principles

- **Secure by default**: API keys stored in Windows Credential Manager, never in plain-text settings files
- **Sane defaults**: App should work without any configuration beyond providing an API key
- **Progressive disclosure**: Basic settings prominent; advanced settings behind "Advanced" toggle
- **No restart required**: All settings changes apply live (except LLM model, which takes effect on next query)

---

## Settings Storage Strategy

| Setting Type | Storage Location | Rationale |
|-------------|-----------------|-----------|
| API Keys | Windows Credential Manager (`DPAPI`) | Never on disk in plaintext |
| User Preferences | `%APPDATA%\Chum\settings.json` | Roams with user profile; human-readable |
| Window State | `%APPDATA%\Chum\window_state.json` | Separate from preferences for easy reset |
| Prompt Templates | `%APPDATA%\Chum\templates.json` | User-editable; separate file |
| Downloaded Models | `%LOCALAPPDATA%\Chum\Models\` | Machine-local; large files |
| Logs | `%LOCALAPPDATA%\Chum\Logs\` | Machine-local; not roamed |

---

## Stories

### US-07-01: API Key Management
**Story Points: 5**

**As a** user setting up Chum,  
**I want** to enter my API keys securely,  
**so that** Chum can connect to LLM providers without exposing my keys to other users or processes.

**Acceptance Criteria:**
- [ ] Settings panel has fields for: Anthropic API Key, OpenAI API Key, Azure Speech Key + Region
- [ ] Keys entered in password-masked input fields (shown as dots)
- [ ] "Show/Hide" toggle per key field
- [ ] Keys saved to Windows Credential Manager (`CredentialManager.WriteCredential()`)
- [ ] Keys never written to `settings.json` or any log file
- [ ] "Test Connection" button per provider — makes a minimal API call and shows success/failure
- [ ] Keys deletable via "Remove" button per field
- [ ] Clear warning if key is invalid (not just "error" — say "API key rejected by Anthropic — check it's correct")

**Technical Notes:**
```csharp
// Save
CredentialManager.WriteCredential("Chum_Anthropic_Key", "Chum", apiKey, CredentialPersistence.LocalMachine);

// Read
var cred = CredentialManager.ReadCredential("Chum_Anthropic_Key");
string apiKey = cred?.Password;
```

Use `AdysTech.CredentialManager` NuGet for clean DPAPI wrapper.

**Security Notes:**
- Never log API key values, even partially (no "key starting with sk-...")
- Clear key from memory when settings panel is closed (set field to null)
- Audit credential access: log timestamp of when key was read (not the key itself)

---

### US-07-02: Audio Device Configuration
**Story Points: 3**

**As a** user with multiple audio devices,  
**I want** to configure which devices Chum uses for loopback and microphone,  
**so that** Chum captures the right audio in my specific setup.

**Acceptance Criteria:**
- [ ] Dropdown: "Loopback Source" — lists all active audio output devices
- [ ] Dropdown: "Microphone Source" — lists all active audio input devices
- [ ] Each dropdown shows: device name, current default indicator, device icon if available
- [ ] "Use Windows Default" option at top of each list (auto-follows Windows default)
- [ ] Live audio level preview next to each dropdown (updates in real-time)
- [ ] "Test Loopback" button: plays a test tone through selected output and verifies capture
- [ ] Changes applied immediately without restart

---

### US-07-03: LLM Provider and Model Selection
**Story Points: 3**

**As a** user choosing between speed and quality,  
**I want** to configure which LLM provider and model Chum uses,  
**so that** I can optimise for my needs (cost, speed, accuracy, privacy).

**Acceptance Criteria:**
- [ ] Provider options: Anthropic, OpenAI, Ollama (local)
- [ ] Model list per provider, dynamically fetched from provider's API (with fallback hardcoded list)
- [ ] Visual tags on models: "Fast", "Best Quality", "Cheapest", "Vision capable", "Local"
- [ ] Separate model selection for: Text queries, Vision queries
- [ ] "Max tokens per response" slider: 256 – 4096 (default 1024)
- [ ] "Temperature" slider: 0.0 – 1.0 (default 0.3 for factual meeting Q&A)
- [ ] Estimated cost per query shown based on current model and average query size

---

### US-07-04: Transcription Configuration
**Story Points: 3**

**As a** user tuning Chum for my hardware,  
**I want** to configure the transcription engine,  
**so that** I get the right balance of speed and accuracy.

**Acceptance Criteria:**
- [ ] Whisper model size selector: tiny, base, small, medium, large-v3
  - Each option shows: disk size, speed estimate, accuracy level, GPU RAM requirement
- [ ] "Use GPU" toggle (default: on if GPU detected)
- [ ] Language override dropdown (default: Auto-detect)
- [ ] Filler word removal toggle (um, uh, like — default: off)
- [ ] "Download model" button per model size (with progress bar)
- [ ] Transcript retention window: 5 / 10 / 15 / 30 minutes
- [ ] Cloud STT fallback: enable/disable + provider selection

---

### US-07-05: Hotkey Configuration
**Story Points: 3**

**As a** user whose hotkeys conflict with other apps,  
**I want** to remap all Chum hotkeys,  
**so that** Chum works without interfering with my other tools.

**Acceptance Criteria:**
- [ ] Table showing all hotkeys: Action | Current Binding | Edit
- [ ] Click "Edit" → enter recording mode; press desired combo; "Save" or "Cancel"
- [ ] Combo validation: requires at least one modifier key
- [ ] Duplicate detection: if new combo conflicts with another Chum hotkey, show error
- [ ] Known system conflict warnings (Teams shortcuts, Windows shortcuts)
- [ ] "Reset All to Defaults" button with confirmation
- [ ] Changes apply immediately

**Default Hotkey Assignments:**
| Action | Default Binding |
|--------|----------------|
| Hold to Ask | `Ctrl + Alt + Space` |
| Screen Capture | `Ctrl + Alt + S` |
| Privacy Pause | `Ctrl + Alt + P` |
| Show/Hide Overlay | `Ctrl + Alt + H` |
| Action Items | `Ctrl + Alt + A` |
| Template 1 | `Ctrl + Alt + 1` |
| Template 2 | `Ctrl + Alt + 2` |

---

### US-07-06: Overlay Appearance Settings
**Story Points: 2**

**As a** user personalising their experience,  
**I want** to customise how the Chum overlay looks,  
**so that** it fits my workflow and visual preferences.

**Acceptance Criteria:**
- [ ] Background opacity: 20%–95% slider (default 75%)
- [ ] Color theme: Dark, Light, High Contrast, Custom (custom = pick any hex background + text colour)
- [ ] Font size: 10–18px slider
- [ ] Corner radius: 0–12px slider (default 8px)
- [ ] Snap to screen corners toggle
- [ ] "Show transcript strip" toggle (default: off)
- [ ] "Show audio meters" toggle (default: off)
- [ ] Live preview of overlay as settings change

---

### US-07-07: Startup and Run Behavior
**Story Points: 2**

**As a** user who wants Chum ready when they need it,  
**I want** to configure when Chum starts and how it behaves in the background,  
**so that** it is always available without getting in the way.

**Acceptance Criteria:**
- [ ] "Start with Windows" toggle — adds/removes registry entry at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- [ ] "Start minimised to tray" toggle (default: on)
- [ ] "Start capturing audio on launch" toggle (default: on)
- [ ] "Auto-hide overlay when no meeting app detected" toggle (default: off)
- [ ] Meeting app detection: configure which process names count as "meeting apps" (defaults: `Teams.exe`, `chrome.exe` with Google Meet URL, `zoom.exe`)

---

### US-07-08: Data Retention and Privacy Settings
**Story Points: 2**

**As a** user concerned about privacy,  
**I want** to control what data Chum retains and for how long,  
**so that** sensitive meeting content is not stored beyond my needs.

*(Full details in EPIC-08; these are the UI-facing settings)*

**Acceptance Criteria:**
- [ ] "Transcript retention" dropdown: During session only / 24 hours / 7 days / Never (default: session only)
- [ ] "Screen captures" toggle: Keep in memory only (default) vs. auto-save to folder
- [ ] "Send transcript to cloud" toggle: if off, no cloud LLM or cloud STT is used
- [ ] "Clear all data now" button — wipes transcript, screen captures, response history from memory and disk
- [ ] Link to privacy policy / data handling explanation

---

### US-07-09: Settings Import and Export
**Story Points: 2**

**As a** user setting up Chum on a new machine,  
**I want** to export and import my settings,  
**so that** I don't need to reconfigure everything from scratch.

**Acceptance Criteria:**
- [ ] "Export Settings" button — saves `settings.json` and `templates.json` to user-chosen location
- [ ] API keys excluded from export (with clear warning: "API keys are not exported for security reasons")
- [ ] "Import Settings" button — loads from a previously exported file; validates format before applying
- [ ] Imported settings merged with current settings (not full replace) — allows importing only templates, for example
- [ ] Version field in settings JSON for forward compatibility

---

### US-07-10: About and Diagnostics Panel
**Story Points: 2**

**As a** user troubleshooting an issue,  
**I want** a diagnostics page in settings,  
**so that** I can gather information to report a bug or resolve a problem myself.

**Acceptance Criteria:**
- [ ] About tab: App version, build date, .NET version, OS version
- [ ] System info: Audio devices detected, GPU info, available disk for models
- [ ] "Open log file" button — opens latest log in Notepad
- [ ] "Copy diagnostics to clipboard" button — structured text block of all system/config info (no secrets)
- [ ] "Check for updates" button
- [ ] "Report issue" link → GitHub Issues

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| Windows Credential Manager unavailable in some sandboxed environments | Low | Detect; fallback to `DPAPI`-encrypted file in `%APPDATA%\Chum\` as secondary option |
| Settings JSON schema evolution (adding new fields) | Medium | Use `JsonSerializer` with `IgnoreUnknownProperties`; always write defaults on first launch |
| User accidentally deletes API key | Low | "Remove" action requires confirmation; keys deletable but not recoverable from Chum |
| Multiple user accounts on same machine | Low | `%APPDATA%` is per-user; each user has independent settings |

---

## Dependencies

- All other epics — settings is consumed by everyone
- EPIC-08 (Privacy) — privacy settings in EPIC-07 UI; enforcement in EPIC-08
