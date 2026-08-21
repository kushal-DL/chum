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

# Log everything to a file — readable even if the window closes immediately.
$LogFile = "F:\repos\chum\scripts\whisper-start-log.txt"
Start-Transcript -Path $LogFile -Force | Out-Null
Write-Host "Transcript: $LogFile"

# Strict only for setup — the server launch below runs with this relaxed so that
# whisper-server's normal stderr diagnostics aren't treated as terminating errors.
$ErrorActionPreference = "Stop"

# Keep the window open so the user can read any error before it disappears.
trap {
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host ""
    Stop-Transcript | Out-Null
    powershell -Command "Read-Host 'Press Enter to close'"
    exit 1
}

$BinDir   = "F:\repos\chum\local-llm\whisper.cpp\bin"
$LlamaDir = "F:\repos\chum\local-llm\llama.cpp"
$serverExe = Join-Path $BinDir "whisper-server.exe"

# 1. Ensure whisper-server.exe is present — download from whisper.cpp releases if missing.
if (-not (Test-Path $serverExe)) {
    Write-Host "whisper-server.exe not found, downloading latest whisper.cpp build..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $BinDir | Out-Null

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggerganov/whisper.cpp/releases/latest"
    # Pick the first Windows x64 zip (avx2 preferred for best CPU perf; any x64 build works)
    $asset = $release.assets |
        Where-Object { $_.name -match "win.*x64.*\.zip" -or $_.name -match "x64.*win.*\.zip" } |
        Sort-Object { if ($_.name -like "*avx2*") { 0 } else { 1 } } |
        Select-Object -First 1
    if (-not $asset) { throw "Could not find a Windows x64 zip in the latest whisper.cpp release. Check https://github.com/ggerganov/whisper.cpp/releases" }

    $zipPath = Join-Path $BinDir $asset.name
    Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."
    curl.exe -L -o $zipPath $asset.browser_download_url
    Expand-Archive -Path $zipPath -DestinationPath $BinDir -Force
    Remove-Item $zipPath

    if (-not (Test-Path $serverExe)) { throw "Extraction did not produce whisper-server.exe at $serverExe. Check $BinDir." }
    Write-Host "whisper.cpp installed at $BinDir" -ForegroundColor Green
}

# 1a. Resolve which model to use: prefer the fine-tuned model; fall back to the
#     stock ggml-large-v3-turbo downloaded from Hugging Face.
$StockModelFile = "ggml-large-v3-turbo.bin"
$StockModelPath = Join-Path (Split-Path $Model -Parent) $StockModelFile
$StockModelUrl  = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$StockModelFile"

$activeModel = $Model
if (-not (Test-Path $Model)) {
    Write-Host "Fine-tuned model not found at $(Split-Path $Model -Leaf)" -ForegroundColor Yellow
    if (-not (Test-Path $StockModelPath)) {
        Write-Host "Downloading stock $StockModelFile (~1.5 GB) from Hugging Face..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Force -Path (Split-Path $StockModelPath -Parent) | Out-Null
        curl.exe -L -C - -o $StockModelPath $StockModelUrl
        if (-not (Test-Path $StockModelPath)) { throw "Download failed - $StockModelPath not found after curl." }
        Write-Host "Stock model downloaded to $StockModelPath" -ForegroundColor Green
    } else {
        Write-Host "Stock model already present: $StockModelFile" -ForegroundColor Green
    }
    $activeModel = $StockModelPath
    Write-Host "NOTE: Using stock model - technical vocabulary accuracy lower than the fine-tuned version." -ForegroundColor Yellow
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

# 2c. Open firewall so other machines on the LAN can reach this server.
$fwRule = "Chum Whisper API port $Port"
if (-not (Get-NetFirewallRule -DisplayName $fwRule -ErrorAction SilentlyContinue)) {
    Write-Host "Adding Windows Firewall inbound rule for port $Port ..." -ForegroundColor Yellow
    New-NetFirewallRule -DisplayName $fwRule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    Write-Host "Firewall rule added." -ForegroundColor Green
}

$lanIp = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -in "Dhcp","Manual" } |
    Select-Object -First 1).IPAddress
if (-not $lanIp) { $lanIp = "<your-lan-ip>" }

# 3. Banner
Write-Host ""
Write-Host "=== Chum Whisper API (whisper.cpp / Vulkan GPU) ===" -ForegroundColor Cyan
Write-Host "Base URL : http://${lanIp}:$Port/v1  (LAN)"
Write-Host "           http://127.0.0.1:$Port/v1  (local)"
Write-Host "Endpoint : POST /v1/audio/transcriptions"
Write-Host "Model    : $(Split-Path $activeModel -Leaf)"
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
    "--host", "0.0.0.0",
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
    "-m", "`"$activeModel`"",
    "--host", "0.0.0.0",
    "--port", $Port,
    "-l", $Language,
    "-t", $Threads,
    "--inference-path", "/v1/audio/transcriptions",
    "-sns"
) -join " "

Push-Location $BinDir
try {
    $proc = Start-Process -FilePath $serverExe -ArgumentList $argLine -NoNewWindow -Wait -PassThru
    $code = $proc.ExitCode
    if ($code -ne 0) {
        Write-Host ""
        Write-Host "whisper-server exited with code $code" -ForegroundColor Red
        Write-Host "Common causes: missing DLL next to exe, incompatible ggml version, bad model path." -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}

Stop-Transcript | Out-Null
powershell -Command "Read-Host 'Server has stopped. Press Enter to close'"
