# Chum — Repository Structure

> **This is the canonical source of truth for how this repo is organised.**
> Read this before creating any new file. Update it only when the structure genuinely changes
> (new project added, new top-level directory, significant new subsystem). Do NOT update it
> for every new class — it describes directories and layers, not individual files.

---

## Top-Level Layout

```
chum/
├── .claude/
│   ├── commands/          ← Claude Code skill commands (slash commands)
│   ├── hooks/             ← Shell hooks (Stop, PreToolUse, PostToolUse)
│   └── settings.json      ← Project-level Claude Code configuration
├── product-backlog/       ← Product backlog (markdown only, no code)
│   ├── BACKLOG-STATUS.md  ← AUTHORITATIVE status tracker — update after every code change
│   ├── README.md          ← Vision, tech stack, epic index
│   └── EPIC-NN-*.md       ← One file per epic (10 total)
├── src/                   ← All application source code
│   ├── Chum.sln           ← Solution file (only updated when adding/removing projects)
│   ├── Chum.Audio/        ← Layer 1: audio capture + VAD
│   ├── Chum.Transcription/← Layer 2: STT + transcript buffer
│   ├── Chum.Llm/          ← Layer 3: LLM provider abstraction
│   └── Chum.App/          ← Layer 4: WPF app, services, UI
├── .claude/               ← (see above)
├── .gitignore
├── CLAUDE.md              ← Session protocol + code conventions (read every session)
├── REPO_STRUCTURE.md      ← This file
└── session-handoff.md     ← Cross-session state (update every session)
```

---

## Project Dependency Rules

Dependencies flow **strictly downward**. No upward or circular references.

```
Chum.App
  └── references Chum.Audio, Chum.Transcription, Chum.Llm

Chum.Transcription
  └── references Chum.Audio  (for AudioSource enum + AudioChunk)

Chum.Llm
  └── references nothing (pure library)

Chum.Audio
  └── references nothing (pure library)
```

**Rule:** If you need functionality from a higher layer, use an event or callback — never add an upward project reference.

---

## Source Directory Layout (per project)

### `Chum.Audio/`
```
Chum.Audio/
├── Chum.Audio.csproj
├── Capture/           ← IAudioCapture + concrete capture classes (Loopback, Mic)
├── Models/            ← Plain data types: AudioChunk, AudioSource
├── Pipeline/          ← AudioPipeline (orchestrator), AudioConverter (DSP utility)
└── Vad/               ← Voice activity detection: EnergyVad, (future) SileroVad
```

### `Chum.Transcription/`
```
Chum.Transcription/
├── Chum.Transcription.csproj
├── Models/            ← TranscriptSegment
├── WhisperSttEngine.cs← ISttProvider implementation (Whisper.net)
├── TranscriptBuffer.cs← Thread-safe rolling transcript store
└── ContextExtractor.cs← Token-budget-aware context builder for LLM requests
```

### `Chum.Llm/`
```
Chum.Llm/
├── Chum.Llm.csproj
├── ILlmProvider.cs    ← Interface + LlmRequest/LlmResponse records
├── AnthropicLlmProvider.cs ← Claude (HttpClient + SSE)
├── PromptBuilder.cs   ← Meeting-optimised prompt construction
└── (future) OpenAiLlmProvider.cs, OllamaLlmProvider.cs
```

### `Chum.App/`
```
Chum.App/
├── Chum.App.csproj
├── App.xaml + App.xaml.cs     ← Entry point, DI, tray icon
├── Assets/                    ← Icons, images (binary assets only)
├── Models/                    ← AppSettings (settings schema)
├── Services/                  ← Background services and orchestration
│   ├── SettingsService.cs     ← Load/save settings.json
│   ├── CredentialService.cs   ← Windows Credential Manager wrappers
│   ├── HotkeyService.cs       ← Win32 WH_KEYBOARD_LL hook
│   ├── ModelDownloadService.cs← Whisper/Silero model download helpers
│   ├── MeetingOrchestrator.cs ← Main wiring: audio → STT → LLM → overlay
│   └── ScreenShareDetector.cs ← Win32 window polling for share detection
├── ViewModels/                ← INPC view models (UI state only, no business logic)
│   └── OverlayViewModel.cs
└── Views/                     ← XAML windows and controls
    ├── OverlayWindow.xaml + .cs
    └── SettingsWindow.xaml + .cs
```

