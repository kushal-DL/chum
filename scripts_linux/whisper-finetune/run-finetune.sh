#!/usr/bin/env bash
# ==============================================================================
# run-finetune.sh — Fine-tunes whisper-large-v3-turbo on technical vocabulary
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Source common helpers if present
if [[ -f "$SCRIPT_DIR/../common.sh" ]]; then
    source "$SCRIPT_DIR/../common.sh"
fi

DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Fine-tunes whisper-large-v3-turbo on technical jargon and saves the merged model.

Options:
  --dry-run      Check dependencies and configuration without running training
  -h, --help     Show this help message and exit
EOF
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            printf "Unknown option: %s\n" "$1" >&2
            usage ;;
    esac
done

printf "\n\033[0;36m╔══════════════════════════════════════════════════════════╗\033[0m\n"
printf "\033[0;36m║   Whisper large-v3-turbo  ×  Technical Domain Fine-Tune ║\033[0m\n"
printf "\033[0;36m╚══════════════════════════════════════════════════════════╝\033[0m\n\n"

# 1. Dependency check
printf "\033[0;33mStep 1/4: Checking Python dependencies...\033[0m\n"

PY_CMD="python3"
if ! command -v "$PY_CMD" >/dev/null 2>&1; then
    printf "\033[0;31mERROR: python3 not found on PATH\033[0m\n" >&2
    exit 1
fi

MISSING=()
DEPS=("peft" "accelerate" "soundfile" "edge_tts" "torch")
for pkg in "${DEPS[@]}"; do
    if ! "$PY_CMD" -c "import ${pkg//-/_}" >/dev/null 2>&1; then
        MISSING+=("$pkg")
    fi
done

if [[ ${#MISSING[@]} -gt 0 ]]; then
    printf "  Missing packages: %s\n" "${MISSING[*]}"
    if [[ $DRY_RUN -eq 0 ]]; then
        printf "  Installing missing packages via pip...\n"
        "$PY_CMD" -m pip install "${MISSING[@]}" -q 2>/dev/null || {
            if command -v uv >/dev/null 2>&1; then
                uv pip install "${MISSING[@]}" -q || true
            fi
        }
    fi
else
    printf "  \033[0;32mAll Python dependencies present.\033[0m\n"
fi

if ! command -v ffmpeg >/dev/null 2>&1; then
    printf "  \033[0;33mWARNING: ffmpeg not found on PATH. Install via: sudo apt install ffmpeg\033[0m\n" >&2
    if [[ $DRY_RUN -eq 0 ]]; then
        exit 1
    fi
else
    printf "  \033[0;32mffmpeg OK (%s)\033[0m\n" "$(command -v ffmpeg)"
fi

if [[ $DRY_RUN -eq 1 ]]; then
    printf "\n\033[0;32m[dry-run] Dependency check completed.\033[0m\n"
    exit 0
fi

# 2. Generate training data
METADATA_FILE="$SCRIPT_DIR/training_data/metadata.jsonl"
if [[ ! -f "$METADATA_FILE" && -f "$REPO_ROOT/scripts/whisper-finetune/training_data/metadata.jsonl" ]]; then
    METADATA_FILE="$REPO_ROOT/scripts/whisper-finetune/training_data/metadata.jsonl"
fi

DATA_GEN_SCRIPT="$SCRIPT_DIR/gen_training_data.py"
if [[ ! -f "$DATA_GEN_SCRIPT" && -f "$REPO_ROOT/scripts/whisper-finetune/gen_training_data.py" ]]; then
    DATA_GEN_SCRIPT="$REPO_ROOT/scripts/whisper-finetune/gen_training_data.py"
fi

printf "\n\033[0;33mStep 2/4: Generating TTS training data...\033[0m\n"
if [[ -f "$METADATA_FILE" ]]; then
    LINE_COUNT=$(wc -l < "$METADATA_FILE" 2>/dev/null || echo "0")
    printf "  Existing metadata.jsonl found (%s samples). Skipping generation.\n" "$LINE_COUNT"
    printf "  Delete %s/training_data/ to regenerate from scratch.\n" "$(dirname "$METADATA_FILE")"
else
    if [[ -f "$DATA_GEN_SCRIPT" ]]; then
        printf "  Generating audio samples...\n"
        "$PY_CMD" "$DATA_GEN_SCRIPT"
    else
        printf "  \033[0;31mERROR: gen_training_data.py not found at %s\033[0m\n" "$DATA_GEN_SCRIPT" >&2
        exit 1
    fi
fi

# 3. Fine-tune with LoRA
FINETUNE_SCRIPT="$SCRIPT_DIR/finetune_whisper.py"
if [[ ! -f "$FINETUNE_SCRIPT" && -f "$REPO_ROOT/scripts/whisper-finetune/finetune_whisper.py" ]]; then
    FINETUNE_SCRIPT="$REPO_ROOT/scripts/whisper-finetune/finetune_whisper.py"
fi
MODEL_OUT="$REPO_ROOT/models/whisper-large-v3-turbo-tech"

printf "\n\033[0;33mStep 3/4: Fine-tuning with LoRA...\033[0m\n"
printf "  Output will be saved to: %s\n" "$MODEL_OUT"

if [[ -f "$FINETUNE_SCRIPT" ]]; then
    "$PY_CMD" "$FINETUNE_SCRIPT"
else
    printf "  \033[0;31mERROR: finetune_whisper.py not found at %s\033[0m\n" "$FINETUNE_SCRIPT" >&2
    exit 1
fi

# 4. Summary
printf "\n\033[0;33mStep 4/4: Summary\033[0m\n"
if [[ -f "$MODEL_OUT/config.json" ]]; then
    printf "\n  \033[0;32m✓ Model saved: %s\033[0m\n" "$MODEL_OUT"
fi
