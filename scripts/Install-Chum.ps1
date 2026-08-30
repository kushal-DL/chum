#Requires -RunAsAdministrator
<#
.SYNOPSIS
    One-step Chum installer. Right-click install.cmd -> Run as administrator.

.DESCRIPTION
    A single interactive installer that takes care of the entire installation on
    any Windows 10/11 (x64) PC. The user performs ONE action (run install.cmd as
    admin); this script decides how to obtain the binaries and installs them.

    It automatically finds the binaries in this priority order:
      1. Pre-built binaries beside the installer (App\ and Service\)   -> install directly (no SDK)
      2. Source tree beside the installer (src\) + .NET SDK            -> build, install, and emit a deploy package
      3. Otherwise (interactive)                                       -> a menu lets you:
           - download the latest release from GitHub (recommended), OR
           - enter the path to a 'src' folder to build from, OR
           - point at a folder that already contains App\ and Service\

    Before installing it detects any prior Chum installation and offers to
    update (replace), remove, or cancel.

.PARAMETER InstallDir       Root install dir.  Default: %ProgramFiles%\Chum
.PARAMETER DataDir          Runtime data dir.  Default: %PROGRAMDATA%\Chum
.PARAMETER StartService     Start ChumHostSvc after registration.
.PARAMETER Source           auto | prebuilt | build | download   (default auto)
.PARAMETER SrcPath          Path to the 'src' folder (or repo root) when Source=build.
.PARAMETER BinPath          Folder containing App\ and Service\ when Source=prebuilt.
.PARAMETER RepoSlug         GitHub owner/repo used for release download. Default kushal-DL/chum
.PARAMETER NonInteractive   Never prompt; take the default action at every decision.
.PARAMETER DeployPackageDir Where to write the distributable package after a source build.

.EXAMPLE
    # Normal use (via install.cmd, elevated). Figures everything out.
    .\Install-Chum.ps1 -StartService

.EXAMPLE
    # Force downloading the published release (used by the bootstrap in install.cmd)
    .\Install-Chum.ps1 -StartService -Source download
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDir       = "$env:ProgramFiles\Chum",
    [string]$DataDir          = "$env:ProgramData\Chum",
    [switch]$StartService,
    [ValidateSet('auto','prebuilt','build','download')]
    [string]$Source           = 'auto',
    [string]$SrcPath          = '',
    [string]$BinPath          = '',
    [string]$RepoSlug         = 'kushal-DL/chum',
    [switch]$NonInteractive,
    [string]$DeployPackageDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Paths & constants ------------------------------------------------------
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $ScriptDir) { throw "Cannot determine script directory. Run as: powershell -File scripts\Install-Chum.ps1" }
$PackageRoot = Split-Path $ScriptDir -Parent   # parent of scripts\ - repo root OR deploy package root

$ServiceInstallDir = Join-Path $InstallDir 'Service'
$AppInstallDir     = Join-Path $InstallDir 'App'
$ServiceExe        = Join-Path $ServiceInstallDir 'ChumHostSvc.exe'
$ServiceName       = 'ChumHostSvc'
$ServiceDisplay    = 'Chum Collaboration Host'
$ServiceDesc       = 'Chum AI meeting co-pilot: audio capture, transcription, and LLM integration.'
$TaskName          = 'Chum Tray Application'
$EventSource       = 'Chum'
$EventRegPath      = "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$EventSource"

# Set by whichever source mode runs. Kept at script scope so helpers can assign.
$script:SvcPublishDir = $null
$script:AppPublishDir = $null

# --- Output helpers ---------------------------------------------------------
function Write-Step([string]$m) { Write-Host "  $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  [OK] $m" -ForegroundColor Green }
function Write-Err([string]$m)  { Write-Host "  ERROR: $m" -ForegroundColor Red }
function Write-Skip([string]$m) { Write-Host "  [--] $m" -ForegroundColor DarkGray }

function Read-Choice {
    param([string]$Prompt, [string[]]$Valid, [string]$Default)
    if ($NonInteractive) { return $Default }
    while ($true) {
        $ans = Read-Host $Prompt
        if ([string]::IsNullOrWhiteSpace($ans)) { return $Default }
        $ans = $ans.Trim().ToUpperInvariant()
        if ($Valid -contains $ans) { return $ans }
        Write-Host "  Please enter one of: $($Valid -join ', ')" -ForegroundColor Yellow
    }
}

Write-Host "`nChum Installer" -ForegroundColor White
Write-Host "=========================================" -ForegroundColor DarkGray

