#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a self-contained deployment package for Chum that can be installed
    on machines without the source code or .NET SDK.

.DESCRIPTION
    Runs dotnet publish for Chum.Service and Chum.App, then assembles a deploy
    folder (default: .\chum-deploy\) containing:
        App\           published Chum.App output
        Service\       published Chum.Service output
        scripts\       Install-Chum.ps1 and Uninstall-Chum.ps1
        install.cmd    installer entry-point (run as administrator)

    Copy the entire chum-deploy\ folder to the target machine and run install.cmd
    as Administrator. No source code or .NET SDK is required on the target.

.PARAMETER OutputDir
    Destination folder for the deploy package. Default: .\chum-deploy

.EXAMPLE
    .\Build-DeployPackage.ps1
    # Then copy chum-deploy\ to the target machine and run install.cmd as admin.
#>
param(
    [string]$OutputDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'chum-deploy')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir   = $PSScriptRoot
$RepoRoot    = Split-Path $ScriptDir -Parent
$SrcRoot     = Join-Path $RepoRoot 'src'
$ServiceProj = Join-Path $SrcRoot 'Chum.Service\Chum.Service.csproj'
$AppProj     = Join-Path $SrcRoot 'Chum.App\Chum.App.csproj'

function Write-Step([string]$m) { Write-Host "  $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  [OK] $m" -ForegroundColor Green }

Write-Host "`nChum Deploy Package Builder" -ForegroundColor White
Write-Host "-----------------------------------------" -ForegroundColor DarkGray

# Publish Service
Write-Step "Publishing Chum.Service..."
$svcOut = Join-Path $OutputDir 'Service'
& dotnet publish $ServiceProj -r win-x64 -c Release -o $svcOut --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Chum.Service" }
Write-Ok "Chum.Service -> $svcOut"

# Publish App
Write-Step "Publishing Chum.App..."
$appOut = Join-Path $OutputDir 'App'
& dotnet publish $AppProj -r win-x64 -c Release -o $appOut --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for Chum.App" }
Write-Ok "Chum.App -> $appOut"

# Copy scripts and install.cmd
Write-Step "Copying installer scripts..."
$destScripts = Join-Path $OutputDir 'scripts'
New-Item -ItemType Directory -Force -Path $destScripts | Out-Null
Copy-Item (Join-Path $ScriptDir 'Install-Chum.ps1')   $destScripts -Force
Copy-Item (Join-Path $ScriptDir 'Uninstall-Chum.ps1') $destScripts -Force
Copy-Item (Join-Path $RepoRoot  'install.cmd')         $OutputDir   -Force
Write-Ok "Scripts copied"

Write-Host ""
Write-Host "  Deploy package ready: $OutputDir" -ForegroundColor Green
Write-Host ""
Write-Host "  To install on another machine:" -ForegroundColor DarkGray
Write-Host "    1. Copy the entire '$([System.IO.Path]::GetFileName($OutputDir))\' folder to the target PC" -ForegroundColor DarkGray
Write-Host "    2. Right-click install.cmd -> Run as administrator" -ForegroundColor DarkGray
Write-Host ""
