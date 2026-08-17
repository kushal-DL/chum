<#
.SYNOPSIS
    Downloads (if needed) and starts a local, OpenAI-compatible chat-completions
    API for Chum, backed by llama.cpp's Vulkan build running a quantized LLM
    on your AMD GPU.

.DESCRIPTION
    - Downloads the latest llama.cpp Windows Vulkan release (llama-server.exe)
      from GitHub if not already present.
    - Downloads the chosen GGUF model from Hugging Face if not already present.
    - Launches llama-server, which natively exposes POST /v1/chat/completions,
      /v1/completions, and /v1/models matching the OpenAI API - no wrapper
      needed (unlike the Whisper server, whose /v1/audio/transcriptions isn't
      natively served by llama.cpp).

    Default model: Qwen3-8B (Q4_K_M, ~5.0 GB). Chosen over Qwen2.5-7B because
    it scores significantly higher on instruction following (+17%), technical
    knowledge (MMLU-Pro +26%), and live coding tasks (+58%), while still
    delivering ~44-47 tok/s on an RX 6800 XT via Vulkan - well above the 40
    tok/s real-time threshold. Thinking mode is disabled by default (non-thinking
    mode adds only a 4-token <think></think> stub, negligible overhead).

.PARAMETER Port
    Port to listen on. Default 8001 (8000 is used by the Whisper API).

.PARAMETER ModelRepo / ModelFile
    Hugging Face repo + exact GGUF filename to download. Override these to
    swap models, e.g.:
      -ModelRepo "bartowski/Phi-3.5-mini-instruct-GGUF" -ModelFile "Phi-3.5-mini-instruct-Q4_K_M.gguf"   (3.8B, fastest/smallest)
      -ModelRepo "bartowski/gemma-2-9b-it-GGUF"          -ModelFile "gemma-2-9b-it-Q4_K_M.gguf"           (9B, alternative all-rounder)

.PARAMETER GpuLayers
    Number of layers to offload to GPU. Default 999 (= all layers; llama.cpp
    clamps to the model's actual layer count).

.PARAMETER ContextSize
    Context window in tokens. Default 8192.

.PARAMETER ApiKey / NoAuth
    Same pattern as start-whisper-api.ps1: fixed/random key, or -NoAuth to
    disable auth entirely (fine for localhost-only binding).

.PARAMETER EnableThinking
    Pass this switch to enable Qwen3's chain-of-thought reasoning mode.
    Off by default - adds latency but improves complex reasoning.
#>
param(
    [int]$Port = 8001,
    [string]$ModelRepo = "bartowski/Qwen_Qwen3.5-9B-GGUF",
    [string]$ModelFile = "Qwen_Qwen3.5-9B-Q4_K_M.gguf",
    [int]$GpuLayers = 999,
    [int]$ContextSize = 8192,
    [string]$ApiKey = "",
    [switch]$NoAuth,
    [switch]$EnableThinking,
    [string]$ToolsDir = "F:\repos\chum\local-llm\llama.cpp",
    [string]$ModelsDir = "F:\repos\chum\local-llm\models"
)

$ErrorActionPreference = "Stop"

# 1. Ensure llama-server.exe is present (download latest Vulkan build if not)
$serverExe = Join-Path $ToolsDir "llama-server.exe"
if (-not (Test-Path $serverExe)) {
    Write-Host "llama-server.exe not found, downloading latest Vulkan build..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -like "*bin-win-vulkan-x64.zip" } | Select-Object -First 1
    if (-not $asset) {
        throw "Could not find a win-vulkan-x64 asset in the latest llama.cpp release."
    }

    $zipPath = Join-Path $ToolsDir $asset.name
    Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."
    curl.exe -L -o $zipPath $asset.browser_download_url
    Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
    Remove-Item $zipPath

    if (-not (Test-Path $serverExe)) {
        throw "Extraction did not produce llama-server.exe - check $ToolsDir contents."
    }
    Write-Host "llama.cpp installed at $ToolsDir" -ForegroundColor Green
}

# 2. Ensure the GGUF model is present (download from Hugging Face if not)
$modelPath = Join-Path $ModelsDir $ModelFile
if (-not (Test-Path $modelPath)) {
    Write-Host "Model not found, downloading $ModelFile from $ModelRepo..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    $url = "https://huggingface.co/$ModelRepo/resolve/main/$ModelFile"
    # -C - resumes a partial download if this was interrupted last time
    curl.exe -L -C - -o $modelPath $url
    Write-Host "Model downloaded to $modelPath" -ForegroundColor Green
}

# 3. API key setup (same pattern as start-whisper-api.ps1)
if (-not $NoAuth -and [string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = -join ((48..57) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

$thinkingMode = if ($EnableThinking) { "ON  (chain-of-thought reasoning active)" } else { "OFF (fast non-thinking mode)" }

Write-Host ""
Write-Host "=== Chum Local LLM API ===" -ForegroundColor Cyan
Write-Host "Base URL : http://127.0.0.1:$Port/v1"
if ($NoAuth) {
    Write-Host "API Key  : (none - auth disabled)"
} else {
    Write-Host "API Key  : $ApiKey"
}
Write-Host "Model    : $ModelFile"
Write-Host "Thinking : $thinkingMode"
Write-Host "==========================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Enter these into Chum's LLM provider settings. Starting server..." -ForegroundColor Yellow
Write-Host ""

$serverArgs = @(
    "-m", $modelPath,
    "--host", "127.0.0.1",
    "--port", $Port,
    "-ngl", $GpuLayers,
    "-c", $ContextSize,
    "--jinja"
)
if (-not $EnableThinking) {
    $serverArgs += @("--chat-template-kwargs", '{"enable_thinking":false}')
}
if (-not $NoAuth) {
    $serverArgs += @("--api-key", $ApiKey)
}

# llama-server emits ANSI color codes; PowerShell 5.1 doesn't render them.
# Strip escape sequences so the output is readable.
& $serverExe @serverArgs | ForEach-Object {
    [System.Text.RegularExpressions.Regex]::Replace($_, "\x1b\[[0-9;]*[A-Za-z]", "")
}
