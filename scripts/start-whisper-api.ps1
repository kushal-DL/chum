<#
.SYNOPSIS
    Starts the local Whisper STT API for Chum on the GPU (Vulkan).

.DESCRIPTION
    Runs whisper.cpp's whisper-server.exe with the fine-tuned tech model in ggml
    format, GPU-accelerated via Vulkan on your AMD Radeon RX 6800 XT.

    WHY whisper.cpp instead of the old Python/transformers server:
      The fine-tuned HuggingFace model could only be served via transformers'
      `model.generate()`, whose per-token autoregressive dispatch on torch-directml
      either hangs or is slower than CPU on this AMD card. whisper.cpp runs the
      same fine-tuned weights (converted to ggml) through the Vulkan backend -
      the same proven GPU stack the Qwen LLM server uses - at ~2s per 8s clip.

    NO BUILD TOOLCHAIN NEEDED:
      whisper.cpp ships no prebuilt Vulkan Windows binary, but its ggml backend
      loads dynamically. We use the prebuilt whisper-server.exe + whisper.dll and
      drop in the Vulkan-enabled ggml DLLs already present from the llama.cpp
      build (local-llm\llama.cpp). This script re-syncs those DLLs on each start.

    Endpoint served:
        POST http://127.0.0.1:<Port>/v1/audio/transcriptions
        (OpenAI-compatible: multipart 'file' = 16kHz mono WAV, returns {"text": ...})

    Point Chum's STT settings at:
        Base URL : http://127.0.0.1:<Port>/v1
        Model    : whisper-large-v3-turbo   (label only; server uses the loaded ggml)
        API Key  : (leave blank - server binds to 127.0.0.1, no auth)

.PARAMETER Port
    Port to listen on. Default 8000.

.PARAMETER Model
    Path to the ggml Whisper model. Default: the fine-tuned tech model.

.PARAMETER Threads
    CPU threads for the parts not on GPU (mel, sampling). Default 4.

.PARAMETER Language
    Spoken language. Default 'en'. Use 'auto' to auto-detect.

.PARAMETER ApiKey / NoAuth
    Accepted for signature compatibility with the other start-*.ps1 scripts.
    whisper-server does not enforce auth; it binds to localhost only.
#>
param(
    [int]$Port      = 8000,
    [string]$Model  = "F:\repos\chum\local-llm\models\ggml-whisper-large-v3-turbo-tech-f16.bin",
    [int]$Threads   = 4,
    [string]$Language = "en",
    [string]$ApiKey = "",
    [switch]$NoAuth
)

# Strict only for setup — the server launch below runs with this relaxed so that
# whisper-server's normal stderr diagnostics aren't treated as terminating errors.
$ErrorActionPreference = "Stop"

$BinDir   = "F:\repos\chum\local-llm\whisper.cpp\bin"
$LlamaDir = "F:\repos\chum\local-llm\llama.cpp"
$serverExe = Join-Path $BinDir "whisper-server.exe"

# 1. Sanity checks
if (-not (Test-Path $serverExe)) {
    throw "whisper-server.exe not found at $serverExe. Re-run the whisper.cpp setup (download whisper-bin-x64.zip and extract whisper-server.exe + whisper.dll into $BinDir)."
}
if (-not (Test-Path $Model)) {
    throw "ggml model not found at $Model. Convert the fine-tuned HF model with convert-h5-to-ggml.py first."
}

# 1b. If a previous whisper-server still holds the port, stop it so we can bind.
$existing = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($existing) {
    $existing.OwningProcess | Select-Object -Unique | ForEach-Object {
        $proc = Get-Process -Id $_ -ErrorAction SilentlyContinue
        if ($proc -and $proc.Name -eq "whisper-server") {
            Write-Host "Port $Port is held by an existing whisper-server (PID $($proc.Id)) - stopping it..." -ForegroundColor Yellow
            Stop-Process -Id $proc.Id -Force
            Start-Sleep -Milliseconds 500
        }
        elseif ($proc) {
            throw "Port $Port is already in use by $($proc.Name) (PID $($proc.Id)). Free it or pass -Port <other>."
        }
    }
}

# 2. Re-sync the Vulkan-enabled ggml DLLs from the llama.cpp build so whisper-server
#    uses the GPU backend. These share the same ggml ABI as whisper.dll here.
$ggmlDlls = @("ggml.dll", "ggml-base.dll", "ggml-vulkan.dll", "libomp140.x86_64.dll")
foreach ($dll in $ggmlDlls) {
    $src = Join-Path $LlamaDir $dll
    if (Test-Path $src) { Copy-Item $src (Join-Path $BinDir $dll) -Force }
}
# CPU backend variants (ggml picks the best for your CPU at runtime)
Get-ChildItem (Join-Path $LlamaDir "ggml-cpu-*.dll") -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $BinDir $_.Name) -Force }

# 3. Banner
Write-Host ""
Write-Host "=== Chum Whisper API (whisper.cpp / Vulkan GPU) ===" -ForegroundColor Cyan
Write-Host "Base URL : http://127.0.0.1:$Port/v1"
Write-Host "Endpoint : POST /v1/audio/transcriptions"
Write-Host "Model    : $(Split-Path $Model -Leaf)"
Write-Host "Device   : GPU (Vulkan - AMD RX 6800 XT)"
Write-Host "API Key  : (none - localhost only)"
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting whisper-server..." -ForegroundColor Yellow
Write-Host ""

# 4. Launch. Working dir = BinDir so the Vulkan ggml DLLs load next to the exe.
#    --inference-path matches what Chum POSTs to (baseUrl '/v1' + '/audio/transcriptions').
#    -sns suppresses non-speech tokens (helps with fan/HVAC noise); temperature fallback
#    and entropy/logprob thresholds (on by default) catch repetition loops.
$serverArgs = @(
    "-m", $Model,
    "--host", "127.0.0.1",
    "--port", $Port,
    "-l", $Language,
    "-t", $Threads,
    "--inference-path", "/v1/audio/transcriptions",
    "-sns"
)

# Launch via Start-Process -NoNewWindow -Wait. whisper-server writes its diagnostics
# (Vulkan device, load progress, request logs) to stderr — that is NORMAL output, not an
# error. Calling the exe directly with `&` makes PowerShell wrap those stderr lines as
# terminating NativeCommandErrors (which was killing the launch). Start-Process attaches
# the child's stdout/stderr straight to this console, so nothing is wrapped or treated
# as an error, and -Wait keeps the script blocked while the server runs.
$ErrorActionPreference = "Continue"

# Quote the model path in case it ever contains spaces.
$argLine = @(
    "-m", "`"$Model`"",
    "--host", "127.0.0.1",
    "--port", $Port,
    "-l", $Language,
    "-t", $Threads,
    "--inference-path", "/v1/audio/transcriptions",
    "-sns"
) -join " "

Push-Location $BinDir
try {
    Start-Process -FilePath $serverExe -ArgumentList $argLine -NoNewWindow -Wait
}
finally {
    Pop-Location
}
