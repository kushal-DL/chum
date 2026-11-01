#!/usr/bin/env bash
# ==============================================================================
# stop-whisper.sh — Stops the Whisper STT server
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

PORT="${1:-8000}"
LABEL="Whisper STT server"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
    cat <<EOF
Usage: $(basename "$0") [PORT]

Stops the Whisper STT server running on the specified port.
Default port: 8000
EOF
    exit 0
fi

kill_port_process "$PORT" "$LABEL"
