<#
.SYNOPSIS
    Starts the Google AI Mode image search bridge for Chum.

    Opens a Chrome window (using a saved session in google-session/) and serves
    POST /image on http://127.0.0.1:8002.

    FIRST RUN: Chrome will ask you to sign in to Google -- do so, then the API
               becomes ready automatically. Your session is saved for future runs.

    Chum sends a screenshot here; the script returns Google AI Mode's response text.
    Do NOT close the Chrome window that opens -- Playwright controls it.
#>
param()

# Never Stop -- a non-zero exit from a native command (python, pip) would
# silently kill the window before the user can read the error.
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
Write-Host "[internet-search] Installing Playwright Chromium (skipped if already present)..." -ForegroundColor Yellow
python -m playwright install chromium
if ($LASTEXITCODE -ne 0) { Bail "playwright install chromium failed -- check the output above." }

# -- Launch --
Write-Host ""
Write-Host "[internet-search] Starting Google AI Search bridge on http://127.0.0.1:8002" -ForegroundColor Green
Write-Host "[internet-search] A Chrome window will open. FIRST RUN: sign in to Google when prompted." -ForegroundColor Yellow
Write-Host "[internet-search] Do NOT close the Chrome window -- Playwright controls it." -ForegroundColor Yellow
Write-Host ""

python internet-search-api.py
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "internet-search-api.py exited with code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Press Enter to close..." -ForegroundColor Gray
    $null = Read-Host
}
