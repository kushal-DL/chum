<#
.SYNOPSIS
    Downloads (if needed) and starts a local, OpenAI-compatible API for Chum,
    backed by Qwen 3.5 9B (Q4_K_M) on your AMD GPU via llama.cpp Vulkan.

.DESCRIPTION
    Handles both text and image (vision) queries on the same endpoint -
    no separate vision server needed. The mmproj vision projector is included
    automatically. start-vision-api.ps1 is not required.

    Endpoints served:
        POST /v1/chat/completions   - text and/or image input
        GET  /v1/models

    VRAM usage on RX 6800 XT (Vulkan, -ngl 999, -c 8192):
        ~6.0 GB text-only / ~6.5 GB with vision projector

    Steps run automatically if files are missing:
        1. Download latest llama.cpp Vulkan release (llama-server.exe)
        2. Download Qwen_Qwen3.5-9B-Q4_K_M.gguf from Hugging Face
        3. Download mmproj-Qwen_Qwen3.5-9B-f16.gguf (vision projector)

    Argument quoting note: --chat-template-kwargs carries a JSON string that
    PowerShell 5.1 mangles when passed directly to native exes. This script
    delegates the actual launch to start-llm.py so Python subprocess handles
    the quoting correctly.

.PARAMETER Port
    Port to listen on. Default 8001 (8000 is reserved for the Whisper STT API).

.PARAMETER ModelRepo
    Hugging Face repo for the GGUF model. Default: bartowski/Qwen_Qwen3.5-9B-GGUF

.PARAMETER ModelFile
    GGUF filename within the repo. Default: Qwen_Qwen3.5-9B-Q4_K_M.gguf

.PARAMETER MmprojFile
    Vision projector filename within the same repo.
    Default: mmproj-Qwen_Qwen3.5-9B-f16.gguf

.PARAMETER GpuLayers
    Layers to offload to GPU. Default 999 (= all; llama.cpp clamps to model max).

.PARAMETER ContextSize
    Context window in tokens. Default 8192.

.PARAMETER ApiKey
    Fixed API key. If omitted, uses the hardcoded key in start-llm.py.
    Pass -NoAuth to disable authentication entirely.

.PARAMETER NoAuth
    Disable API key checking (fine since the server binds to 127.0.0.1 only).

.PARAMETER EnableThinking
    Enable Qwen3.5 chain-of-thought reasoning mode. Off by default for speed.

.PARAMETER ToolsDir
    Directory where llama-server.exe lives (or will be downloaded to).

.PARAMETER ModelsDir
    Directory where GGUF model files live (or will be downloaded to).
#>
param(
    [int]$Port         = 8001,
    [string]$ModelRepo = "bartowski/Qwen_Qwen3.5-9B-GGUF",
    [string]$ModelFile = "Qwen_Qwen3.5-9B-Q4_K_M.gguf",
    [string]$MmprojFile= "mmproj-Qwen_Qwen3.5-9B-f16.gguf",
    [int]$GpuLayers    = 999,
    [int]$ContextSize  = 8192,
    [string]$ApiKey    = "",
    [switch]$NoAuth,
    [switch]$EnableThinking,
    [string]$ToolsDir  = "F:\repos\chum\local-llm\llama.cpp",
    [string]$ModelsDir = "F:\repos\chum\local-llm\models"
)

$ErrorActionPreference = "Stop"

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

# 2. Ensure the main model GGUF is present
$modelPath = Join-Path $ModelsDir $ModelFile
if (-not (Test-Path $modelPath)) {
    Write-Host "Model not found, downloading $ModelFile (~5.8 GB)..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    curl.exe -L -C - -o $modelPath "https://huggingface.co/$ModelRepo/resolve/main/$ModelFile"
    Write-Host "Model downloaded to $modelPath" -ForegroundColor Green
}

# 3. Ensure the vision projector (mmproj) is present
$mmprojPath = Join-Path $ModelsDir $MmprojFile
if (-not (Test-Path $mmprojPath)) {
    Write-Host "Vision projector not found, downloading $MmprojFile (~880 MB)..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    curl.exe -L -C - -o $mmprojPath "https://huggingface.co/$ModelRepo/resolve/main/$MmprojFile"
    Write-Host "mmproj downloaded to $mmprojPath" -ForegroundColor Green
}

# 4. Banner
$thinkingLabel = if ($EnableThinking) { "ON" } else { "OFF (fast mode)" }
$keyLabel      = if ($NoAuth) { "(none - auth disabled)" } else { if ($ApiKey) { $ApiKey } else { "chum-llm-key-2026 (default)" } }

Write-Host ""
Write-Host "=== Chum LLM + Vision API ===" -ForegroundColor Cyan
Write-Host "Base URL : http://127.0.0.1:$Port/v1"
Write-Host "API Key  : $keyLabel"
Write-Host "Model    : $ModelFile"
Write-Host "Vision   : ON (mmproj included)"
Write-Host "Thinking : $thinkingLabel"
Write-Host "==============================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting server..." -ForegroundColor Yellow
Write-Host ""

# 5. Launch via start-llm.py - Python subprocess handles Windows argument quoting
#    correctly for the --chat-template-kwargs JSON string that PS 5.1 mangles.
$launchScript = "F:\repos\chum\local-llm\start-llm.py"

$pyArgs = @("--port", $Port)
if ($NoAuth)         { $pyArgs += "--no-auth" }
elseif ($ApiKey)     { $pyArgs += @("--api-key", $ApiKey) }
if ($EnableThinking) { $pyArgs += "--thinking" }

python $launchScript @pyArgs
