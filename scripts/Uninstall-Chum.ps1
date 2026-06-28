#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls Chum Collaboration Host (complements Install-Chum.ps1).

.DESCRIPTION
    Stops and removes ChumHostSvc, deletes the scheduled task, removes
    the program files from %ProgramFiles%\Chum\, and removes the Event
    Log source registry key. %PROGRAMDATA%\Chum\ is preserved unless
    -RemoveData is specified (it may contain audit logs).

.PARAMETER InstallDir
    Root installation directory. Default: %ProgramFiles%\Chum

.PARAMETER DataDir
    Runtime data directory. Default: %PROGRAMDATA%\Chum

.PARAMETER RemoveData
    Also remove %PROGRAMDATA%\Chum\ (includes audit logs). Off by default.

.EXAMPLE
    .\Uninstall-Chum.ps1

.EXAMPLE
    # Remove everything including audit logs
    .\Uninstall-Chum.ps1 -RemoveData
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDir  = "$env:ProgramFiles\Chum",
    [string]$DataDir     = "$env:ProgramData\Chum",
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ServiceName  = 'ChumHostSvc'
$TaskName     = 'Chum Tray Application'
$EventSource  = 'Chum'
$EventRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$EventSource"

function Write-Step([string]$Message) {
    Write-Host "  $Message" -ForegroundColor Cyan
}
function Write-Ok([string]$Message) {
    Write-Host "  [OK] $Message" -ForegroundColor Green
}
function Write-Skip([string]$Message) {
    Write-Host "  [--] $Message" -ForegroundColor DarkGray
}

Write-Host "`nChum Uninstaller" -ForegroundColor White
Write-Host "-----------------------------------------" -ForegroundColor DarkGray

# -- 1. Stop and delete Windows service ---------------------------------------
Write-Step "Stopping and removing $ServiceName service..."
$existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingSvc) {
    if ($existingSvc.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Ok "Service stopped and removed"
} else {
    Write-Skip "$ServiceName service not found"
}

# -- 2. Delete scheduled task -------------------------------------------------
Write-Step "Removing scheduled task '$TaskName'..."
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Ok "Scheduled task removed"
} else {
    Write-Skip "Scheduled task not found"
}

# -- 3. Remove Event Log source -----------------------------------------------
Write-Step "Removing Event Log source '$EventSource'..."
if (Test-Path $EventRegPath) {
    Remove-Item -Path $EventRegPath -Recurse -Force
    Write-Ok "Event Log source removed"
} else {
    Write-Skip "Event Log source registry key not found"
}

# -- 4. Remove program files --------------------------------------------------
Write-Step "Removing program files from $InstallDir..."
if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Ok "Program files removed"
} else {
    Write-Skip "$InstallDir not found"
}

# -- 5. Remove %PROGRAMDATA%\Chum\ (opt-in) -----------------------------------
if ($RemoveData) {
    Write-Step "Removing data directory $DataDir..."
    if (Test-Path $DataDir) {
        Remove-Item -Path $DataDir -Recurse -Force
        Write-Ok "Data directory removed"
    } else {
        Write-Skip "$DataDir not found"
    }
} else {
    Write-Skip "Skipping data directory $DataDir (use -RemoveData to remove)"
}

# -- 6. Write uninstall event (best-effort) -----------------------------------
Write-Step "Writing uninstall event to Application log..."
try {
    $src = [System.Diagnostics.EventLog]::SourceExists($EventSource)
    if ($src) {
        Write-EventLog -LogName Application -Source $EventSource `
            -EventId 1001 -EntryType Information `
            -Message "Chum Collaboration Host uninstalled."
        Write-Ok "Uninstall event written (EventId 1001)"
    } else {
        Write-Skip "Event source no longer registered; skipping"
    }
} catch {
    Write-Warning "Could not write to Event Log (non-fatal): $_"
}

Write-Host ""
Write-Host "Uninstallation complete." -ForegroundColor Green
if (-not $RemoveData -and (Test-Path $DataDir)) {
    Write-Host ""
    Write-Host "  Note: $DataDir was preserved (audit logs)." -ForegroundColor DarkYellow
    Write-Host "        Run with -RemoveData to delete it." -ForegroundColor DarkYellow
}
Write-Host ""
