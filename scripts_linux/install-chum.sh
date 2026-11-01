#!/usr/bin/env bash
# ==============================================================================
# install-chum.sh — One-step Chum installer for Linux
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Determine root vs user installation defaults
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
START_SERVICE=0
SOURCE_MODE="auto"
SRC_PATH=""
BIN_PATH=""
REPO_SLUG="kushal-DL/chum"
NON_INTERACTIVE=0
DEPLOY_PKG_DIR=""
DRY_RUN=0

SERVICE_NAME="chum.service"

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Installs Chum Collaboration Host and tray application on Linux.

Options:
  --install-dir <dir>       Root installation directory (default: $DEFAULT_INSTALL_DIR)
  --data-dir <dir>          Runtime data directory (default: $DEFAULT_DATA_DIR)
  --start-service           Start Chum systemd service after registration
  --source <mode>           Installation source: auto, prebuilt, build, download (default: auto)
  --src-path <dir>          Source tree path (when source=build)
  --bin-path <dir>          Prebuilt binaries folder containing App/ and Service/
  --repo-slug <slug>        GitHub repository for release download (default: $REPO_SLUG)
  --non-interactive         Never prompt; apply default actions
  --deploy-pkg-dir <dir>    Emit distributable package after building
  --dry-run                 Simulate installation without touching system
  -h, --help                Show this help message and exit
EOF
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir)
            INSTALL_DIR="$2"; shift 2 ;;
        --data-dir)
            DATA_DIR="$2"; shift 2 ;;
        --start-service)
            START_SERVICE=1; shift ;;
        --source)
            SOURCE_MODE="$2"; shift 2 ;;
        --src-path)
            SRC_PATH="$2"; shift 2 ;;
        --bin-path)
            BIN_PATH="$2"; shift 2 ;;
        --repo-slug)
            REPO_SLUG="$2"; shift 2 ;;
        --non-interactive)
            NON_INTERACTIVE=1; shift ;;
        --deploy-pkg-dir)
            DEPLOY_PKG_DIR="$2"; shift 2 ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

printf "\n${COLOR_WHITE}Chum Installer (Linux)${COLOR_RESET}\n"
printf "${COLOR_GRAY}=========================================${COLOR_RESET}\n"

SERVICE_INSTALL_DIR="$INSTALL_DIR/Service"
APP_INSTALL_DIR="$INSTALL_DIR/App"

SRC_PUBLISH_DIR=""
APP_PUBLISH_DIR=""

test_bins() {
    local app_dir="$1"
    local svc_dir="$2"
    if [[ (-f "$app_dir/Chum.App" || -f "$app_dir/Chum.App.dll") && \
          (-f "$svc_dir/ChumHostSvc" || -f "$svc_dir/Chum.Service" || -f "$svc_dir/Chum.Service.dll") ]]; then
        return 0
    fi
    return 1
}

resolve_src_root() {
    local p="$1"
    if [[ -z "$p" || ! -d "$p" ]]; then
        return 1
    fi
    if [[ -f "$p/Chum.Service/Chum.Service.csproj" ]]; then
        printf "%s" "$p"
        return 0
    fi
    if [[ -f "$p/src/Chum.Service/Chum.Service.csproj" ]]; then
        printf "%s" "$p/src"
        return 0
    fi
    return 1
}

