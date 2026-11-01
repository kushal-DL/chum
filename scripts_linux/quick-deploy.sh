#!/usr/bin/env bash
# ==============================================================================
# quick-deploy.sh — Fast build and deploy for local development
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Resolve default install dir (prefers /opt/chum if writable or user install dir)
if [[ $EUID -eq 0 && -d "/opt/chum/App" ]]; then
    DEFAULT_INSTALL_DIR="/opt/chum/App"
elif [[ -d "$HOME/.local/share/chum/App" ]]; then
    DEFAULT_INSTALL_DIR="$HOME/.local/share/chum/App"
else
    DEFAULT_INSTALL_DIR="/opt/chum/App"
fi

INSTALL_DIR="$DEFAULT_INSTALL_DIR"
DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Builds Chum in Release mode and copies updated assemblies to the installation directory.

Options:
  --install-dir <dir>   Destination App directory (default: $INSTALL_DIR)
  --repo-root <dir>     Repository root directory (default: $REPO_ROOT)
  --dry-run             Validate build and target paths without copying files
  -h, --help            Show this help message and exit
EOF
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir)
            INSTALL_DIR="$2"; shift 2 ;;
        --repo-root)
            REPO_ROOT="$2"; shift 2 ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

if [[ ! -d "$INSTALL_DIR" ]]; then
    log_err "Chum is not installed at '$INSTALL_DIR'. Run install-chum.sh first."
    exit 1
fi

log_step "Checking .NET SDK..."
if ! command -v dotnet >/dev/null 2>&1; then
    log_err ".NET SDK not found on PATH. Install from https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VER=$(dotnet --version 2>&1 || echo "unknown")
log_ok ".NET SDK $DOTNET_VER found."

SLN_PATH="$REPO_ROOT/src/Chum.sln"
if [[ ! -f "$SLN_PATH" ]]; then
    log_err "Solution file not found at '$SLN_PATH'."
    exit 1
fi

log_step "Building Chum in Release configuration..."
if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Build step verified."
else
    dotnet build "$SLN_PATH" -c Release --nologo -v minimal
    if [[ $? -ne 0 ]]; then
        log_err "dotnet build failed."
        exit 1
    fi
    log_ok "Build succeeded."
fi

# Stop running Chum application if active
log_step "Checking for running Chum processes..."
CHUM_PIDS=$(pgrep -f "Chum.App" 2>/dev/null || true)
if [[ -n "$CHUM_PIDS" ]]; then
    log_step "Stopping Chum.App (PID: $CHUM_PIDS)..."
    if [[ $DRY_RUN -eq 0 ]]; then
        pkill -f "Chum.App" 2>/dev/null || true
        sleep 1
    fi
    log_ok "Chum.App stopped."
else
    log_skip "Chum.App is not running."
fi

# Locate compiled assemblies
PROJECT_DIRS=(
    "$REPO_ROOT/src/Chum.App/bin/Release"
    "$REPO_ROOT/src/Chum.Audio/bin/Release"
    "$REPO_ROOT/src/Chum.Llm/bin/Release"
    "$REPO_ROOT/src/Chum.Transcription/bin/Release"
)

TARGET_FILES=(
    "Chum.App.dll" "Chum.App.pdb" "Chum.App"
    "Chum.Audio.dll" "Chum.Audio.pdb"
    "Chum.Llm.dll" "Chum.Llm.pdb"
    "Chum.Transcription.dll" "Chum.Transcription.pdb"
)

log_step "Deploying updated files to $INSTALL_DIR..."
for file in "${TARGET_FILES[@]}"; do
    copied=0
    for pdir in "${PROJECT_DIRS[@]}"; do
        if [[ -d "$pdir" ]]; then
            # Find file in target framework subdirectories (e.g. net8.0, net10.0-windows, etc.)
            src_match=$(find "$pdir" -type f -name "$file" | head -n 1)
            if [[ -n "$src_match" && -f "$src_match" ]]; then
                if [[ $DRY_RUN -eq 1 ]]; then
                    log_ok "[dry-run] Would copy $file"
                else
                    cp -f "$src_match" "$INSTALL_DIR/$file"
                    log_ok "Copied $file"
                fi
                copied=1
                break
            fi
        fi
    done
    if [[ $copied -eq 0 ]]; then
        log_skip "$file (not found in project outputs)"
    fi
done

printf "\n${COLOR_GREEN}Done. Chum binaries deployed to %s.${COLOR_RESET}\n\n" "$INSTALL_DIR"