# ===========================================================================
# Source resolution helpers
# ===========================================================================
function Test-Bins([string]$appDir, [string]$svcDir) {
    return (Test-Path (Join-Path $appDir 'Chum.App.exe')) -and `
           (Test-Path (Join-Path $svcDir 'ChumHostSvc.exe'))
}

function Resolve-SrcRoot([string]$path) {
    # Accept either the 'src' folder itself or a repo root that contains 'src'.
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    $path = $path.Trim().Trim('"')
    if (-not (Test-Path $path)) { return $null }
    if (Test-Path (Join-Path $path 'Chum.Service\Chum.Service.csproj')) { return (Resolve-Path $path).Path }
    $inner = Join-Path $path 'src'
    if (Test-Path (Join-Path $inner 'Chum.Service\Chum.Service.csproj')) { return (Resolve-Path $inner).Path }
    return $null
}

function Invoke-Build([string]$srcRoot) {
    Write-Host "  [Source] Build from source: $srcRoot" -ForegroundColor DarkYellow

    Write-Step "Checking .NET SDK..."
    $sdkOk = $false
    try {
        $sdkVer = (& dotnet --version 2>&1)
        $sdkOk  = ($LASTEXITCODE -eq 0)
        if ($sdkOk) { Write-Ok ".NET SDK $sdkVer" }
    } catch { $sdkOk = $false }
    if (-not $sdkOk) {
        throw ".NET SDK not found. Install from https://dotnet.microsoft.com/download, or choose the GitHub-download option instead."
    }

    $serviceProject = Join-Path $srcRoot 'Chum.Service\Chum.Service.csproj'
    $appProject     = Join-Path $srcRoot 'Chum.App\Chum.App.csproj'
    if (-not (Test-Path $serviceProject)) { throw "Project not found: $serviceProject" }
    if (-not (Test-Path $appProject))     { throw "Project not found: $appProject" }

    $svcOut = Join-Path $srcRoot 'Chum.Service\publish'
    $appOut = Join-Path $srcRoot 'Chum.App\publish'

    Write-Step "Publishing Chum.Service (self-contained, win-x64)..."
    & dotnet publish $serviceProject -r win-x64 -c Release --self-contained true -o $svcOut --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Chum.Service (exit $LASTEXITCODE)" }
    Write-Ok "Chum.Service published"

    Write-Step "Publishing Chum.App (self-contained, win-x64)..."
    & dotnet publish $appProject -r win-x64 -c Release --self-contained true -o $appOut --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Chum.App (exit $LASTEXITCODE)" }
    Write-Ok "Chum.App published"

    $script:SvcPublishDir = $svcOut
    $script:AppPublishDir = $appOut

    # Emit a ready-to-distribute deploy package (best-effort; never fails install).
    try {
        $pkgDir = $DeployPackageDir
        if ($pkgDir -eq '') { $pkgDir = Join-Path (Split-Path $srcRoot -Parent) 'chum-deploy' }
        if ($pkgDir) {
            Write-Step "Writing deploy package -> $pkgDir"
            $pkgSvc     = Join-Path $pkgDir 'Service'
            $pkgApp     = Join-Path $pkgDir 'App'
            $pkgScripts = Join-Path $pkgDir 'scripts'
            New-Item -ItemType Directory -Force -Path $pkgSvc, $pkgApp, $pkgScripts | Out-Null
            Copy-Item "$svcOut\*" $pkgSvc -Recurse -Force
            Copy-Item "$appOut\*" $pkgApp -Recurse -Force
            Copy-Item (Join-Path $ScriptDir 'Install-Chum.ps1')   $pkgScripts -Force
            $uninst = Join-Path $ScriptDir 'Uninstall-Chum.ps1'
            if (Test-Path $uninst) { Copy-Item $uninst $pkgScripts -Force }
            $cmd = Join-Path (Split-Path $srcRoot -Parent) 'install.cmd'
            if (Test-Path $cmd) { Copy-Item $cmd $pkgDir -Force }
            Write-Ok "Deploy package ready - copy '$pkgDir' to any PC and run install.cmd"
        }
    } catch {
        Write-Warning "Could not write deploy package (non-fatal): $($_.Exception.Message)"
    }
}

function Invoke-Download {
    Write-Host "  [Source] Download latest release from GitHub ($RepoSlug)" -ForegroundColor DarkYellow
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $hdr = @{ 'User-Agent' = 'ChumInstaller'; 'Accept' = 'application/vnd.github+json' }

    Write-Step "Querying latest release..."
    $api = "https://api.github.com/repos/$RepoSlug/releases/latest"
    try {
        $rel = Invoke-RestMethod -Uri $api -Headers $hdr -UseBasicParsing
    } catch {
        throw "Could not reach GitHub releases at $api. $($_.Exception.Message)`n" +
              "  (No published release yet? Build one on a dev machine: scripts\Publish-Release.ps1 -Publish)"
    }

    $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $asset) { throw "Latest release '$($rel.tag_name)' has no .zip asset to download." }

    $zipPath = Join-Path $env:TEMP $asset.name
    $sizeMB  = [math]::Round($asset.size / 1MB, 1)
    Write-Step "Downloading $($asset.name) ($sizeMB MB) [$($rel.tag_name)]..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -Headers $hdr -UseBasicParsing
    Write-Ok "Downloaded"

    $extractRoot = Join-Path $env:TEMP ("chum-dl-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Write-Step "Extracting..."
    Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force

    $appDir = Get-ChildItem -Path $extractRoot -Recurse -Directory -Filter 'App' |
        Where-Object { Test-Path (Join-Path $_.FullName 'Chum.App.exe') } | Select-Object -First 1
    $svcDir = Get-ChildItem -Path $extractRoot -Recurse -Directory -Filter 'Service' |
        Where-Object { Test-Path (Join-Path $_.FullName 'ChumHostSvc.exe') } | Select-Object -First 1
    if (-not $appDir -or -not $svcDir) {
        throw "Downloaded package did not contain App\Chum.App.exe and Service\ChumHostSvc.exe."
    }
    $script:AppPublishDir = $appDir.FullName
    $script:SvcPublishDir = $svcDir.FullName
    Write-Ok "Release ready"
}

