#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Chum Collaboration Host without an MSI (developer / CI use).

.DESCRIPTION
    Publishes Chum.Service and Chum.App, copies them to %ProgramFiles%\Chum\,
    creates the %PROGRAMDATA%\Chum\ data directory with the correct ACLs,
    registers ChumHostSvc as a Windows service (auto-start, LocalSystem),
    creates a scheduled task so the tray app starts on every user logon,
    and writes EventId 1000 to the Windows Application Event Log.

    Use the WiX MSI (Chum.Installer) for production distribution -- this script
    is intended for development, testing, and CI pipeline validation.

.PARAMETER InstallDir
    Root installation directory. Default: %ProgramFiles%\Chum

.PARAMETER DataDir
    Runtime data directory. Default: %PROGRAMDATA%\Chum

.PARAMETER StartService
    Start ChumHostSvc immediately after registration.

.PARAMETER DotnetRuntime
    .NET runtime version required for the hosted executables. Default: 10.0.

.EXAMPLE
    # Install with defaults (stops and replaces any existing installation)
    .\Install-Chum.ps1

.EXAMPLE
    # Install and start the service immediately
    .\Install-Chum.ps1 -StartService
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDir  = "$env:ProgramFiles\Chum",
    [string]$DataDir     = "$env:ProgramData\Chum",
    [switch]$StartService,
    [string]$DotnetRuntime = '10.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $ScriptDir) { throw "Cannot determine script directory. Run as: powershell -File scripts\Install-Chum.ps1" }
$RepoRoot  = Split-Path $ScriptDir -Parent
$SrcRoot   = Join-Path $RepoRoot 'src'

$ServiceProject = Join-Path $SrcRoot 'Chum.Service\Chum.Service.csproj'
$AppProject     = Join-Path $SrcRoot 'Chum.App\Chum.App.csproj'
$SvcPublishDir  = Join-Path $SrcRoot 'Chum.Service\publish'
$AppPublishDir  = Join-Path $SrcRoot 'Chum.App\publish'

$ServiceInstallDir = Join-Path $InstallDir 'Service'
$AppInstallDir     = Join-Path $InstallDir 'App'

$ServiceExe        = Join-Path $ServiceInstallDir 'ChumHostSvc.exe'
$ServiceName       = 'ChumHostSvc'
$ServiceDisplay    = 'Chum Collaboration Host'
$ServiceDesc       = 'Chum AI meeting co-pilot: audio capture, transcription, and LLM integration.'
$TaskName          = 'Chum Tray Application'
$EventSource       = 'Chum'