invoke_build() {
    local src_root="$1"
    log_step "[Source] Building from source: $src_root"
    if ! command -v dotnet >/dev/null 2>&1; then
        log_err ".NET SDK not found. Install from https://dotnet.microsoft.com/download, or choose download mode."
        exit 1
    fi

    local svc_proj="$src_root/Chum.Service/Chum.Service.csproj"
    local app_proj="$src_root/Chum.App/Chum.App.csproj"
    local svc_out="$src_root/Chum.Service/publish"
    local app_out="$src_root/Chum.App/publish"

    if [[ $DRY_RUN -eq 1 ]]; then
        log_ok "[dry-run] Build command verified for $svc_proj and $app_proj"
        SRC_PUBLISH_DIR="$svc_out"
        APP_PUBLISH_DIR="$app_out"
        return 0
    fi

    log_step "Publishing Chum.Service (self-contained, linux-x64)..."
    dotnet publish "$svc_proj" -r linux-x64 -c Release --self-contained true -o "$svc_out" --nologo -v minimal
    log_ok "Chum.Service published"

    log_step "Publishing Chum.App (self-contained, linux-x64)..."
    dotnet publish "$app_proj" -r linux-x64 -c Release --self-contained true -o "$app_out" --nologo -v minimal
    log_ok "Chum.App published"

    SRC_PUBLISH_DIR="$svc_out"
    APP_PUBLISH_DIR="$app_out"
}

invoke_download() {
    log_step "[Source] Downloading latest release from GitHub ($REPO_SLUG)..."
    local api="https://api.github.com/repos/$REPO_SLUG/releases/latest"
    local rel_json
    rel_json=$(curl -sSL "$api" 2>/dev/null || true)
    
    local asset_url
    asset_url=$(echo "$rel_json" | grep -oP '"browser_download_url":\s*"\K[^"]*linux-x64[^"]*\.tar\.gz' | head -n 1)
    if [[ -z "$asset_url" ]]; then
        asset_url=$(echo "$rel_json" | grep -oP '"browser_download_url":\s*"\K[^"]*\.tar\.gz' | head -n 1)
    fi

    if [[ -z "$asset_url" ]]; then
        if [[ $DRY_RUN -eq 1 ]]; then
            log_warn "[dry-run] No published release tarball found on GitHub yet."
            return 0
        fi
        log_err "Could not find a Linux release asset at $api."
        exit 1
    fi

    local tmp_dir
    tmp_dir=$(mktemp -d)
    log_step "Downloading $(basename "$asset_url")..."
    curl -sSL -o "$tmp_dir/release.tar.gz" "$asset_url"
    tar -xzf "$tmp_dir/release.tar.gz" -C "$tmp_dir"
    
    local found_app
    local found_svc
    found_app=$(find "$tmp_dir" -type d -name "App" | head -n 1)
    found_svc=$(find "$tmp_dir" -type d -name "Service" | head -n 1)

    if [[ -z "$found_app" || -z "$found_svc" ]]; then
        log_err "Downloaded archive did not contain App/ and Service/ directories."
        rm -rf "$tmp_dir"
        exit 1
    fi

    APP_PUBLISH_DIR="$found_app"
    SRC_PUBLISH_DIR="$found_svc"
    log_ok "Release package downloaded and extracted"
}

resolve_source() {
    local prebuilt_svc="${BIN_PATH:-$REPO_ROOT}/Service"
    local prebuilt_app="${BIN_PATH:-$REPO_ROOT}/App"

    case "$SOURCE_MODE" in
        download)
            invoke_download ;;
        prebuilt)
            if test_bins "$prebuilt_app" "$prebuilt_svc"; then
                log_step "[Source] Using prebuilt binaries"
                APP_PUBLISH_DIR="$prebuilt_app"
                SRC_PUBLISH_DIR="$prebuilt_svc"
            else
                log_err "No prebuilt binaries found at $prebuilt_app and $prebuilt_svc"
                exit 1
            fi
            ;;
        build)
            local target_src="${SRC_PATH:-$REPO_ROOT/src}"
            local resolved
            resolved=$(resolve_src_root "$target_src") || {
                log_err "No Chum source tree found at $target_src."
                exit 1
            }
            invoke_build "$resolved"
            ;;
        auto)
            if test_bins "$prebuilt_app" "$prebuilt_svc"; then
                log_ok "[Source] Found prebuilt binaries (no build needed)"
                APP_PUBLISH_DIR="$prebuilt_app"
                SRC_PUBLISH_DIR="$prebuilt_svc"
            elif resolve_src_root "$REPO_ROOT/src" >/dev/null 2>&1; then
                local sr
                sr=$(resolve_src_root "$REPO_ROOT/src")
                invoke_build "$sr"
            elif [[ $NON_INTERACTIVE -eq 1 ]]; then
                invoke_download
            else
                printf "\n  No binaries or source were found next to installer.\n"
                printf "  How should Chum be installed?\n"
                printf "    [1] Download latest release from GitHub (recommended)\n"
                printf "    [2] Build from source\n"
                printf "    [C] Cancel\n"
                read -r -p "  Choice [1]: " choice
                case "${choice:-1}" in
                    1) invoke_download ;;
                    2)
                        read -r -p "  Path to src directory: " user_src
                        local sr
                        sr=$(resolve_src_root "$user_src") || {
                            log_err "Invalid source directory."
                            exit 1
                        }
                        invoke_build "$sr"
                        ;;
                    *)
                        log_warn "Installation cancelled."
                        exit 0
                        ;;
                esac
            fi
            ;;
    esac
}