function Resolve-Source {
    $prebuiltSvc = if ($BinPath) { Join-Path $BinPath 'Service' } else { Join-Path $PackageRoot 'Service' }
    $prebuiltApp = if ($BinPath) { Join-Path $BinPath 'App' }     else { Join-Path $PackageRoot 'App' }

    switch ($Source) {
        'download' { Invoke-Download; return }
        'prebuilt' {
            if (Test-Bins $prebuiltApp $prebuiltSvc) {
                Write-Host "  [Source] Pre-built binaries" -ForegroundColor DarkYellow
                $script:AppPublishDir = $prebuiltApp; $script:SvcPublishDir = $prebuiltSvc; return
            }
            throw "No App\Chum.App.exe + Service\ChumHostSvc.exe found at $(Split-Path $prebuiltApp -Parent)."
        }
        'build' {
            $buildFrom = if ($SrcPath) { $SrcPath } else { Join-Path $PackageRoot 'src' }
            $sr = Resolve-SrcRoot $buildFrom
            if (-not $sr) { throw "No Chum source tree found. Pass -SrcPath <path to 'src'>." }
            Invoke-Build $sr; return
        }
        default {
            # auto: prefer pre-built, then source, else ask.
            if (Test-Bins $prebuiltApp $prebuiltSvc) {
                Write-Host "  [Source] Pre-built binaries (no build needed)" -ForegroundColor DarkYellow
                $script:AppPublishDir = $prebuiltApp; $script:SvcPublishDir = $prebuiltSvc; return
            }
            $autoSrc = Resolve-SrcRoot (Join-Path $PackageRoot 'src')
            if ($autoSrc) { Invoke-Build $autoSrc; return }

            # Nothing beside us - decide how to get the binaries.
            if ($NonInteractive) { Invoke-Download; return }

            Write-Host ""
            Write-Host "  No binaries or source were found next to this installer." -ForegroundColor Yellow
            Write-Host "  How should Chum be installed?" -ForegroundColor White
            Write-Host "    [1] Download the latest release from GitHub   (recommended)" -ForegroundColor Gray
            Write-Host "    [2] Build from source  - I'll enter the path to the 'src' folder" -ForegroundColor Gray
            Write-Host "    [3] Use a folder that already has App\ and Service\" -ForegroundColor Gray
            Write-Host "    [C] Cancel" -ForegroundColor Gray
            $c = Read-Choice "  Choice [1]" @('1','2','3','C') '1'
            switch ($c) {
                '1' { Invoke-Download }
                '2' {
                    $p  = Read-Host "  Full path to the Chum 'src' folder (or the repo root)"
                    $sr = Resolve-SrcRoot $p
                    if (-not $sr) { throw "That folder does not contain Chum source (Chum.Service\Chum.Service.csproj not found under it)." }
                    Invoke-Build $sr
                }
                '3' {
                    $p = (Read-Host "  Full path to the folder containing App\ and Service\").Trim().Trim('"')
                    $a = Join-Path $p 'App'; $s = Join-Path $p 'Service'
                    if (-not (Test-Bins $a $s)) { throw "That folder does not contain App\Chum.App.exe and Service\ChumHostSvc.exe." }
                    Write-Host "  [Source] Pre-built binaries at $p" -ForegroundColor DarkYellow
                    $script:AppPublishDir = $a; $script:SvcPublishDir = $s
                }
                'C' { Write-Host "  Cancelled by user." -ForegroundColor Yellow; exit 0 }
            }
        }
    }
}

# ===========================================================================
# Prior-installation handling
# ===========================================================================
function Remove-ChumInstall {
    Write-Step "Removing existing Chum installation..."
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -eq 'Running') { Stop-Service -Name $ServiceName -Force; Start-Sleep -Seconds 2 }
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 1
    }
    $procs = Get-Process -Name 'Chum.App', 'ChumHostSvc' -ErrorAction SilentlyContinue
    if ($procs) { $procs | Stop-Process -Force; Start-Sleep -Seconds 2 }
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }
    if (Test-Path $EventRegPath) { Remove-Item -Path $EventRegPath -Recurse -Force }
    if (Test-Path $InstallDir)   { Remove-Item -Path $InstallDir -Recurse -Force }
    Write-Ok "Existing installation removed (data at $DataDir preserved)"
}

