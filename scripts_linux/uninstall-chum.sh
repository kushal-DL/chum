#!/usr/bin/env bash
# ==============================================================================
# uninstall-chum.sh — Uninstalls Chum Collaboration Host on Linux
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

IS_ROOT=0
if [[ $EUID -eq 0 ]]; then
    IS_ROOT=1
    DEFAULT_INSTALL_DIR="/opt/chum"
    DEFAULT_DATA_DIR="/var/lib/chum"
    SYSTEMD_DIR="/etc/systemd/system"
    DESKTOP_DIR="/usr/share/applications"
    AUTOSTART_DIR="/etc/xdg/autostart"
else
    DEFAULT_INSTALL_DIR="$HOME/.local/share/chum"
    DEFAULT_DATA_DIR="$HOME/.local/state/chum"
    SYSTEMD_DIR="$HOME/.config/systemd/user"
    DESKTOP_DIR="$HOME/.local/share/applications"
    AUTOSTART_DIR="$HOME/.config/autostart"
fi

INSTALL_DIR="$DEFAULT_INSTALL_DIR"
DATA_DIR="$DEFAULT_DATA_DIR"
REMOVE_DATA=0
DRY_RUN=0
SERVICE_NAME="chum.service"

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Uninstalls Chum from the system.

Options:
  --install-dir <dir>   Installation directory (default: $DEFAULT_INSTALL_DIR)
  --data-dir <dir>      Runtime data directory (default: $DEFAULT_DATA_DIR)
  --remove-data         Also remove data directory (preserves logs/data by default)
  --dry-run             Show what would be removed without deleting
  -h, --help            Show this help message and exit
EOF
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir)
            INSTALL_DIR="$2"; shift 2 ;;
        --data-dir)
            DATA_DIR="$2"; shift 2 ;;
        --remove-data)
            REMOVE_DATA=1; shift ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

printf "\n${COLOR_WHITE}Chum Uninstaller (Linux)${COLOR_RESET}\n"
printf "${COLOR_GRAY}-----------------------------------------${COLOR_RESET}\n"

# 1. Stop and remove systemd service
log_step "Stopping and disabling systemd service '$SERVICE_NAME'..."
if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Would stop and disable $SERVICE_NAME"
else
    if [[ $IS_ROOT -eq 1 ]]; then
        systemctl stop "$SERVICE_NAME" 2>/dev/null || true
        systemctl disable "$SERVICE_NAME" 2>/dev/null || true
    else
        systemctl --user stop "$SERVICE_NAME" 2>/dev/null || true
        systemctl --user disable "$SERVICE_NAME" 2>/dev/null || true
    fi
    pkill -f "Chum.App" 2>/dev/null || true
    pkill -f "ChumHostSvc" 2>/dev/null || true
    log_ok "Service stopped"
fi

UNIT_FILE="$SYSTEMD_DIR/$SERVICE_NAME"
if [[ -f "$UNIT_FILE" ]]; then
    if [[ $DRY_RUN -eq 1 ]]; then
        log_ok "[dry-run] Would remove unit file $UNIT_FILE"
    else
        rm -f "$UNIT_FILE"
        if [[ $IS_ROOT -eq 1 ]]; then
            systemctl daemon-reload 2>/dev/null || true
        else
            systemctl --user daemon-reload 2>/dev/null || true
        fi
        log_ok "Unit file removed"
    fi
else
    log_skip "Unit file not found"
fi

# 2. Remove desktop application entry and autostart
log_step "Removing desktop application entries..."
DESKTOP_FILE="$DESKTOP_DIR/chum.desktop"
AUTOSTART_FILE="$AUTOSTART_DIR/chum.desktop"

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Would remove $DESKTOP_FILE and $AUTOSTART_FILE"
else
    rm -f "$DESKTOP_FILE" "$AUTOSTART_FILE"
    log_ok "Desktop entries removed"
fi

# 3. Remove program files
log_step "Removing program files from $INSTALL_DIR..."
if [[ -d "$INSTALL_DIR" ]]; then
    if [[ $DRY_RUN -eq 1 ]]; then
        log_ok "[dry-run] Would remove directory $INSTALL_DIR"
    else
        rm -rf "$INSTALL_DIR"
        log_ok "Program files removed"
    fi
else
    log_skip "Install directory $INSTALL_DIR not found"
fi

# 4. Remove data directory if opted in
if [[ $REMOVE_DATA -eq 1 ]]; then
    log_step "Removing data directory $DATA_DIR..."
    if [[ -d "$DATA_DIR" ]]; then
        if [[ $DRY_RUN -eq 1 ]]; then
            log_ok "[dry-run] Would remove data directory $DATA_DIR"
        else
            rm -rf "$DATA_DIR"
            log_ok "Data directory removed"
        fi
    else
        log_skip "Data directory $DATA_DIR not found"
    fi
else
    log_skip "Preserved data directory: $DATA_DIR (use --remove-data to delete)"
fi

# 5. Log uninstallation
if command -v logger >/dev/null 2>&1; then
    logger -t Chum "Chum uninstalled from $INSTALL_DIR" 2>/dev/null || true
fi

printf "\n${COLOR_GREEN}Uninstallation complete.${COLOR_RESET}\n\n"
