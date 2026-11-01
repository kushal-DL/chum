#!/usr/bin/env bash
# ==============================================================================
# start-internet-search.sh — Starts the Google AI Mode image search bridge
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

PORT=8002
CDP=0
NETWORK_ACCESS=0
DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Starts the Google AI Mode image search bridge for Chum on port $PORT.

Options:
  -p, --port <int>        Port to listen on (default: $PORT)
  --cdp                   Attach to already-running Chrome on port 9222
  -n, --network-access    Bind to 0.0.0.0 so other LAN machines can reach this bridge
  --dry-run               Validate configuration and exit without starting server
  -h, --help              Show this help message and exit

Modes:
  Default:               Playwright opens a Chrome session in google-session/
  CDP Mode (--cdp):      Attaches to existing Chrome with --remote-debugging-port=9222
EOF
    exit 0
}

# Parse CLI arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)
            PORT="$2"; shift 2 ;;
        --cdp)
            CDP=1; shift ;;
        -n|--network-access)
            NETWORK_ACCESS=1; shift ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

# 0. Check Python & environment
PY_CMD=$(ensure_python_env "3.10") || exit 1
log_ok "Python ready: $($PY_CMD --version) ($PY_CMD)"

# 1. Check Python dependencies
if [[ $DRY_RUN -eq 0 ]]; then
    if ! "$PY_CMD" -c "import fastapi, uvicorn, playwright, pydantic" 2>/dev/null; then
        log_step "Installing/verifying Python packages (fastapi, uvicorn, playwright, pydantic)..."
        install_python_packages "$PY_CMD" fastapi "uvicorn[standard]" playwright pydantic || exit 1
    fi

    log_step "Checking Playwright Chromium..."
    "$PY_CMD" -m playwright install chromium || true
fi

# 2. Kill any previous instance holding port
kill_port_process "$PORT" "Google AI Search bridge" 2

# 3. Configure bind address and environment variables
if [[ $NETWORK_ACCESS -eq 1 ]]; then
    export BRIDGE_HOST="0.0.0.0"
    export BRIDGE_PORT="$PORT"
    LAN_IP=$(get_lan_ip)
    printf "\n${COLOR_CYAN}[internet-search] Network access enabled — set Chum's Google Search URL to:${COLOR_RESET}\n"
    printf "    ${COLOR_WHITE}http://%s:%s${COLOR_RESET}\n\n" "$LAN_IP" "$PORT"
else
    export BRIDGE_HOST="127.0.0.1"
    export BRIDGE_PORT="$PORT"
fi

if [[ $CDP -eq 1 ]]; then
    printf "${COLOR_GREEN}[internet-search] CDP mode: attaching to Chrome on port 9222${COLOR_RESET}\n"
    printf "${COLOR_YELLOW}[internet-search] Ensure Chrome is running with --remote-debugging-port=9222${COLOR_RESET}\n"
    export CDP_URL="http://localhost:9222"
else
    printf "${COLOR_GREEN}[internet-search] Starting Google AI Search bridge on http://%s:%s${COLOR_RESET}\n" "$BRIDGE_HOST" "$PORT"
    export CDP_URL=""
fi

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Google AI Search bridge verified successfully."
    exit 0
fi

API_SCRIPT="$SCRIPT_DIR/internet-search-api.py"
if [[ ! -f "$API_SCRIPT" ]]; then
    # Check parent scripts folder if not in scripts_linux
    if [[ -f "$SCRIPT_DIR/../scripts/internet-search-api.py" ]]; then
        API_SCRIPT="$SCRIPT_DIR/../scripts/internet-search-api.py"
    else
        log_err "internet-search-api.py not found!"
        exit 1
    fi
fi

exec "$PY_CMD" "$API_SCRIPT"