$svcNow  = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$taskNow = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$filesNow = Test-Path $InstallDir
if ($svcNow -or $taskNow -or $filesNow) {
    Write-Host ""
    Write-Host "  A previous Chum installation was detected:" -ForegroundColor Yellow
    Write-Host "    Service : $(if ($svcNow)  {'installed'} else {'not found'})" -ForegroundColor DarkGray
    Write-Host "    Files   : $(if ($filesNow){"$InstallDir"} else {'not found'})" -ForegroundColor DarkGray
    Write-Host "    Task    : $(if ($taskNow) {'present'} else {'absent'})" -ForegroundColor DarkGray
    Write-Host "  What would you like to do?" -ForegroundColor White
    Write-Host "    [U] Update / reinstall (replace it)   (recommended)" -ForegroundColor Gray
    Write-Host "    [R] Remove it and exit" -ForegroundColor Gray
    Write-Host "    [C] Cancel" -ForegroundColor Gray
    $c = Read-Choice "  Choice [U]" @('U','R','C') 'U'
    switch ($c) {
        'R' { Remove-ChumInstall; Write-Host "`n  Done. Chum has been removed.`n" -ForegroundColor Green; exit 0 }
        'C' { Write-Host "  Cancelled by user." -ForegroundColor Yellow; exit 0 }
        default { Write-Ok "Will update the existing installation" }
    }
}

# ===========================================================================
# Get the binaries
# ===========================================================================
Resolve-Source
if (-not $script:SvcPublishDir -or -not $script:AppPublishDir) {
    throw "Internal error: install source could not be resolved."
}
$SvcPublishDir = $script:SvcPublishDir
$AppPublishDir = $script:AppPublishDir

# ===========================================================================
# Stop anything currently running, then install
# ===========================================================================
$existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingSvc) {
    Write-Step "Stopping existing $ServiceName service..."
    if ($existingSvc.Status -eq 'Running') { Stop-Service -Name $ServiceName -Force; Start-Sleep -Seconds 2 }
    Write-Step "Removing existing service registration..."
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
    Write-Ok "Existing service removed"
}

Write-Step "Stopping any running Chum processes..."
$chumProcs = Get-Process -Name 'Chum.App', 'ChumHostSvc' -ErrorAction SilentlyContinue
if ($chumProcs) {
    $chumProcs | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Ok "Chum processes stopped ($($chumProcs.Count) process(es))"
} else {
    Write-Skip "No running Chum processes found"
}

$runningTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($runningTask -and $runningTask.State -eq 'Running') {
    Write-Step "Stopping scheduled task '$TaskName'..."
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Ok "Scheduled task stopped"
}

Write-Step "Copying service files -> $ServiceInstallDir"
New-Item -ItemType Directory -Force -Path $ServiceInstallDir | Out-Null
Copy-Item "$SvcPublishDir\*" $ServiceInstallDir -Recurse -Force
Write-Ok "Service files copied"

