# Chum — Real-Time AI Meeting Co-Pilot

Chum listens to your meeting audio, maintains a rolling transcript via local
Whisper STT, and surfaces LLM-powered answers when you hold a configurable
hotkey. A second hotkey captures your screen and sends it to a multimodal LLM.
The overlay is a transparent always-on-top window that floats above your
meeting app.

**Supported platforms:** Windows 10 (1903+) / Windows 11 (x64)

---

## Architecture

Chum runs as two processes:

| Process | Role |
|---------|------|
| `ChumHostSvc.exe` | Windows Service (auto-start, LocalSystem) — audio capture, VAD, Whisper transcription, LLM calls |
| `Chum.App.exe` | WPF tray application — overlay UI, hotkeys, settings window |

The tray app connects to the host service via named pipe. The service starts
automatically on boot; the tray app launches on user logon via a scheduled task.

Installing as a Windows Service requires administrator access. Your IT
administrator must approve and perform (or supervise) the installation.

---

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| Windows | 10 (1903+) or 11 | x64 only |
| .NET Runtime | 10.0 | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) — WindowsDesktop runtime |
| .NET SDK | 10.0 | Required only if running from source or using the script installer |
| API key | Anthropic or OpenAI | Stored in Windows Credential Manager — never in files |

To check your .NET runtime:

```powershell
dotnet --list-runtimes
# Should include: Microsoft.WindowsDesktop.App 10.0.x
```

---

## Install — One Step

**Right-click `install.cmd` and choose "Run as administrator."** That is the
only step. (Double-clicking also works — it requests admin rights for you.)

The installer figures out everything else on its own:

- It **detects any previous installation** and offers to update, remove, or cancel.
- It **finds the binaries automatically**, in this order:
  1. **Pre-built binaries beside it** (`App\` and `Service\`) → installs directly. No .NET SDK needed.
  2. **Source tree beside it** (`src\`) on a machine with the **.NET SDK** → builds
     self-contained binaries from your current code, installs them, and also writes
     a redistributable `chum-deploy\` folder.
  3. **Neither present** → an on-screen menu lets you **download the latest release
     from GitHub**, enter a path to a `src\` folder to build from, or point at a
     folder that already has `App\` and `Service\`.

It then copies files to `%ProgramFiles%\Chum\`, registers `ChumHostSvc` as an
auto-start Windows service, creates a logon scheduled task for the tray app, and
starts the service. After it finishes, look for the Chum icon in your system tray
and right-click → Settings to enter your API key.

> Binaries always install to `%ProgramFiles%\Chum\`, independent of where you run
> `install.cmd` from. The source/repo is only used during a build.

**To uninstall:** run `install.cmd` again and choose **[R] Remove**, or:

```powershell
.\scripts\Uninstall-Chum.ps1              # keep audit logs
.\scripts\Uninstall-Chum.ps1 -RemoveData  # also remove %ProgramData%\Chum
```

---

## Distributing to other PCs (no source or SDK on the target)

End users never need the source code or the .NET SDK. Two supported ways:

**A. GitHub Release (recommended — always current):**
A GitHub Actions workflow ([.github/workflows/release.yml](.github/workflows/release.yml))
rebuilds the self-contained package from source and publishes it on every version
tag, so the release `install.cmd` downloads is always built from the latest committed
code — users can't get stale binaries.

```powershell
# Cut a release (CI builds + publishes automatically):
git tag v0.1.1 ; git push --tags
```

Then a user just downloads `install.cmd` (or the release ZIP) and runs it as admin;
with no binaries present locally it pulls the latest release automatically.

**B. Hand-built package (offline / no CI):**

```powershell
# On a machine with source + SDK:
.\scripts\Publish-Release.ps1            # writes dist\chum-<version>.zip
# (add -Publish to also upload it to a GitHub Release via the gh CLI)
```

Copy the ZIP (or the `chum-deploy\` folder produced by a source install) to the
target PC, extract, and run `install.cmd` as admin.

---

## Running from Source (Development)

No admin required for development runs. The tray app connects directly to its
embedded service pipeline instead of the Windows Service.

```powershell
# Build everything
dotnet build src/Chum.sln

# Run the tray app (includes the full pipeline in-process)
dotnet run --project src/Chum.App

# Run tests
dotnet test src/Chum.Tests
```

---

## First-Run Setup

1. Launch Chum (tray icon appears in the notification area)
2. Right-click the tray icon → **Settings**
3. Enter your Anthropic or OpenAI API key (stored in Windows Credential Manager)
4. Optionally: configure hotkeys, select audio devices, choose Whisper model
5. Press and hold the **Hold-to-Ask** hotkey during a meeting to query the LLM

Default hotkeys:

| Action | Default |
|--------|---------|
| Hold to Ask (LLM query) | `Ctrl+Space` (hold) |
| Capture Screen | `Ctrl+Shift+S` |
| Show / Hide Overlay | `Ctrl+Shift+H` |

---

## Directory Layout

```
%ProgramFiles%\Chum\
    Service\        ChumHostSvc.exe and dependencies
    App\            Chum.App.exe and dependencies

%ProgramData%\Chum\
                    Audit log, runtime state (ACL: SYSTEM+Admins write, Users read)

%LOCALAPPDATA%\Chum\
    Logs\           Rolling application log (Serilog)
    CrashReports\   Opt-in local crash dumps (never uploaded automatically)

%APPDATA%\Chum\
    settings.json   User settings
```

---

## Audit Trail

Every action taken by Chum is logged:

- **Windows Event Log** (`Application` source: `Chum`) — install, uninstall, service start/stop
- **Serilog rolling file** (`%LOCALAPPDATA%\Chum\Logs\`) — audio capture events, transcription segments, LLM requests/responses (without content), hotkey presses
- **API keys** — stored in Windows Credential Manager; never appear in any log file

To view the audit log:

```powershell
# Event Log entries
Get-EventLog -Log Application -Source Chum

# Recent application log (last 50 lines)
Get-Content "$env:LOCALAPPDATA\Chum\Logs\chum*.log" | Select-Object -Last 50
```

---

## Troubleshooting

| Symptom | Check |
|---------|-------|
| No audio captured | Settings > Audio — verify loopback device; try setting default output |
| Transcription never appears | Settings > Transcription — check Whisper model path; see Logs for errors |
| Hotkey not firing | Ensure Chum tray app is running; check for hotkey conflict in Settings |
| Overlay not visible | Right-click tray icon → Show Overlay; check monitor assignment |
| Service won't start | `Get-EventLog -Log Application -Source Chum -Newest 10` for the error |

---

## Building the MSI Installer

For distribution, a WiX MSI installer project is included:

```powershell
# Install WiX toolset (once)
dotnet tool install --global wix

# Build the MSI
dotnet build src/Chum.Installer/Chum.Installer.wixproj -c Release
# Output: src/Chum.Installer/bin/Release/Chum-Setup.msi
```

---

## Privacy

- Audio is processed locally by Whisper — nothing is sent to external servers
  unless you press a hotkey to query the LLM
- LLM queries send the last N transcript segments to your configured provider
  (Anthropic or OpenAI)
- Screen captures are memory-only and are never written to disk
- Audio buffers are zeroed out after transcription
- Crash reporting is **opt-in** (off by default); local files only

---

## GitHub

`https://github.com/kushal-DL/chum`
