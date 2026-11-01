#!/usr/bin/env bash
# ==============================================================================
# start-fast-llm-api.sh — Starts the fast local LLM for Chum (Qwen3-4B Q4_K_M)
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Default parameter values
PORT=8003
MODEL_REPO="Qwen/Qwen3-4B-GGUF"
MODEL_FILE="Qwen3-4B-Q4_K_M.gguf"
CONTEXT_SIZE=2048
API_KEY=""
NO_AUTH=0
TOOLS_DIR="$REPO_ROOT/local-llm/llama.cpp"
MODELS_DIR="$REPO_ROOT/local-llm/models"
DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Starts the fast local LLM (Qwen3-4B) for low-latency Chum responses on port $PORT.

Options:
  -p, --port <int>           Port to listen on (default: $PORT)
  -r, --model-repo <repo>    Hugging Face repo for GGUF (default: $MODEL_REPO)
  -m, --model-file <file>    GGUF model filename (default: $MODEL_FILE)
  -c, --context-size <int>   Context window in tokens (default: $CONTEXT_SIZE)
  -k, --api-key <key>        Fixed API key (default: chum-llm-key-2026)
  --no-auth                  Disable authentication
  --tools-dir <dir>          Directory where llama-server lives (default: $TOOLS_DIR)
  --models-dir <dir>         Directory where GGUF files live (default: $MODELS_DIR)
  --dry-run                  Prepare configuration and banner without running server
  -h, --help                 Show this help message and exit
EOF
    exit 0
}

# Parse CLI arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)
            PORT="$2"; shift 2 ;;
        -r|--model-repo)
            MODEL_REPO="$2"; shift 2 ;;
        -m|--model-file)
            MODEL_FILE="$2"; shift 2 ;;
        -c|--context-size)
            CONTEXT_SIZE="$2"; shift 2 ;;
        -k|--api-key)
            API_KEY="$2"; shift 2 ;;
        --no-auth)
            NO_AUTH=1; shift ;;
        --tools-dir)
            TOOLS_DIR="$2"; shift 2 ;;
        --models-dir)
            MODELS_DIR="$2"; shift 2 ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

# 0. Check Python
PY_CMD=$(check_python "3.10") || exit 1

# 1. Ensure llama-server is present
SERVER_EXE="$TOOLS_DIR/llama-server"
if [[ ! -x "$SERVER_EXE" ]] && ! command -v llama-server >/dev/null 2>&1; then
    if [[ $DRY_RUN -eq 1 ]]; then
        log_step "[dry-run] llama-server check skipped"
    else
        log_warn "llama-server not found at $SERVER_EXE, checking for latest Linux build..."
        mkdir -p "$TOOLS_DIR"
        
        RELEASE_JSON=$(curl -sSL "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest" 2>/dev/null || true)
        DL_URL=$(echo "$RELEASE_JSON" | grep -oP '"browser_download_url":\s*"\K[^"]*bin-ubuntu-x64\.zip' | head -n 1)
        if [[ -z "$DL_URL" ]]; then
            DL_URL=$(echo "$RELEASE_JSON" | grep -oP '"browser_download_url":\s*"\K[^"]*bin-linux-x64[^"]*\.(zip|tar\.gz)' | head -n 1)
        fi

        if [[ -n "$DL_URL" ]]; then
            log_step "Downloading $(basename "$DL_URL")..."
            ZIP_PATH="$TOOLS_DIR/$(basename "$DL_URL")"
            curl -L -o "$ZIP_PATH" "$DL_URL"
            if [[ "$ZIP_PATH" =~ \.zip$ ]]; then
                unzip -q -o "$ZIP_PATH" -d "$TOOLS_DIR"
            else
                tar -xzf "$ZIP_PATH" -C "$TOOLS_DIR"
            fi
            rm -f "$ZIP_PATH"
            chmod +x "$TOOLS_DIR"/llama-* 2>/dev/null || true
            log_ok "llama.cpp installed at $TOOLS_DIR"
        else
            log_warn "Could not auto-download prebuilt llama-server. Ensure llama-server is in PATH or $TOOLS_DIR."
        fi
    fi
fi

# 2. Ensure model GGUF is present and valid
MODEL_PATH="$MODELS_DIR/$MODEL_FILE"
if ! test_gguf_magic "$MODEL_PATH"; then
    if [[ $DRY_RUN -eq 1 ]]; then
        log_step "[dry-run] Fast model download check skipped for $MODEL_FILE"
    else
        if [[ -f "$MODEL_PATH" ]]; then
            log_warn "Existing model file is corrupt or incomplete — re-downloading..."
            rm -f "$MODEL_PATH"
        else
            log_step "Fast model not found, downloading $MODEL_FILE from $MODEL_REPO..."
        fi
        mkdir -p "$MODELS_DIR"
        curl -L -C - -o "$MODEL_PATH" "https://huggingface.co/$MODEL_REPO/resolve/main/$MODEL_FILE"

        if ! test_gguf_magic "$MODEL_PATH"; then
            log_err "Download failed — not a valid GGUF file. Check ModelRepo='$MODEL_REPO' and ModelFile='$MODEL_FILE'."
            exit 1
        fi
        log_ok "Fast model downloaded to $MODEL_PATH"
    fi
fi

# 3. Kill previous instance holding port if any
EXISTING_PID=$(find_pid_by_port "$PORT")
if [[ -n "$EXISTING_PID" ]]; then
    log_step "Stopping previous instance on port $PORT (PID $EXISTING_PID)..."
    kill_port_process "$PORT" "Previous instance" 2
fi

# 4. Banner
LAN_IP=$(get_lan_ip)
KEY_LABEL="chum-llm-key-2026 (default)"
if [[ $NO_AUTH -eq 1 ]]; then
    KEY_LABEL="(none - auth disabled)"
elif [[ -n "$API_KEY" ]]; then
    KEY_LABEL="$API_KEY"
fi

printf "\n${COLOR_YELLOW}=== Chum Fast LLM API (Qwen3-4B) ===${COLOR_RESET}\n"
printf "Base URL : http://%s:%s/v1  (LAN)\n" "$LAN_IP" "$PORT"
printf "           http://127.0.0.1:%s/v1  (local)\n" "$PORT"
printf "API Key  : %s\n" "$KEY_LABEL"
printf "Model    : %s\n" "$MODEL_FILE"
printf "Context  : %s tokens\n" "$CONTEXT_SIZE"
printf "Vision   : OFF (text only)\n"
printf "${COLOR_YELLOW}======================================${COLOR_RESET}\n\n"

# 5. Launch via start-llm.py with --no-mmproj
LAUNCH_SCRIPT="$SCRIPT_DIR/start-llm.py"
PY_ARGS=(
    "--port" "$PORT"
    "--host" "0.0.0.0"
    "--model-path" "$MODEL_PATH"
    "--no-mmproj"
    "--context-size" "$CONTEXT_SIZE"
    "--tools-dir" "$TOOLS_DIR"
)

if [[ $NO_AUTH -eq 1 ]]; then
    PY_ARGS+=("--no-auth")
elif [[ -n "$API_KEY" ]]; then
    PY_ARGS+=("--api-key" "$API_KEY")
fi

if [[ $DRY_RUN -eq 1 ]]; then
    PY_ARGS+=("--dry-run")
fi

printf "${COLOR_YELLOW}Starting server...${COLOR_RESET}\n\n"
exec "$PY_CMD" "$LAUNCH_SCRIPT" "${PY_ARGS[@]}"