Write-Step "Copying tray app files -> $AppInstallDir"
New-Item -ItemType Directory -Force -Path $AppInstallDir | Out-Null
Copy-Item "$AppPublishDir\*" $AppInstallDir -Recurse -Force
Write-Ok "Tray app files copied"

# --- Data directory with ACLs ----------------------------------------------
Write-Step "Creating $DataDir with ACLs..."
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
$acl = Get-Acl $DataDir
$acl.SetAccessRuleProtection($false, $true)
foreach ($entry in @(
    @('NT AUTHORITY\SYSTEM',   'FullControl'),
    @('BUILTIN\Administrators','FullControl'),
    @('BUILTIN\Users',         'ReadAndExecute')
)) {
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $entry[0], $entry[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -Path $DataDir -AclObject $acl
Write-Ok "Data directory created with ACLs"

# --- Event Log source -------------------------------------------------------
Write-Step "Registering Event Log source '$EventSource'..."
if (-not (Test-Path $EventRegPath)) { New-Item -Path $EventRegPath -Force | Out-Null }
Set-ItemProperty -Path $EventRegPath -Name 'EventMessageFile' `
    -Value "$env:SystemRoot\System32\EventCreate.exe" -Type ExpandString
Set-ItemProperty -Path $EventRegPath -Name 'TypesSupported' -Value 7 -Type DWord
Write-Ok "Event Log source registered"

# --- Register the service ---------------------------------------------------
Write-Step "Registering ChumHostSvc service..."
& sc.exe create $ServiceName binPath= "`"$ServiceExe`"" start= auto DisplayName= "$ServiceDisplay" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed (exit $LASTEXITCODE)" }
& sc.exe description $ServiceName "$ServiceDesc" | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/10000/none/0 | Out-Null
Write-Ok "ChumHostSvc service registered (auto-start, LocalSystem)"

# --- config.json (writable by Users) ---------------------------------------
Write-Step "Creating config.json..."
$configPath = Join-Path $AppInstallDir 'config.json'
if (-not (Test-Path $configPath)) {
    @'
{
  "AnthropicApiKey": "",
  "OpenAiApiKey": ""
}
'@ | Out-File -FilePath $configPath -Encoding utf8 -NoNewline
}
$configAcl = Get-Acl $configPath
$configAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    'BUILTIN\Users', 'Modify', 'None', 'None', 'Allow')))
Set-Acl -Path $configPath -AclObject $configAcl
Write-Ok "config.json ready"

# --- Scheduled task ---------------------------------------------------------
Write-Step "Creating scheduled task '$TaskName'..."
Register-ScheduledTask -Force `
    -TaskName  $TaskName `
    -Action    (New-ScheduledTaskAction -Execute "$AppInstallDir\Chum.App.exe") `
    -Trigger   (New-ScheduledTaskTrigger -AtLogOn) `
    -Principal (New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Users' -RunLevel Limited) `
    -Settings  (New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 0)) `
    -Description "Starts the Chum tray application on user logon." | Out-Null
Write-Ok "Scheduled task created"

# --- Installation event -----------------------------------------------------
Write-Step "Writing installation event to Application log..."
try {
    Write-EventLog -LogName Application -Source $EventSource -EventId 1000 `
        -EntryType Information `
        -Message "Chum installed. Service: $ServiceName. Path: $InstallDir."
    Write-Ok "Installation event written (EventId 1000)"
} catch {
    Write-Warning "Could not write to Event Log (non-fatal): $($_.Exception.Message)"
}

# --- Start service ----------------------------------------------------------
if ($StartService) {
    Write-Step "Starting $ServiceName..."
    Start-Service -Name $ServiceName
    Write-Ok "Service status: $((Get-Service -Name $ServiceName).Status)"
}

Write-Host ""
Write-Host "  Installation complete." -ForegroundColor Green
Write-Host ""
Write-Host "  Service:        sc query $ServiceName" -ForegroundColor DarkGray
Write-Host "  Event log:      Get-EventLog -Log Application -Source Chum -Newest 5" -ForegroundColor DarkGray
Write-Host "  Scheduled task: schtasks /Query /TN `"$TaskName`" /FO LIST" -ForegroundColor DarkGray
Write-Host "  Uninstall:      run install.cmd again and choose [R], or scripts\Uninstall-Chum.ps1" -ForegroundColor DarkGray
Write-Host ""
