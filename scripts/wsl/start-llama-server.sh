#!/usr/bin/env bash
# Starts llama-server with an OpenAI-compatible API on localhost:11434.
# Run from WSL2 after completing the setup in the repo README.
set -euo pipefail

LLAMA_BIN="${LLAMA_BIN:-$HOME/repos/llama.cpp/build/bin/llama-server}"
LLAMA_MODEL="${LLAMA_MODEL:-$HOME/models/mistral-7b/Mistral-7B-Instruct-v0.3-Q4_K_M.gguf}"
LLAMA_HOST="${LLAMA_HOST:-0.0.0.0}"
LLAMA_PORT="${LLAMA_PORT:-11434}"
LLAMA_CTX="${LLAMA_CTX:-8192}"
LLAMA_GPU_LAYERS="${LLAMA_GPU_LAYERS:-99}"

if [[ ! -f "$LLAMA_BIN" ]]; then
  echo "ERROR: llama-server not found at $LLAMA_BIN"
  echo "  Set LLAMA_BIN env var or build llama.cpp first (see repo setup guide)."
  exit 1
fi

if [[ ! -f "$LLAMA_MODEL" ]]; then
  echo "ERROR: model not found at $LLAMA_MODEL"
  echo "  Set LLAMA_MODEL env var or download the model first."
  exit 1
fi

echo "Starting llama-server on ${LLAMA_HOST}:${LLAMA_PORT} ..."
echo "  Model:      $LLAMA_MODEL"
echo "  GPU layers: $LLAMA_GPU_LAYERS"
echo "  Context:    $LLAMA_CTX tokens"

exec "$LLAMA_BIN" \
  -m "$LLAMA_MODEL" \
  -ngl "$LLAMA_GPU_LAYERS" \
  --host "$LLAMA_HOST" \
  --port "$LLAMA_PORT" \
  -c "$LLAMA_CTX" \
  --log-disable
