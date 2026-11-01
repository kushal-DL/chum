#!/usr/bin/env bash
# ==============================================================================
# stop-qwen.sh — Stops the Qwen LLM server
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

PORT="${1:-8001}"
LABEL="Qwen LLM server"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
    cat <<EOF
Usage: $(basename "$0") [PORT]

Stops the Qwen LLM server running on the specified port.
Default port: 8001 (pass 8003 for fast LLM)
EOF
    exit 0
fi

kill_port_process "$PORT" "$LABEL"
