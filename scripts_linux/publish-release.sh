#!/usr/bin/env bash
# ==============================================================================
# publish-release.sh — Build self-contained Chum package and publish release
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC_ROOT="$REPO_ROOT/src"

VERSION=""
REPO_SLUG="kushal-DL/chum"
PUBLISH=0
DRAFT=0
OUTPUT_DIR=""
RUNTIME="linux-x64"
DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Publishes self-contained Chum binaries and generates a distribution archive.

Options:
  -v, --version <ver>      Release version (default: parsed from Chum.App.csproj)
  -r, --repo-slug <slug>   GitHub repository slug (default: $REPO_SLUG)
  --runtime <rid>          Target runtime identifier (default: $RUNTIME)
  --output-dir <dir>       Directory for output archives (default: <repo>/dist)
  --publish                Publish release to GitHub using 'gh' CLI
  --draft                  Create release as draft when publishing
  --dry-run                Validate projects and version without building
  -h, --help               Show this help message and exit
EOF
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -v|--version)
            VERSION="$2"; shift 2 ;;
        -r|--repo-slug)
            REPO_SLUG="$2"; shift 2 ;;
        --runtime)
            RUNTIME="$2"; shift 2 ;;
        --output-dir)
            OUTPUT_DIR="$2"; shift 2 ;;
        --publish)
            PUBLISH=1; shift ;;
        --draft)
            DRAFT=1; shift ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

printf "\n${COLOR_WHITE}Chum Release Publisher (Linux)${COLOR_RESET}\n"
printf "${COLOR_GRAY}-----------------------------------------${COLOR_RESET}\n"

SVC_PROJ="$SRC_ROOT/Chum.Service/Chum.Service.csproj"
APP_PROJ="$SRC_ROOT/Chum.App/Chum.App.csproj"

if [[ ! -f "$SVC_PROJ" ]]; then
    log_err "Service project not found: $SVC_PROJ"
    exit 1
fi
if [[ ! -f "$APP_PROJ" ]]; then
    log_err "App project not found: $APP_PROJ"
    exit 1
fi

# Resolve version from Chum.App.csproj if not specified
if [[ -z "$VERSION" ]]; then
    if grep -q "<AssemblyVersion>" "$APP_PROJ"; then
        VERSION=$(grep -oP '(?<=<AssemblyVersion>)\s*([0-9]+(\.[0-9]+){1,3})' "$APP_PROJ" | head -n 1 | xargs)
    elif grep -q "<Version>" "$APP_PROJ"; then
        VERSION=$(grep -oP '(?<=<Version>)\s*([0-9]+(\.[0-9]+){1,3})' "$APP_PROJ" | head -n 1 | xargs)
    else
        VERSION="1.0.0"
    fi
fi

TAG="v$VERSION"
log_ok "Version $VERSION (tag $TAG)"

if [[ -z "$OUTPUT_DIR" ]]; then
    OUTPUT_DIR="$REPO_ROOT/dist"
fi

STAGE_DIR="$OUTPUT_DIR/chum-$VERSION"
ARCHIVE_TAR="$OUTPUT_DIR/chum-$VERSION-$RUNTIME.tar.gz"
ARCHIVE_ZIP="$OUTPUT_DIR/chum-$VERSION-$RUNTIME.zip"

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] Staging directory: $STAGE_DIR"
    log_ok "[dry-run] Target archives: $ARCHIVE_TAR, $ARCHIVE_ZIP"
    exit 0
fi

# Check dotnet
if ! command -v dotnet >/dev/null 2>&1; then
    log_err ".NET SDK is required to publish releases."
    exit 1
fi

# Clean previous staging
rm -rf "$STAGE_DIR" "$ARCHIVE_TAR" "$ARCHIVE_ZIP"
mkdir -p "$STAGE_DIR" "$OUTPUT_DIR"

log_step "Publishing Chum.Service (self-contained, $RUNTIME)..."
dotnet publish "$SVC_PROJ" -r "$RUNTIME" -c Release --self-contained true -o "$STAGE_DIR/Service" --nologo -v minimal
log_ok "Chum.Service published"

log_step "Publishing Chum.App (self-contained, $RUNTIME)..."
dotnet publish "$APP_PROJ" -r "$RUNTIME" -c Release --self-contained true -o "$STAGE_DIR/App" --nologo -v minimal
log_ok "Chum.App published"

log_step "Adding installer and scripts..."
mkdir -p "$STAGE_DIR/scripts_linux"
cp -r "$SCRIPT_DIR"/* "$STAGE_DIR/scripts_linux/"
if [[ -f "$REPO_ROOT/install.sh" ]]; then
    cp "$REPO_ROOT/install.sh" "$STAGE_DIR/"
fi
chmod +x "$STAGE_DIR/scripts_linux"/*.sh 2>/dev/null || true
log_ok "Scripts staged"

log_step "Packaging tarball and zip archives..."
tar -czf "$ARCHIVE_TAR" -C "$OUTPUT_DIR" "chum-$VERSION"
if command -v zip >/dev/null 2>&1; then
    (cd "$OUTPUT_DIR" && zip -q -r "$ARCHIVE_ZIP" "chum-$VERSION")
fi
log_ok "Created: $ARCHIVE_TAR"

if [[ $PUBLISH -eq 1 ]]; then
    if ! command -v gh >/dev/null 2>&1; then
        log_warn "GitHub CLI 'gh' not found. Install from https://cli.github.com/."
    else
        log_step "Publishing release $TAG to GitHub repository $REPO_SLUG..."
        GH_ARGS=("release" "create" "$TAG" "$ARCHIVE_TAR" "--repo" "$REPO_SLUG" "--title" "Chum $VERSION" "--notes" "Chum $VERSION for Linux ($RUNTIME)")
        if [[ -f "$ARCHIVE_ZIP" ]]; then
            GH_ARGS+=("$ARCHIVE_ZIP")
        fi
        if [[ $DRAFT -eq 1 ]]; then
            GH_ARGS+=("--draft")
        fi
        gh "${GH_ARGS[@]}" || gh release upload "$TAG" "$ARCHIVE_TAR" --repo "$REPO_SLUG" --clobber
        log_ok "Published to https://github.com/$REPO_SLUG/releases/tag/$TAG"
    fi
fi

printf "\n${COLOR_GREEN}Deploy package created at: %s${COLOR_RESET}\n\n" "$ARCHIVE_TAR"