---

## Where New Files Go

| Type of file | Goes in |
|---|---|
| New audio capture source (e.g. virtual cable) | `Chum.Audio/Capture/` |
| New VAD algorithm | `Chum.Audio/Vad/` |
| New audio DSP utility | `Chum.Audio/Pipeline/` |
| New data record/enum shared in audio layer | `Chum.Audio/Models/` |
| New STT provider | `Chum.Transcription/` (implement `ISttProvider`) |
| New transcript post-processing | `Chum.Transcription/` |
| New LLM provider (OpenAI, Ollama, etc.) | `Chum.Llm/` (implement `ILlmProvider`) |
| New WPF window | `Chum.App/Views/` |
| New view model | `Chum.App/ViewModels/` |
| New background service / OS integration | `Chum.App/Services/` |
| New settings property | `Chum.App/Models/AppSettings.cs` (add property there) |
| Binary assets (icons, sounds) | `Chum.App/Assets/` |

**Never create:** top-level source files outside these directories. Every new `.cs` belongs in one of the above folders.

---

## Naming Conventions

| Concept | Convention | Example |
|---|---|---|
| Interfaces | `IXxx` | `ILlmProvider`, `IAudioCapture` |
| Data records | Noun, no suffix | `AudioChunk`, `TranscriptSegment` |
| Services (stateful, long-lived) | `XxxService` | `SettingsService`, `HotkeyService` |
| Orchestrators | `XxxOrchestrator` | `MeetingOrchestrator` |
| Engine/processor | `XxxEngine` | `WhisperSttEngine` |
| Async methods | Suffix `Async` | `TranscribeAsync`, `StreamResponseAsync` |
| XAML windows | `XxxWindow.xaml` | `OverlayWindow.xaml` |
| View models | `XxxViewModel` | `OverlayViewModel` |
| Event args | `XxxEventArgs` | `HotkeyQueryEventArgs` |

---

## Interface Boundaries (Anti-Corruption Rules)

- **All external API calls** go through `ILlmProvider` or `ISttProvider` — never call `HttpClient` directly from `Chum.App`.
- **All audio capture** goes through `IAudioCapture` — never new up `WasapiLoopbackCapture` outside `Chum.Audio`.
- **All credential access** goes through `CredentialService` — never call `AdysTech.CredentialManager` directly outside that class.
- **All settings access** goes through `SettingsService.Current` — never read `settings.json` directly.
- **UI thread rule:** `OverlayViewModel` marshals all mutations via `Dispatcher.InvokeAsync`. Views never touch data directly.

---

## Threading Model (Summary)

| Thread | Owns |
|---|---|
| WPF UI thread | All WPF/XAML, Win32 keyboard hook |
| Audio capture threads (×2) | `LoopbackCapture`, `MicCapture` — fire events |
| `AudioPipeline` | Consumes capture events, writes to `Channel<AudioChunk>` |
| `ThreadPool` (BelowNormal) | `WhisperSttEngine.TranscribeAsync` |
| `Task` async | All LLM calls — never `.Wait()` or `.Result` |

---

## When to Update This File

**Do update when:**
- A new project is added to the solution
- A new top-level directory is created under `src/`
- A new major subsystem is introduced that doesn't fit existing buckets
- A naming convention changes project-wide

**Do NOT update when:**
- Adding a normal new class inside an existing directory
- Renaming a single file
- Adding a NuGet package

---

*Last updated: 2026-06-27 — initial structure document*
