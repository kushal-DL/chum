<#
.SYNOPSIS
    Starts the Google AI Mode image search bridge for Chum.

    Opens a Chrome window and serves POST /image on http://127.0.0.1:8002.
    Chum sends a screenshot region here; this script returns Google AI Mode's response.

    FIRST RUN (or after Google shows a CAPTCHA):
        Solve the "I am not a robot" check in the Chrome window that opens.
        After that, the session is saved in google-session/ for future runs.

    Do NOT close the Chrome window -- Playwright controls it.
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
Write-Host "[internet-search] A Chrome window will open." -ForegroundColor Yellow
Write-Host "[internet-search] If Google shows a CAPTCHA, solve it in the browser -- it will not appear again." -ForegroundColor Yellow
Write-Host "[internet-search] Do NOT close the Chrome window -- Playwright controls it." -ForegroundColor Yellow
Write-Host ""

python internet-search-api.py
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "internet-search-api.py exited with code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Press Enter to close..." -ForegroundColor Gray
    $null = Read-Host
}
