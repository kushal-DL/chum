#!/usr/bin/env bash
# ==============================================================================
# install.sh — Chum one-step installer launcher for Linux (mirrors install.cmd)
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER="$SCRIPT_DIR/scripts_linux/install-chum.sh"

printf "\n\033[1;37mChum Installer (Linux)\033[0m\n"
printf "\033[0;90m======================\033[0m\n\n"

if [[ -f "$INSTALLER" ]]; then
    # Normal case: installer script ships next to us in scripts_linux/
    chmod +x "$INSTALLER" 2>/dev/null || true
    exec "$INSTALLER" --start-service "$@"
else
    # Bootstrap mode: download installer from GitHub
    printf "  Installer script not found locally. Downloading from GitHub...\n"
    TMP_SCRIPT=$(mktemp /tmp/install-chum-XXXXXX.sh)
    DL_URL="https://raw.githubusercontent.com/kushal-DL/chum/main/scripts_linux/install-chum.sh"
    
    if curl -sSL -f -o "$TMP_SCRIPT" "$DL_URL"; then
        chmod +x "$TMP_SCRIPT"
        exec "$TMP_SCRIPT" --start-service --source download "$@"
    else
        printf "\n\033[0;31mERROR: Could not download the installer from GitHub.\033[0m\n"
        printf "Check your internet connection or download from: https://github.com/kushal-DL/chum/releases\n\n"
        exit 1
    fi
fi