# --- Prior installation handling ---
check_existing_install() {
    local has_svc=0
    local has_files=0

    if [[ -f "$SYSTEMD_DIR/$SERVICE_NAME" ]]; then
        has_svc=1
    fi
    if [[ -d "$INSTALL_DIR" ]]; then
        has_files=1
    fi

    if [[ $has_svc -eq 1 || $has_files -eq 1 ]]; then
        printf "\n  ${COLOR_YELLOW}Previous Chum installation detected:${COLOR_RESET}\n"
        printf "    Service : %s\n" "$([[ $has_svc -eq 1 ]] && echo "installed" || echo "not found")"
        printf "    Files   : %s\n" "$([[ $has_files -eq 1 ]] && echo "$INSTALL_DIR" || echo "not found")"
        if [[ $NON_INTERACTIVE -eq 1 || $DRY_RUN -eq 1 ]]; then
            log_ok "Updating existing installation (non-interactive mode)"
        else
            printf "  What would you like to do?\n"
            printf "    [U] Update / reinstall (replace it)  (recommended)\n"
            printf "    [R] Remove it and exit\n"
            printf "    [C] Cancel\n"
            read -r -p "  Choice [U]: " choice
            case "${choice:-U}" in
                [rR])
                    "$SCRIPT_DIR/uninstall-chum.sh" --install-dir "$INSTALL_DIR" --data-dir "$DATA_DIR"
                    exit 0 ;;
                [cC])
                    log_warn "Cancelled by user."
                    exit 0 ;;
                *)
                    log_ok "Updating existing installation..." ;;
            esac
        fi
    fi
}

check_existing_install
resolve_source

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Source resolved successfully."
    log_ok "[dry-run] Install directory: $INSTALL_DIR"
    log_ok "[dry-run] Data directory: $DATA_DIR"
    exit 0
fi

# Stop existing services / processes
log_step "Stopping any existing Chum services..."
if [[ $IS_ROOT -eq 1 ]]; then
    systemctl stop "$SERVICE_NAME" 2>/dev/null || true
else
    systemctl --user stop "$SERVICE_NAME" 2>/dev/null || true
fi
pkill -f "Chum.App" 2>/dev/null || true
pkill -f "ChumHostSvc" 2>/dev/null || true

