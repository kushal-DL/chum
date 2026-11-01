#!/usr/bin/env bash
# ==============================================================================
# start-asp-rag.sh — Starts the ASP RAG bridge for Chum
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

PORT=8003
ASP_WHEEL_PATH=""
DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Starts the ASP RAG bridge for Chum on port $PORT (binds 0.0.0.0 by default).

Options:
  -p, --port <int>             Port to listen on (default: $PORT)
  -w, --asp-wheel-path <path>  Path to asp-*.whl package
  --dry-run                    Verify configuration without starting the bridge
  -h, --help                   Show this help message and exit
EOF
    exit 0
}

# Parse CLI arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)
            PORT="$2"; shift 2 ;;
        -w|--asp-wheel-path)
            ASP_WHEEL_PATH="$2"; shift 2 ;;
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
log_ok "Python ready: $($PY_CMD --version)"

# 1. Resolve ASP wheel
if [[ -z "$ASP_WHEEL_PATH" ]]; then
    CANDIDATES=(
        "$SCRIPT_DIR/../../ASP/dist"
        "$SCRIPT_DIR/../ASP/dist"
        "$SCRIPT_DIR/ASP/dist"
    )
    for c in "${CANDIDATES[@]}"; do
        if [[ -d "$c" ]]; then
            whl=$(find "$c" -maxdepth 1 -name "asp-*.whl" | head -n 1)
            if [[ -n "$whl" && -f "$whl" ]]; then
                ASP_WHEEL_PATH="$whl"
                break
            fi
        fi
    done
fi

if [[ -z "$ASP_WHEEL_PATH" || ! -f "$ASP_WHEEL_PATH" ]]; then
    if [[ $DRY_RUN -eq 1 ]]; then
        log_warn "[dry-run] ASP wheel not found (pass --asp-wheel-path '/path/to/asp-*.whl')"
    else
        log_err "ASP wheel not found. Pass --asp-wheel-path '/path/to/asp-0.1.0-py3-none-any.whl'"
        exit 1
    fi
else
    log_ok "[asp-rag] Using ASP wheel: $ASP_WHEEL_PATH"
fi

# 2. Check/install Python dependencies
if [[ $DRY_RUN -eq 0 && -n "$ASP_WHEEL_PATH" ]]; then
    log_step "[asp-rag] Installing/verifying Python packages..."
    $PY_CMD -m pip install "$ASP_WHEEL_PATH" fastapi "uvicorn[standard]" sentence-transformers pydantic --quiet 2>/dev/null || {
        if command -v uv >/dev/null 2>&1; then
            uv pip install "$ASP_WHEEL_PATH" fastapi "uvicorn[standard]" sentence-transformers pydantic --quiet 2>/dev/null || true
        fi
    }
fi

# 3. Kill previous instance on port
kill_port_process "$PORT" "ASP RAG bridge" 2

# 4. LAN IP & Banner
LAN_IP=$(get_lan_ip)
export BRIDGE_HOST="0.0.0.0"
export BRIDGE_PORT="$PORT"

printf "\n${COLOR_GREEN}[asp-rag] Bridge starting on port %s (all interfaces)${COLOR_RESET}\n" "$PORT"
if [[ -n "$LAN_IP" ]]; then
    printf "${COLOR_CYAN}[asp-rag] Set Chum's ASP RAG Bridge URL to:${COLOR_RESET}\n"
    printf "    ${COLOR_WHITE}http://%s:%s${COLOR_RESET}\n" "$LAN_IP" "$PORT"
    printf "${COLOR_GRAY}[asp-rag] Or use http://127.0.0.1:%s if Chum runs on this machine.${COLOR_RESET}\n" "$PORT"
fi
printf "${COLOR_GRAY}[asp-rag] Index builds on first POST /index from Chum.${COLOR_RESET}\n\n"

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] ASP RAG bridge configuration verified."
    exit 0
fi

API_SCRIPT="$SCRIPT_DIR/asp-rag-api.py"
if [[ ! -f "$API_SCRIPT" ]]; then
    if [[ -f "$SCRIPT_DIR/../scripts/asp-rag-api.py" ]]; then
        API_SCRIPT="$SCRIPT_DIR/../scripts/asp-rag-api.py"
    else
        log_err "asp-rag-api.py not found!"
        exit 1
    fi
fi

exec "$PY_CMD" "$API_SCRIPT"
