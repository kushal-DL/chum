#!/usr/bin/env bash
# ==============================================================================
# start-whisper-api.sh — Starts the local Whisper STT API for Chum
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Default parameter values
PORT=8000
MODEL_PATH="$REPO_ROOT/local-llm/models/ggml-whisper-large-v3-turbo-tech-f16.bin"
THREADS=4
LANGUAGE="en"
API_KEY="llm"
GPU_DEVICE=0
if command -v rocm-smi >/dev/null 2>&1; then
    NUM_ROCM_DEVS=$(rocm-smi 2>/dev/null | grep -E "^[0-9]+\s+" | wc -l || true)
    if [[ "$NUM_ROCM_DEVS" -gt 1 ]]; then
        GPU_DEVICE=1
    fi
fi
NO_AUTH=0
BIN_DIR="$REPO_ROOT/local-llm/whisper.cpp/bin"
LLAMA_DIR="$REPO_ROOT/local-llm/llama.cpp"
DRY_RUN=0
LOG_FILE="$SCRIPT_DIR/whisper-start-log.txt"

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Starts the local Whisper STT API for Chum on port $PORT (whisper.cpp).

Options:
  -p, --port <int>        Port to listen on (default: $PORT)
  -m, --model <path>      Path to ggml Whisper model (default: $MODEL_PATH)
  -t, --threads <int>     CPU threads for processing (default: $THREADS)
  -l, --language <lang>   Spoken language (default: $LANGUAGE, or 'auto')
  -g, --device <id>       GPU device ID (default: $GPU_DEVICE)
  -k, --api-key <key>     API key (default: llm)
  --no-auth               Disable authentication
  --bin-dir <dir>         whisper.cpp bin directory (default: $BIN_DIR)
  --dry-run               Validate configuration without starting server
  -h, --help              Show this help message and exit
EOF
    exit 0
}

# Parse CLI arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)
            PORT="$2"; shift 2 ;;
        -m|--model)
            MODEL_PATH="$2"; shift 2 ;;
        -t|--threads)
            THREADS="$2"; shift 2 ;;
        -l|--language)
            LANGUAGE="$2"; shift 2 ;;
        -g|--device)
            GPU_DEVICE="$2"; shift 2 ;;
        -k|--api-key)
            API_KEY="$2"; shift 2 ;;
        --no-auth)
            NO_AUTH=1; shift ;;
        --bin-dir)
            BIN_DIR="$2"; shift 2 ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

# Initialize log file and tee all stdout/stderr to it (ensures crashes are always captured)
mkdir -p "$(dirname "$LOG_FILE")"
touch "$LOG_FILE"
exec > >(tee -a "$LOG_FILE") 2>&1
log_info "Logging to: $LOG_FILE"

# Trap errors and log them
trap 'log_err "Whisper API script failed at line $LINENO (exit code $?)"' ERR

# 1. Ensure whisper-server is present
SERVER_EXE="$BIN_DIR/whisper-server"
if [[ ! -x "$SERVER_EXE" && -x "$BIN_DIR/whisper-bin-ubuntu-x64/whisper-server" ]]; then
    SERVER_EXE="$BIN_DIR/whisper-bin-ubuntu-x64/whisper-server"
fi

# Clean out bundled CPU-only ggml libraries that shadow the system's ROCm backend
if [[ -f /usr/lib/x86_64-linux-gnu/ggml/backends0/libggml-hip.so ]]; then
    rm -f "$BIN_DIR"/libggml* 2>/dev/null || true
fi

