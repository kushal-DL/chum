#!/usr/bin/env bash
# Starts whisper-server with domain-specific vocabulary priming.
# Run from WSL2 after completing the setup in the repo README.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WHISPER_BIN="${WHISPER_BIN:-$HOME/repos/whisper.cpp/build/bin/whisper-server}"
WHISPER_MODEL="${WHISPER_MODEL:-$HOME/models/whisper-large-v3-turbo/ggml-large-v3-turbo.bin}"
WHISPER_HOST="${WHISPER_HOST:-0.0.0.0}"
WHISPER_PORT="${WHISPER_PORT:-9090}"
WHISPER_THREADS="${WHISPER_THREADS:-4}"

PROMPT_FILE="$REPO_ROOT/config/whisper/initial-prompt.txt"
HOTWORDS_FILE="$REPO_ROOT/config/whisper/hotwords.txt"

if [[ ! -f "$WHISPER_BIN" ]]; then
  echo "ERROR: whisper-server not found at $WHISPER_BIN"
  echo "  Set WHISPER_BIN env var or build whisper.cpp first (see repo setup guide)."
  exit 1
fi

if [[ ! -f "$WHISPER_MODEL" ]]; then
  echo "ERROR: model not found at $WHISPER_MODEL"
  echo "  Set WHISPER_MODEL env var or download the model first."
  exit 1
fi

PROMPT=""
if [[ -f "$PROMPT_FILE" ]]; then
  PROMPT="$(tr -d '\n' < "$PROMPT_FILE")"
  echo "Loaded initial prompt from $PROMPT_FILE"
fi

HOTWORDS=""
if [[ -f "$HOTWORDS_FILE" ]]; then
  HOTWORDS="$(tr -d '\n' < "$HOTWORDS_FILE")"
  echo "Loaded hotwords from $HOTWORDS_FILE"
fi

echo "Starting whisper-server on ${WHISPER_HOST}:${WHISPER_PORT} ..."
echo "  Model:   $WHISPER_MODEL"
echo "  Threads: $WHISPER_THREADS"

exec "$WHISPER_BIN" \
  -m "$WHISPER_MODEL" \
  --host "$WHISPER_HOST" \
  --port "$WHISPER_PORT" \
  -t "$WHISPER_THREADS" \
  --language en \
  ${PROMPT:+--prompt "$PROMPT"} \
  ${HOTWORDS:+--hotwords "$HOTWORDS"}
