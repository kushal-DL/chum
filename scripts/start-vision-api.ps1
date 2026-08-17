<#
.SYNOPSIS
    Downloads (if needed) and starts a local, OpenAI-compatible multimodal API
    for Chum, backed by Qwen3.5-9B on your AMD GPU via llama.cpp Vulkan.

.DESCRIPTION
    Qwen3.5-9B is a dense multimodal model supporting text + image + video input.
    It exposes the same OpenAI-compatible endpoints as the main LLM server:
      POST /v1/chat/completions   - text and/or image input
      GET  /v1/models

    For image input, include an image_url content part in your message:
      { "role": "user", "content": [
          { "type": "image_url", "image_url": { "url": "data:image/png;base64,..." } },
          { "type": "text",      "text": "What do you see?" }
      ]}

    Default port is 8002 (8000 = Whisper STT, 8001 = Qwen3-8B text LLM).

    Thinking mode (chain-of-thought) is OFF by default for speed. Use
    -EnableThinking to turn it on for complex reasoning tasks.

    Measured performance on RX 6800 XT (llama.cpp b10444, Vulkan, -np 1):
      Text (256 tok input):   TTFT=144ms  PP=2091 t/s  TG=41.7 t/s  total=4.9s
      Image (1080p + text):   TTFT=188ms  PP=13021 t/s TG=37.2 t/s  total=5.5s
      VRAM at -np 1, 4096 ctx: 6.0 GB

    IMPORTANT: run with -np 1 (single parallel slot). The default of 4 slots
    pre-allocates 4x the KV/state cache, pushing VRAM to ~11.7 GB and causing
    severe memory pressure that degrades TG from ~40 t/s down to ~6 t/s.
    This script already sets -np 1 by default.

.PARAMETER Port
    Port to listen on. Default 8002.

.PARAMETER ModelRepo / ModelFile
    HuggingFace repo and GGUF filename. Defaults to bartowski's Qwen3.5-9B Q4_K_M.

.PARAMETER MmprojFile
    Filename of the vision projection weights in the same repo.
    Defaults to the f16 mmproj from bartowski.
    Pass -NoVision to skip mmproj download and run text-only mode.

.PARAMETER NoVision
    Skip mmproj download and start in text-only mode.

.PARAMETER GpuLayers
    Layers to offload to GPU. Default 999 (all).

.PARAMETER ContextSize
    Context window in tokens. Default 8192.

.PARAMETER ApiKey / NoAuth
    Same pattern as the other start-*.ps1 scripts.

.PARAMETER EnableThinking
    Enable Qwen3.5 chain-of-thought reasoning mode. Off by default.
#>
param(
    [int]$Port = 8002,
    [string]$ModelRepo = "bartowski/Qwen_Qwen3.5-9B-GGUF",
    [string]$ModelFile = "Qwen_Qwen3.5-9B-Q4_K_M.gguf",
    [string]$MmprojFile = "mmproj-Qwen_Qwen3.5-9B-f16.gguf",
    [switch]$NoVision,
    [int]$GpuLayers = 999,
    [int]$ContextSize = 8192,
    [string]$ApiKey = "",
    [switch]$NoAuth,
    [switch]$EnableThinking,
    [string]$ToolsDir = "F:\repos\chum\local-llm\llama.cpp",
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
    Write-Host "Model not found, downloading $ModelFile from $ModelRepo (~6.2 GB)..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    $url = "https://huggingface.co/$ModelRepo/resolve/main/$ModelFile"
    curl.exe -L -C - -o $modelPath $url
    Write-Host "Model downloaded to $modelPath" -ForegroundColor Green
}

# 3. Ensure mmproj (vision projector) is present, unless -NoVision
$mmprojPath = $null
if (-not $NoVision) {
    $mmprojPath = Join-Path $ModelsDir $MmprojFile
    if (-not (Test-Path $mmprojPath)) {
        Write-Host "Vision projector not found, downloading $MmprojFile..." -ForegroundColor Yellow
        $mmUrl = "https://huggingface.co/$ModelRepo/resolve/main/$MmprojFile"
        curl.exe -L -C - -o $mmprojPath $mmUrl
        Write-Host "mmproj downloaded to $mmprojPath" -ForegroundColor Green
    }
}

# 4. API key setup
if (-not $NoAuth -and [string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = -join ((48..57) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

$thinkingMode = if ($EnableThinking) { "ON  (chain-of-thought reasoning active)" } else { "OFF (fast non-thinking mode)" }
$visionMode   = if ($mmprojPath) { "ON  (text + image input supported)" } else { "OFF (text-only, run without -NoVision to enable)" }

Write-Host ""
Write-Host "=== Chum Vision API ===" -ForegroundColor Magenta
Write-Host "Model    : $ModelFile"
Write-Host "Base URL : http://127.0.0.1:$Port/v1"
if ($NoAuth) {
    Write-Host "API Key  : (none - auth disabled)"
} else {
    Write-Host "API Key  : $ApiKey"
}
Write-Host "Thinking : $thinkingMode"
Write-Host "Vision   : $visionMode"
Write-Host "=======================" -ForegroundColor Magenta
Write-Host ""
Write-Host "Starting server..." -ForegroundColor Yellow
Write-Host ""

$serverArgs = @(
    "-m", $modelPath,
    "--host", "127.0.0.1",
    "--port", $Port,
    "-ngl", $GpuLayers,
    "-c", $ContextSize
)
if ($mmprojPath) { $serverArgs += @("--mmproj", $mmprojPath) }
if (-not $EnableThinking) { $serverArgs += @("--reasoning", "off") }
if (-not $NoAuth) { $serverArgs += @("--api-key", $ApiKey) }

# Strip ANSI escape sequences - PowerShell 5.1 doesn't render them
& $serverExe @serverArgs | ForEach-Object {
    [System.Text.RegularExpressions.Regex]::Replace($_, "\x1b\[[0-9;]*[A-Za-z]", "")
}
