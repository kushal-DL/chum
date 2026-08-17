<#
.SYNOPSIS
    Fine-tunes whisper-large-v3-turbo on technical vocabulary and saves the
    merged model to models/whisper-large-v3-turbo-tech/.

.DESCRIPTION
    Steps:
      1. Install missing Python dependencies (peft, accelerate)
      2. Generate ~6000 TTS audio training samples (edge-tts, 5 voices)
      3. Fine-tune with LoRA on CPU (~30-90 min depending on hardware)
      4. Merge LoRA adapters into base model and save
      5. Print summary + instructions for swapping into whisper_api_server.py

.NOTES
    Requires: Python 3.12+, torch, transformers, edge-tts, ffmpeg on PATH
    Produces: models\whisper-large-v3-turbo-tech\  (HuggingFace format)
#>

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$RepoRoot  = (Get-Item $ScriptDir).Parent.Parent.FullName

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Whisper large-v3-turbo  ×  Technical Domain Fine-Tune ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── 1. Dependency check ───────────────────────────────────────────────────────
Write-Host "Step 1/4: Checking Python dependencies..." -ForegroundColor Yellow

$missing = @()
$deps    = @("peft", "accelerate", "soundfile", "edge_tts", "torch_directml")
foreach ($pkg in $deps) {
    $check = & python -c "import $($pkg.Replace('-','_'))" 2>&1
    if ($LASTEXITCODE -ne 0) { $missing += $pkg }
}

if ($missing.Count -gt 0) {
    Write-Host "  Installing: $($missing -join ', ')"
    pip install @missing -q
    if ($LASTEXITCODE -ne 0) { Write-Error "pip install failed"; exit 1 }
} else {
    Write-Host "  All dependencies present." -ForegroundColor Green
}

# Check ffmpeg
$ff = Get-Command ffmpeg -ErrorAction SilentlyContinue
if (-not $ff) {
    Write-Error "ffmpeg not found on PATH. Install it from https://ffmpeg.org/download.html"
    exit 1
}
Write-Host "  ffmpeg OK ($($ff.Source))" -ForegroundColor Green

# ── 2. Generate training data ─────────────────────────────────────────────────
$MetadataFile = Join-Path $ScriptDir "training_data\metadata.jsonl"
$DataGenScript = Join-Path $ScriptDir "gen_training_data.py"

Write-Host ""
Write-Host "Step 2/4: Generating TTS training data..." -ForegroundColor Yellow

if (Test-Path $MetadataFile) {
    $lineCount = (Get-Content $MetadataFile | Measure-Object -Line).Lines
    Write-Host "  Existing metadata.jsonl found ($lineCount samples). Skipping generation."
    Write-Host "  Delete training_data\ to regenerate from scratch." -ForegroundColor Gray
} else {
    Write-Host "  Generating audio samples (~10-20 min, 5 voices × 1200 sentences)..."
    $t0 = Get-Date
    python $DataGenScript
    if ($LASTEXITCODE -ne 0) { Write-Error "Data generation failed"; exit 1 }
    $elapsed = ((Get-Date) - $t0).TotalMinutes
    Write-Host "  Data generation completed in $([Math]::Round($elapsed,1)) min." -ForegroundColor Green
}

$lineCount = (Get-Content $MetadataFile | Measure-Object -Line).Lines
Write-Host "  Training samples: $lineCount" -ForegroundColor Green

# ── 3. Fine-tune ──────────────────────────────────────────────────────────────
$FinetuneScript = Join-Path $ScriptDir "finetune_whisper.py"
$ModelOut       = Join-Path $RepoRoot "models\whisper-large-v3-turbo-tech"

Write-Host ""
Write-Host "Step 3/4: Fine-tuning with LoRA (CPU, ~30-90 min)..." -ForegroundColor Yellow
Write-Host "  Output will be saved to: $ModelOut"
Write-Host "  Progress is printed every 20 optimizer steps."
Write-Host ""

$t0 = Get-Date
python $FinetuneScript
if ($LASTEXITCODE -ne 0) { Write-Error "Fine-tuning failed"; exit 1 }
$elapsed = ((Get-Date) - $t0).TotalMinutes

# ── 4. Summary ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Step 4/4: Summary" -ForegroundColor Yellow

if (Test-Path (Join-Path $ModelOut "config.json")) {
    $modelSize = (Get-ChildItem $ModelOut -Recurse | Measure-Object -Property Length -Sum).Sum / 1GB
    Write-Host ""
    Write-Host "  ✓ Model saved: $ModelOut" -ForegroundColor Green
    Write-Host "  ✓ Size: $([Math]::Round($modelSize, 2)) GB" -ForegroundColor Green
    Write-Host "  ✓ Training time: $([Math]::Round($elapsed, 1)) min" -ForegroundColor Green
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  To use the fine-tuned model in whisper_api_server.py:  ║" -ForegroundColor Cyan
    Write-Host "║                                                          ║" -ForegroundColor Cyan
    Write-Host "║  Change MODEL_ID at the top of whisper_api_server.py to ║" -ForegroundColor Cyan
    Write-Host "║  the absolute path of models\whisper-large-v3-turbo-tech ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
} else {
    Write-Warning "Model output directory missing or incomplete. Check logs above."
}
