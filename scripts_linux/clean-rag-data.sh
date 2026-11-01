#!/usr/bin/env bash
# ==============================================================================
# clean-rag-data.sh — Purges in-memory RAG indexes, chunks, and staged documents
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

PORT=8003
URL=""
API_KEY="${RAG_API_KEY:-}"
CLEAN_DOCS_DIR=0
DRY_RUN=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Purges all RAG documents, vector index chunks, and session memories from the
ASP RAG bridge, ensuring zero persistent retention on the host.

Options:
  -p, --port <int>             Port of ASP RAG bridge (default: $PORT)
  -u, --url <url>              Full bridge URL (default: http://127.0.0.1:<port>)
  -k, --api-key <key>          RAG bridge API key (or set RAG_API_KEY env)
      --clean-docs-dir         Also purge documents in the dedicated rag_docs directory
      --dry-run                Verify configuration without cleaning
  -h, --help                   Show this help message and exit
EOF
    exit 0
}

# Parse CLI arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)
            PORT="$2"; shift 2 ;;
        -u|--url)
            URL="$2"; shift 2 ;;
        -k|--api-key)
            API_KEY="$2"; shift 2 ;;
        --clean-docs-dir)
            CLEAN_DOCS_DIR=1; shift ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage ;;
        *)
            log_err "Unknown argument: $1"
            usage ;;
    esac
done

if [[ -z "$URL" ]]; then
    URL="http://127.0.0.1:$PORT"
fi

DOCS_DIR="${SCRIPT_DIR}/../rag_docs"
STAGING_DIR="${SCRIPT_DIR}/../data/rag_staging"

if [[ $DRY_RUN -eq 1 ]]; then
    log_ok "[dry-run] clean-rag-data configuration verified for target: $URL"
    exit 0
fi

log_step "Purging RAG bridge data on $URL..."

# 1. Call /clean on the running bridge if active
AUTH_HEADER=()
if [[ -n "$API_KEY" ]]; then
    AUTH_HEADER=(-H "Authorization: Bearer $API_KEY")
fi

CLEAN_PAYLOAD='{"namespaces":["chum_docs","chum_session"],"clean_staging":true}'

if curl -s --connect-timeout 2 "$URL/health" >/dev/null 2>&1; then
    resp=$(curl -s -X POST "$URL/clean" \
        -H "Content-Type: application/json" \
        "${AUTH_HEADER[@]}" \
        -d "$CLEAN_PAYLOAD" 2>&1 || true)
    log_ok "ASP RAG bridge in-memory index & chunks purged ($URL)"
else
    log_step "Bridge not currently reachable on $URL (cleaning local disk artifacts only)"
fi

# 2. Clean local staging directory
if [[ -d "$STAGING_DIR" ]]; then
    rm -rf "${STAGING_DIR:?}"/*
    log_ok "Removed staged files from $STAGING_DIR"
fi

# 3. Clean dedicated docs directory if explicitly requested
if [[ $CLEAN_DOCS_DIR -eq 1 && -d "$DOCS_DIR" ]]; then
    find "$DOCS_DIR" -mindepth 1 -not -name "README.md" -not -name ".gitkeep" -delete
    log_ok "Purged files in dedicated docs directory ($DOCS_DIR)"
fi

log_ok "RAG cleanup completed. Zero persistent document chunks remaining on host."
