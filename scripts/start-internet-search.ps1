<#
.SYNOPSIS
    Starts the Google AI Mode image search bridge for Chum.
    Serves POST /image on http://127.0.0.1:8002.

TWO MODES:

  Mode 1 -- Default (no extra steps, recommended for most users):
    Playwright opens a separate Chrome window.
    If Google shows a "I am not a robot" check, solve it once in that window.
    The session is saved in google-session/ so it only happens once.

  Mode 2 -- Attach to YOUR Chrome (no CAPTCHA, uses your Google account):
    1. Close your current Chrome.
    2. Open Chrome with: Start -> Run -> paste this and press Enter:
          chrome.exe --remote-debugging-port=9222
    3. Run this script with the -Cdp flag:
          .\start-internet-search.ps1 -Cdp
    Playwright will attach to YOUR Chrome window -- no new browser opens.

#>
param(
    [switch]$Cdp  # Attach to already-running Chrome on port 9222
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

# -- Python --
Write-Host "[internet-search] Checking Python..." -ForegroundColor Cyan
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { Bail "Python 3.10+ not found in PATH. Install from https://python.org and re-run." }
$pyVer = python --version 2>&1
Write-Host "[internet-search] Found: $pyVer" -ForegroundColor Cyan

# -- pip dependencies --
Write-Host "[internet-search] Installing/verifying Python packages..." -ForegroundColor Yellow
pip install fastapi "uvicorn[standard]" playwright pydantic --quiet
if ($LASTEXITCODE -ne 0) { Bail "pip install failed -- check the output above." }

# -- Playwright browser binary --
Write-Host "[internet-search] Checking Playwright Chromium..." -ForegroundColor Yellow
python -m playwright install chromium
if ($LASTEXITCODE -ne 0) { Bail "playwright install chromium failed -- check the output above." }

# -- Kill any previous instance holding port 8002 --
$old = Get-NetTCPConnection -LocalPort 8002 -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "[internet-search] Stopping previous instance on port 8002..." -ForegroundColor Yellow
    $old | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

# -- Launch --
Write-Host ""
if ($Cdp) {
    Write-Host "[internet-search] CDP mode: attaching to Chrome on port 9222" -ForegroundColor Green
    Write-Host "[internet-search] Make sure Chrome is running with --remote-debugging-port=9222" -ForegroundColor Yellow
    $env:CDP_URL = "http://localhost:9222"
} else {
    Write-Host "[internet-search] Starting Google AI Search bridge on http://127.0.0.1:8002" -ForegroundColor Green
    Write-Host "[internet-search] A separate Chrome window will open." -ForegroundColor Yellow
    Write-Host "[internet-search] If Google shows a CAPTCHA, solve it once -- it will not appear again." -ForegroundColor Yellow
    $env:CDP_URL = ""
}
Write-Host ""

python internet-search-api.py
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "internet-search-api.py exited with code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Press Enter to close..." -ForegroundColor Gray
    $null = Read-Host
}
