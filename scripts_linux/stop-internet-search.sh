#!/usr/bin/env bash
# ==============================================================================
# stop-internet-search.sh — Stops the Google AI Search bridge
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

PORT="${1:-8002}"
LABEL="Google AI Search bridge"

# Support --help / -h
if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
    cat <<EOF
Usage: $(basename "$0") [PORT]

Stops the Google AI Search bridge running on the specified port.
Default port: 8002
EOF
    exit 0
fi

kill_port_process "$PORT" "$LABEL"
