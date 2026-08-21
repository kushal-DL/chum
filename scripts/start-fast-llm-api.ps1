<#
.SYNOPSIS
    Downloads (if needed) and starts the fast local LLM for Chum — Qwen3-4B Q4_K_M
    on port 8003 with a 2048-token context window.

.DESCRIPTION
    Complements start-llm-api.ps1 (Qwen3.5-9B on port 8001). The fast model handles
    response modes 1 (Default) and 2 (Quick Response) where first-token latency matters
    more than response depth. The UI model-toggle button switches between the two at runtime.

    Endpoints served:
        POST /v1/chat/completions   - text only (no vision projector)
        GET  /v1/models

    VRAM: ~2.5 GB (Qwen3-4B Q4_K_M), leaving ~7 GB for the quality model and Whisper.

.PARAMETER Port
    Port to listen on. Default 8003.

.PARAMETER ModelRepo
    Hugging Face repo for the GGUF. Default: bartowski/Qwen3-4B-GGUF

.PARAMETER ModelFile
    GGUF filename. Default: Qwen3-4B-Q4_K_M.gguf

.PARAMETER ContextSize
    Context window in tokens. Default 2048.

.PARAMETER ApiKey / NoAuth
    Same pattern as the other start-*.ps1 scripts.
#>
param(
    [int]$Port         = 8003,
    [string]$ModelRepo = "bartowski/Qwen3-4B-GGUF",
    [string]$ModelFile = "Qwen3-4B-Q4_K_M.gguf",
    [int]$ContextSize  = 2048,
    [string]$ApiKey    = "",
    [switch]$NoAuth,
    [string]$ToolsDir  = "F:\repos\chum\local-llm\llama.cpp",
    [string]$ModelsDir = "F:\repos\chum\local-llm\models"
)

$ErrorActionPreference = "Stop"

trap {
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host ""
    powershell -Command "Read-Host 'Press Enter to close'"
    exit 1
}

# 1. Ensure llama-server.exe is present
$serverExe = Join-Path $ToolsDir "llama-server.exe"
if (-not (Test-Path $serverExe)) {
    Write-Host "llama-server.exe not found, downloading latest Vulkan build..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -like "*bin-win-vulkan-x64.zip" } | Select-Object -First 1
    if (-not $asset) { throw "Could not find a win-vulkan-x64 asset in the latest llama.cpp release." }

    $zipPath = Join-Path $ToolsDir $asset.name
    Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."
    curl.exe -L -o $zipPath $asset.browser_download_url
    Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
    Remove-Item $zipPath

    if (-not (Test-Path $serverExe)) { throw "Extraction did not produce llama-server.exe - check $ToolsDir." }
    Write-Host "llama.cpp installed at $ToolsDir" -ForegroundColor Green
}

# 2. Ensure the model GGUF is present
$modelPath = Join-Path $ModelsDir $ModelFile
if (-not (Test-Path $modelPath)) {
    Write-Host "Fast model not found, downloading $ModelFile from $ModelRepo..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    curl.exe -L -C - -o $modelPath "https://huggingface.co/$ModelRepo/resolve/main/$ModelFile"
    Write-Host "Fast model downloaded to $modelPath" -ForegroundColor Green
}

# 3. Open firewall - one-time UAC prompt if rule not yet present
$fwRule = "Chum Fast LLM API port $Port"
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
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -in "Dhcp","Manual" } |
    Select-Object -First 1).IPAddress
if (-not $lanIp) { $lanIp = "<your-lan-ip>" }

$keyLabel = if ($NoAuth) { "(none - auth disabled)" } else { if ($ApiKey) { $ApiKey } else { "chum-llm-key-2026 (default)" } }

# 4. Banner
Write-Host ""
Write-Host "=== Chum Fast LLM API (Qwen3-4B) ===" -ForegroundColor Yellow
Write-Host "Base URL : http://${lanIp}:$Port/v1  (LAN)"
Write-Host "           http://127.0.0.1:$Port/v1  (local)"
Write-Host "API Key  : $keyLabel"
Write-Host "Model    : $ModelFile"
Write-Host "Context  : $ContextSize tokens"
Write-Host "Vision   : OFF (text only)"
Write-Host "======================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "Starting server..." -ForegroundColor Yellow
Write-Host ""

# 5. Launch via start-llm.py
$launchScript = Join-Path $PSScriptRoot "start-llm.py"

$pyArgs = @(
    "--port",         $Port,
    "--host",         "0.0.0.0",
    "--model-path",   $modelPath,
    "--no-mmproj",
    "--context-size", $ContextSize
)
if ($NoAuth)      { $pyArgs += "--no-auth" }
elseif ($ApiKey)  { $pyArgs += @("--api-key", $ApiKey) }

python $launchScript @pyArgs
$code = $LASTEXITCODE
if ($code -ne 0) {
    Write-Host ""
    Write-Host "Server exited with code $code" -ForegroundColor Red
}
powershell -Command "Read-Host 'Server has stopped. Press Enter to close'"
