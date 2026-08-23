<#
.SYNOPSIS
    Starts the Google AI Mode image search bridge for Chum.

    Launches an incognito Chrome window via Playwright, navigates to Google AI Mode,
    and serves POST /image on http://127.0.0.1:8002.

    Chum sends a screenshot here; the script returns Google's AI response text.
    Do NOT close the Chrome window that opens — Playwright controls it.
#>
param()
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "[internet-search] Checking Python..." -ForegroundColor Cyan
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    Write-Error "Python 3.10+ not found in PATH. Install from https://python.org and re-run."
    exit 1
}

# Install Python dependencies
$deps = @(
    @{ pkg = "fastapi";    mod = "fastapi"   },
    @{ pkg = "uvicorn";    mod = "uvicorn"   },
    @{ pkg = "playwright"; mod = "playwright" },
    @{ pkg = "pydantic";   mod = "pydantic"  }
)
foreach ($d in $deps) {
    python -c "import $($d.mod)" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[internet-search] Installing $($d.pkg)..." -ForegroundColor Yellow
        pip install $d.pkg --quiet
    }
}

# Ensure Playwright's Chromium browser binary is installed
Write-Host "[internet-search] Checking Playwright Chromium..." -ForegroundColor Yellow
python -m playwright install chromium 2>&1 | Where-Object { $_ -match "chromium|browser|download" }

Write-Host ""
Write-Host "[internet-search] Starting Google AI Search bridge on http://127.0.0.1:8002" -ForegroundColor Green
Write-Host "[internet-search] A Chrome window will open automatically. Do NOT close it." -ForegroundColor Yellow
Write-Host ""

python internet-search-api.py
