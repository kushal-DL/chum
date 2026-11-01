<#
.SYNOPSIS
    Starts the ASP RAG bridge for Chum.
    Binds to 0.0.0.0 by default so other machines on the LAN can reach it.
    Set Chum's Settings → ASP RAG → Bridge URL to http://<this-machine-ip>:<Port>

RUNNING ON THE LLM MACHINE (typical deployment):
    .\start-asp-rag.ps1
    Then set Chum's ASP RAG URL to the LAN IP shown below, e.g. http://192.168.1.50:8003

RUNNING ON THE SAME MACHINE AS CHUM:
    .\start-asp-rag.ps1 -Port 8003
    Then set Chum's ASP RAG URL to http://127.0.0.1:8003

CUSTOM ASP WHEEL PATH:
    .\start-asp-rag.ps1 -AspWheelPath "C:\path\to\asp-0.1.0-py3-none-any.whl"

#>
param(
    [int]$Port = 8003,
    [string]$AspWheelPath = "",
    [string]$DocsDir = ""
)

$ErrorActionPreference = "Continue"

if ($PSScriptRoot) { Set-Location $PSScriptRoot }

function Bail($msg) {
    Write-Host ""
    Write-Host "ERROR: $msg" -ForegroundColor Red
    Write-Host ""
    Write-Host "Press Enter to close..." -ForegroundColor Gray
    $null = Read-Host
    exit 1
}

# -- Resolve ASP wheel --
if (-not $AspWheelPath) {
    $candidates = @(
        (Join-Path $PSScriptRoot "..\..\..\ASP\dist\asp-0.1.0-py3-none-any.whl"),
        (Join-Path $PSScriptRoot "..\..\ASP\dist\asp-0.1.0-py3-none-any.whl"),
        (Join-Path $PSScriptRoot "..\ASP\dist\asp-0.1.0-py3-none-any.whl")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $AspWheelPath = (Resolve-Path $c).Path; break }
    }
    if (-not $AspWheelPath) {
        $distDirs = @(
            (Join-Path $PSScriptRoot "..\..\..\ASP\dist"),
            (Join-Path $PSScriptRoot "..\..\ASP\dist"),
            (Join-Path $PSScriptRoot "..\ASP\dist")
        )
        foreach ($d in $distDirs) {
            if (Test-Path $d) {
                $whl = Get-ChildItem -Path $d -Filter "asp-*.whl" -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($whl) { $AspWheelPath = $whl.FullName; break }
            }
        }
    }
}
if (-not $AspWheelPath -or -not (Test-Path $AspWheelPath)) {
    Bail "ASP wheel not found. Pass -AspWheelPath 'C:\path\to\asp-0.1.0-py3-none-any.whl'"
}
Write-Host "[asp-rag] Using ASP wheel: $AspWheelPath" -ForegroundColor Cyan

# -- Python --
Write-Host "[asp-rag] Checking Python..." -ForegroundColor Cyan
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { Bail "Python 3.10+ not found in PATH. Install from https://python.org and re-run." }
$pyVer = python --version 2>&1
Write-Host "[asp-rag] Found: $pyVer" -ForegroundColor Cyan

# -- pip dependencies --
Write-Host "[asp-rag] Installing/verifying Python packages..." -ForegroundColor Yellow
pip install "$AspWheelPath" fastapi "uvicorn[standard]" sentence-transformers pydantic --quiet
if ($LASTEXITCODE -ne 0) { Bail "pip install failed -- check the output above." }

# -- Kill any previous instance on the port --
$old = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "[asp-rag] Stopping previous instance on port $Port..." -ForegroundColor Yellow
    $old | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

# -- Firewall rule (allows LAN access — bridge always binds 0.0.0.0) --
$fwRule = "Chum ASP RAG bridge port $Port"
if (-not (Get-NetFirewallRule -DisplayName $fwRule -ErrorAction SilentlyContinue)) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        New-NetFirewallRule -DisplayName $fwRule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
        Write-Host "[asp-rag] Firewall rule added for port $Port." -ForegroundColor Green
    } else {
        Write-Host "[asp-rag] Adding firewall rule for port $Port (UAC prompt — one time only)..." -ForegroundColor Yellow
        Start-Process powershell -Verb RunAs -Wait -ArgumentList "-ExecutionPolicy Bypass -NoProfile -Command `"New-NetFirewallRule -DisplayName '$fwRule' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null`""
    }
}

# -- Show LAN IP --
$lanIp = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and
                   $_.PrefixOrigin -in "Dhcp","Manual" } |
    Select-Object -First 1).IPAddress

if (-not $DocsDir) {
    $DocsDir = Join-Path $PSScriptRoot "..\rag_docs"
}
if (-not (Test-Path $DocsDir)) {
    New-Item -ItemType Directory -Path $DocsDir -Force | Out-Null
}
$env:ASP_RAG_DOCS_DIR = (Resolve-Path $DocsDir).Path

$env:BRIDGE_HOST = "0.0.0.0"
$env:BRIDGE_PORT = "$Port"

Write-Host ""
Write-Host "[asp-rag] Bridge starting on port $Port (all interfaces)" -ForegroundColor Green
if ($lanIp) {
    Write-Host "[asp-rag] Set Chum's ASP RAG Bridge URL to:" -ForegroundColor Cyan
    Write-Host "    http://${lanIp}:$Port" -ForegroundColor White
    Write-Host "[asp-rag] Or use http://127.0.0.1:$Port if Chum runs on this machine." -ForegroundColor Gray
}
Write-Host "[asp-rag] Dedicated RAG docs folder: $($env:ASP_RAG_DOCS_DIR)" -ForegroundColor Cyan
Write-Host "[asp-rag] Auto-indexes documents from docs folder or via Chum desktop client." -ForegroundColor Gray
Write-Host ""

python asp-rag-api.py
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "asp-rag-api.py exited with code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Press Enter to close..." -ForegroundColor Gray
    $null = Read-Host
}
