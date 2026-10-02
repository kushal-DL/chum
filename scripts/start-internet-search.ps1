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
    [switch]$Cdp,       # Attach to already-running Chrome on port 9222
    [int]$Port = 8002,
    [string]$ApiKey = "llm"
)

$ErrorActionPreference = "Continue"

if ($PSScriptRoot) { Set-Location $PSScriptRoot }

# Write GoogleSearchApiKey into the installed app's config.json so Chum authenticates
# against this bridge with the same key it enforces.
$configPath = "$env:ProgramFiles\Chum\App\config.json"
if (Test-Path $configPath) {
    try {
        $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
        $needsSave = $false
        if ($cfg.GoogleSearchApiKey -ne $ApiKey) {
            $cfg | Add-Member -NotePropertyName GoogleSearchApiKey -NotePropertyValue $ApiKey -Force
            $needsSave = $true
        }
        if ($needsSave) {
            $cfg | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding utf8
            Write-Host "Set GoogleSearchApiKey in config.json -> '$ApiKey'" -ForegroundColor Green
        } else {
            Write-Host "GoogleSearchApiKey already set in config.json" -ForegroundColor DarkGray
        }
    } catch {
        Write-Warning "Could not update config.json (non-fatal): $_"
    }
} else {
    Write-Host "config.json not found at $configPath - Chum not installed yet." -ForegroundColor DarkGray
}

# Open firewall so other machines on the LAN can reach this bridge.
#     Only needed once - UAC prompt appears the first time, rule persists forever.
$fwRule = "Chum Google Search API port $Port"
if (-not (Get-NetFirewallRule -DisplayName $fwRule -ErrorAction SilentlyContinue)) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        New-NetFirewallRule -DisplayName $fwRule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    } else {
        Write-Host "Adding firewall rule for port $Port (UAC prompt will appear - one time only)..." -ForegroundColor Yellow
        Start-Process powershell -Verb RunAs -Wait -ArgumentList "-ExecutionPolicy Bypass -NoProfile -Command `"New-NetFirewallRule -DisplayName '$fwRule' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null`""
    }
    Write-Host "Firewall rule added for port $Port." -ForegroundColor Green
}

$lanIp = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -like "192.168.*" -and $_.PrefixOrigin -in "Dhcp","Manual" } |
    Select-Object -First 1).IPAddress
if (-not $lanIp) {
    $lanIp = (Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and
                       $_.IPAddress -notlike "172.*" -and $_.PrefixOrigin -in "Dhcp","Manual" } |
        Select-Object -First 1).IPAddress
}
if (-not $lanIp) { $lanIp = "<your-lan-ip>" }

$env:PORT    = $Port
$env:API_KEY = $ApiKey

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

# -- Kill any previous instance holding the port --
$old = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "[internet-search] Stopping previous instance on port $Port..." -ForegroundColor Yellow
    $old | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

# -- Launch --
Write-Host ""
Write-Host "=== Chum Google Search API ===" -ForegroundColor Cyan
Write-Host "Base URL : http://${lanIp}:$Port  (LAN)"
Write-Host "           http://127.0.0.1:$Port  (local)"
Write-Host "API Key  : $ApiKey"
Write-Host "===============================" -ForegroundColor Cyan
if ($Cdp) {
    Write-Host "[internet-search] CDP mode: attaching to Chrome on port 9222" -ForegroundColor Green
    Write-Host "[internet-search] Make sure Chrome is running with --remote-debugging-port=9222" -ForegroundColor Yellow
    $env:CDP_URL = "http://localhost:9222"
} else {
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