if [[ ! -x "$SERVER_EXE" ]] && ! command -v whisper-server >/dev/null 2>&1; then
    if [[ $DRY_RUN -eq 1 ]]; then
        log_step "[dry-run] whisper-server check skipped"
    else
        log_warn "whisper-server not found, resolving binary..."
        mkdir -p "$BIN_DIR"
        
        # Check if Ubuntu / Debian package repository has whisper.cpp-tools (supports ROCm / system ggml)
        if command -v apt-get >/dev/null 2>&1 && apt-cache show whisper.cpp-tools >/dev/null 2>&1; then
            log_step "Extracting ROCm/GPU-compatible whisper.cpp package from repository..."
            TMP_DEB_DIR=$(mktemp -d /tmp/whisper_deb_XXXXXX)
            (
                cd "$TMP_DEB_DIR"
                apt-get download whisper.cpp-tools libwhisper1 >/dev/null 2>&1 || true
                for deb in *.deb; do
                    [[ -f "$deb" ]] && dpkg-deb -x "$deb" "$TMP_DEB_DIR"
                done
                if [[ -f "$TMP_DEB_DIR/usr/bin/whisper-server" ]]; then
                    cp "$TMP_DEB_DIR/usr/bin/whisper-server" "$BIN_DIR/"
                    cp "$TMP_DEB_DIR/usr/bin/whisper-cli" "$BIN_DIR/" 2>/dev/null || true
                    cp -a "$TMP_DEB_DIR"/usr/lib/*/libwhisper* "$BIN_DIR/" 2>/dev/null || true
                    chmod +x "$BIN_DIR"/whisper-* 2>/dev/null || true
                fi
            )
            rm -rf "$TMP_DEB_DIR"
        fi

        # Fallback to GitHub release if still not found
        if [[ ! -x "$SERVER_EXE" ]]; then
            log_step "Checking releases from ggml-org/whisper.cpp..."
            DL_URL=""
            RELS_JSON=$(curl -sSL "https://api.github.com/repos/ggml-org/whisper.cpp/releases" 2>/dev/null || true)
            if [[ -n "$RELS_JSON" ]]; then
                DL_URL=$(echo "$RELS_JSON" | grep -oP '"browser_download_url":\s*"\K[^"]*bin-ubuntu-x64\.tar\.gz' | head -n 1 || true)
                if [[ -z "$DL_URL" ]]; then
                    DL_URL=$(echo "$RELS_JSON" | grep -oP '"browser_download_url":\s*"\K[^"]*bin-linux-x64[^"]*\.(tar\.gz|zip)' | head -n 1 || true)
                fi
            fi

            if [[ -n "$DL_URL" ]]; then
                log_step "Downloading $(basename "$DL_URL")..."
                PKG="$BIN_DIR/$(basename "$DL_URL")"
                curl -L -o "$PKG" "$DL_URL"
                if [[ "$PKG" =~ \.zip$ ]]; then
                    unzip -q -o "$PKG" -d "$BIN_DIR"
                else
                    tar -xzf "$PKG" -C "$BIN_DIR" --strip-components=1 2>/dev/null || tar -xzf "$PKG" -C "$BIN_DIR"
                fi
                rm -f "$PKG"
                chmod +x "$BIN_DIR"/whisper-* "$BIN_DIR"/main 2>/dev/null || true
                if [[ -x "$BIN_DIR/whisper-bin-ubuntu-x64/whisper-server" ]]; then
                    SERVER_EXE="$BIN_DIR/whisper-bin-ubuntu-x64/whisper-server"
                fi
                log_ok "whisper.cpp installed at $BIN_DIR"
            else
                log_err "Could not find a prebuilt Linux release for whisper.cpp. Ensure whisper-server is in PATH or $BIN_DIR."
                exit 1
            fi
        fi
    fi
fi

# 2. Resolve model (fine-tuned or stock fallback)
ACTIVE_MODEL="$MODEL_PATH"
if [[ ! -f "$MODEL_PATH" ]]; then
    STOCK_MODEL_FILE="ggml-large-v3-turbo.bin"
    MODELS_DIR="$(dirname "$MODEL_PATH")"
    STOCK_MODEL_PATH="$MODELS_DIR/$STOCK_MODEL_FILE"
    STOCK_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$STOCK_MODEL_FILE"

    log_warn "Fine-tuned model not found at $(basename "$MODEL_PATH")"
    if [[ ! -f "$STOCK_MODEL_PATH" || $(stat -c%s "$STOCK_MODEL_PATH" 2>/dev/null || echo 0) -lt 1500000000 ]]; then
        if [[ $DRY_RUN -eq 1 ]]; then
            log_step "[dry-run] Stock model download check skipped"
        else
            log_step "Downloading/resuming stock $STOCK_MODEL_FILE (~1.5 GB)..."
            mkdir -p "$MODELS_DIR"
            curl -L -C - -o "$STOCK_MODEL_PATH" "$STOCK_URL"
            log_ok "Stock model downloaded to $STOCK_MODEL_PATH"
        fi
    else
        log_ok "Stock model already present: $STOCK_MODEL_FILE"
    fi
    ACTIVE_MODEL="$STOCK_MODEL_PATH"
fi

# 3. Stop previous instance on port
kill_port_process "$PORT" "Whisper STT server" 2

# 4. Banner
LAN_IP=$(get_lan_ip)
DEVICE_DESC="CPU"
if [[ -f /usr/lib/x86_64-linux-gnu/ggml/backends0/libggml-hip.so ]] || command -v rocm-smi >/dev/null 2>&1; then
    GPU_NAME=$(rocminfo 2>/dev/null | grep -m1 "Marketing Name:.*Radeon" | awk -F': +' '{print $2}' | xargs || true)
    DEVICE_DESC="GPU (ROCm / HIP - ${GPU_NAME:-AMD Radeon RX 7900 XTX}, device $GPU_DEVICE)"
fi

printf "\n${COLOR_CYAN}=== Chum Whisper API (whisper.cpp) ===${COLOR_RESET}\n"
printf "Base URL : http://%s:%s/v1  (LAN)\n" "$LAN_IP" "$PORT"
printf "           http://127.0.0.1:%s/v1  (local)\n" "$PORT"
printf "Endpoint : POST /v1/audio/transcriptions\n"
printf "Model    : %s\n" "$(basename "$ACTIVE_MODEL")"
printf "Threads  : %s\n" "$THREADS"
printf "Device   : %s\n" "$DEVICE_DESC"
printf "Language : %s\n" "$LANGUAGE"
printf "API Key  : %s\n" "${API_KEY:-llm}"
printf "${COLOR_CYAN}=======================================${COLOR_RESET}\n\n"

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Whisper API configuration verified successfully."
    exit 0
fi

# 5. Launch server
EXEC_CMD="$SERVER_EXE"
if [[ ! -x "$EXEC_CMD" ]]; then
    EXEC_CMD=$(command -v whisper-server || echo "")
fi

if [[ -z "$EXEC_CMD" || ! -x "$EXEC_CMD" ]]; then
    log_err "whisper-server executable not found or not executable: $SERVER_EXE"
    exit 1
fi

SERVER_ARGS=(
    "-m" "$ACTIVE_MODEL"
    "--host" "0.0.0.0"
    "--port" "$PORT"
    "-l" "$LANGUAGE"
    "-t" "$THREADS"
    "--inference-path" "/v1/audio/transcriptions"
    "-dev" "$GPU_DEVICE"
    "-sns"
)

log_step "Starting whisper-server..."
export GPU_MAX_HW_QUEUES="${GPU_MAX_HW_QUEUES:-1}"
export LD_LIBRARY_PATH="$(dirname "$EXEC_CMD"):${LD_LIBRARY_PATH:-}"
exec "$EXEC_CMD" "${SERVER_ARGS[@]}"