# Copy files
log_step "Installing files to $INSTALL_DIR..."
mkdir -p "$SERVICE_INSTALL_DIR" "$APP_INSTALL_DIR"
if [[ -n "$SRC_PUBLISH_DIR" && -d "$SRC_PUBLISH_DIR" ]]; then
    cp -r "$SRC_PUBLISH_DIR"/* "$SERVICE_INSTALL_DIR/"
fi
if [[ -n "$APP_PUBLISH_DIR" && -d "$APP_PUBLISH_DIR" ]]; then
    cp -r "$APP_PUBLISH_DIR"/* "$APP_INSTALL_DIR/"
fi
chmod +x "$SERVICE_INSTALL_DIR"/* "$APP_INSTALL_DIR"/* 2>/dev/null || true
log_ok "Program files copied"

# Create Data Directory
log_step "Configuring data directory: $DATA_DIR..."
mkdir -p "$DATA_DIR"
chmod 775 "$DATA_DIR" 2>/dev/null || true
log_ok "Data directory configured"

# Create default config.json if not present
CONFIG_FILE="$APP_INSTALL_DIR/config.json"
if [[ ! -f "$CONFIG_FILE" ]]; then
    cat <<'EOF' > "$CONFIG_FILE"
{
  "AnthropicApiKey": "llm",
  "OpenAiApiKey": "llm",
  "CloudSttApiKey": "llm",
  "InternetSearchApiKey": "llm",
  "RagApiKey": "llm"
}
EOF
    chmod 664 "$CONFIG_FILE" 2>/dev/null || true
    log_ok "config.json created"
fi

# Determine service executable
SERVICE_BIN="$SERVICE_INSTALL_DIR/ChumHostSvc"
if [[ ! -f "$SERVICE_BIN" ]]; then
    SERVICE_BIN="$SERVICE_INSTALL_DIR/Chum.Service"
fi
if [[ ! -f "$SERVICE_BIN" && -f "$SERVICE_INSTALL_DIR/Chum.Service.dll" ]]; then
    SERVICE_BIN="$(command -v dotnet || echo "/usr/bin/dotnet") $SERVICE_INSTALL_DIR/Chum.Service.dll"
fi

# Configure Systemd Unit
log_step "Configuring systemd service..."
mkdir -p "$SYSTEMD_DIR"
cat <<EOF > "$SYSTEMD_DIR/$SERVICE_NAME"
[Unit]
Description=Chum Collaboration Host
After=network.target

[Service]
Type=simple
WorkingDirectory=$SERVICE_INSTALL_DIR
ExecStart=$SERVICE_BIN
Restart=always
RestartSec=10

[Install]
WantedBy=default.target
EOF

if [[ $IS_ROOT -eq 1 ]]; then
    systemctl daemon-reload 2>/dev/null || true
    systemctl enable "$SERVICE_NAME" 2>/dev/null || true
else
    systemctl --user daemon-reload 2>/dev/null || true
    systemctl --user enable "$SERVICE_NAME" 2>/dev/null || true
fi
log_ok "Systemd service $SERVICE_NAME configured"

# Configure Desktop Entry and Autostart
log_step "Creating desktop application entry..."
mkdir -p "$DESKTOP_DIR" "$AUTOSTART_DIR"
APP_BIN="$APP_INSTALL_DIR/Chum.App"
if [[ ! -f "$APP_BIN" && -f "$APP_INSTALL_DIR/Chum.App.dll" ]]; then
    APP_BIN="$(command -v dotnet || echo "/usr/bin/dotnet") $APP_INSTALL_DIR/Chum.App.dll"
fi

cat <<EOF > "$DESKTOP_DIR/chum.desktop"
[Desktop Entry]
Type=Application
Name=Chum
Comment=Chum AI meeting co-pilot
Exec=$APP_BIN
Icon=system-run
Terminal=false
Categories=Utility;Office;
EOF
chmod +x "$DESKTOP_DIR/chum.desktop" 2>/dev/null || true
cp -f "$DESKTOP_DIR/chum.desktop" "$AUTOSTART_DIR/chum.desktop" 2>/dev/null || true
log_ok "Desktop entry and autostart registered"

if [[ $START_SERVICE -eq 1 ]]; then
    log_step "Starting $SERVICE_NAME..."
    if [[ $IS_ROOT -eq 1 ]]; then
        systemctl start "$SERVICE_NAME" 2>/dev/null || true
    else
        systemctl --user start "$SERVICE_NAME" 2>/dev/null || true
    fi
    log_ok "$SERVICE_NAME started."
fi

printf "\n${COLOR_GREEN}Installation complete.${COLOR_RESET}\n"
printf "  Install Directory: %s\n" "$INSTALL_DIR"
printf "  Data Directory:    %s\n" "$DATA_DIR"
printf "  Service:           %s\n\n" "$SERVICE_NAME"