function Write-Step([string]$Message) {
    Write-Host "  $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

# -- 1. Verify dotnet SDK is available ----------------------------------------
Write-Host "`nChum Installer" -ForegroundColor White
Write-Host "-----------------------------------------" -ForegroundColor DarkGray

Write-Step "Checking .NET SDK..."
try {
    $sdkVer = (& dotnet --version 2>&1)
    Write-Ok ".NET SDK $sdkVer found"
} catch {
    Write-Error ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
}

# -- 2. Publish Chum.Service --------------------------------------------------
Write-Step "Publishing Chum.Service -> $SvcPublishDir"
& dotnet publish $ServiceProject -r win-x64 -c Release -o $SvcPublishDir --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for Chum.Service" }
Write-Ok "Chum.Service published"

# -- 3. Publish Chum.App ------------------------------------------------------
Write-Step "Publishing Chum.App -> $AppPublishDir"
& dotnet publish $AppProject -r win-x64 -c Release -o $AppPublishDir --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for Chum.App" }
Write-Ok "Chum.App published"

# -- 4. Stop and remove existing service (if present) -------------------------
$existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingSvc) {
    Write-Step "Stopping existing $ServiceName service..."
    if ($existingSvc.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    Write-Step "Removing existing service registration..."
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
    Write-Ok "Existing service removed"
}

# -- 5. Copy files to %ProgramFiles%\Chum\ ------------------------------------
Write-Step "Copying service files -> $ServiceInstallDir"
New-Item -ItemType Directory -Force -Path $ServiceInstallDir | Out-Null
Copy-Item -Path "$SvcPublishDir\*" -Destination $ServiceInstallDir -Recurse -Force
Write-Ok "Service files copied"

Write-Step "Copying tray app files -> $AppInstallDir"
New-Item -ItemType Directory -Force -Path $AppInstallDir | Out-Null
Copy-Item -Path "$AppPublishDir\*" -Destination $AppInstallDir -Recurse -Force
Write-Ok "Tray app files copied"

# -- 6. Create %PROGRAMDATA%\Chum\ with ACLs ----------------------------------
Write-Step "Creating $DataDir with ACLs..."
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

$acl = Get-Acl $DataDir
$acl.SetAccessRuleProtection($false, $true)

# SYSTEM: full control (service writes audit log)
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'NT AUTHORITY\SYSTEM',
    'FullControl',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow')
$acl.AddAccessRule($systemRule)

# Administrators: full control
$adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'BUILTIN\Administrators',
    'FullControl',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow')
$acl.AddAccessRule($adminRule)

# Users: read (view audit log)
$userRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'BUILTIN\Users',
    'ReadAndExecute',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow')
$acl.AddAccessRule($userRule)

Set-Acl -Path $DataDir -AclObject $acl
Write-Ok "Data directory created with ACLs"

# -- 7. Register Windows Event Log source -------------------------------------
Write-Step "Registering Event Log source '$EventSource'..."
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$EventSource"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name 'EventMessageFile' -Value "$env:SystemRoot\System32\EventCreate.exe" -Type ExpandString
Set-ItemProperty -Path $regPath -Name 'TypesSupported'   -Value 7 -Type DWord
Write-Ok "Event Log source registered"

# -- 8. Register ChumHostSvc Windows service ----------------------------------
Write-Step "Registering ChumHostSvc service..."
& sc.exe create $ServiceName `
    binPath= "`"$ServiceExe`"" `
    start= auto `
    DisplayName= "$ServiceDisplay" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "sc.exe create failed (exit $LASTEXITCODE)" }

& sc.exe description $ServiceName "$ServiceDesc" | Out-Null

# Configure failure recovery: restart on 1st and 2nd failure; reset counter daily
& sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/10000/none/0 | Out-Null
Write-Ok "ChumHostSvc service registered (auto-start, LocalSystem)"

# -- 9. Create scheduled task for tray app ------------------------------------
Write-Step "Creating scheduled task '$TaskName'..."
$taskAction  = New-ScheduledTaskAction -Execute "$AppInstallDir\Chum.App.exe"
$taskTrigger = New-ScheduledTaskTrigger -AtLogOn
$taskPrincipal = New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Users' -RunLevel Limited
$taskSettings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 0)

Register-ScheduledTask `
    -Force `
    -TaskName  $TaskName `
    -Action    $taskAction `
    -Trigger   $taskTrigger `
    -Principal $taskPrincipal `
    -Settings  $taskSettings `
    -Description "Starts the Chum tray application on user logon." | Out-Null
Write-Ok "Scheduled task created"

# -- 10. Write EventId 1000 (installation event) ------------------------------
Write-Step "Writing installation event to Application log..."
try {
    Write-EventLog -LogName Application -Source $EventSource `
        -EventId 1000 -EntryType Information `
        -Message "Chum Collaboration Host installed successfully. Service: $ServiceName. Install path: $InstallDir."
    Write-Ok "Installation event written (EventId 1000)"
} catch {
    Write-Warning "Could not write to Event Log (non-fatal): $_"
}

# -- 11. Start service (optional) ---------------------------------------------
if ($StartService) {
    Write-Step "Starting $ServiceName..."
    Start-Service -Name $ServiceName
    $svc = Get-Service -Name $ServiceName
    Write-Ok "Service status: $($svc.Status)"
}

Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host ""
Write-Host "  Service:        sc query $ServiceName" -ForegroundColor DarkGray
Write-Host "  Event log:      Get-EventLog -Log Application -Source Chum -Newest 5" -ForegroundColor DarkGray
Write-Host "  Scheduled task: schtasks /Query /TN `"$TaskName`" /FO LIST" -ForegroundColor DarkGray
Write-Host "  Uninstall:      .\Uninstall-Chum.ps1" -ForegroundColor DarkGray
Write-Host ""
