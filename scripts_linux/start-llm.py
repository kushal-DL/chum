#!/usr/bin/env python3
"""
Launches llama-server with Qwen models, GPU/CPU acceleration, thinking mode option.
Includes --mmproj when specified so the same server handles both text and image queries.
Cross-platform support: auto-detects llama-server (Linux) or llama-server.exe (Windows).

Called by start-llm-api.sh and start-fast-llm-api.sh.
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "local-llm"
_DEFAULT_MODEL = ROOT / "models" / "Qwen_Qwen3.5-9B-Q4_K_M.gguf"
_DEFAULT_MMPROJ = ROOT / "models" / "mmproj-Qwen_Qwen3.5-9B-f16.gguf"

parser = argparse.ArgumentParser(description="Launch llama-server for Chum")
parser.add_argument("--host", default="0.0.0.0", help="Host to bind to")
parser.add_argument("--port", type=int, default=8001, help="Port to listen on")
parser.add_argument("--api-key", default="llm", help="API Key")
parser.add_argument("--no-auth", action="store_true", help="Disable API key auth")
parser.add_argument("--thinking", action="store_true", help="Enable reasoning/thinking mode")
parser.add_argument("--model-path", default=str(_DEFAULT_MODEL), help="Path to GGUF model")
parser.add_argument("--no-mmproj", action="store_true", help="Disable multimodal projection (vision)")
parser.add_argument("--mmproj-path", default=str(_DEFAULT_MMPROJ), help="Path to mmproj GGUF")
parser.add_argument("--context-size", type=int, default=8192, help="Context size in tokens")
parser.add_argument("--gpu-layers", type=int, default=999, help="Number of GPU layers to offload")
parser.add_argument("--tools-dir", default=str(ROOT / "llama.cpp"), help="Directory containing llama-server")
parser.add_argument("--dry-run", action="store_true", help="Print command without starting server")
args = parser.parse_args()

# Locate llama-server binary
tools_path = Path(args.tools_dir)
candidates = [
    tools_path / "llama-server",
    tools_path / "llama-server.exe",
    tools_path / "bin" / "llama-server",
    tools_path / "bin" / "llama-server.exe",
]

server_exe = None
for cand in candidates:
    if cand.is_file() and (os.access(cand, os.X_OK) or sys.platform == "win32"):
        server_exe = cand
        break

if not server_exe:
    # Try finding in system PATH
    found = shutil.which("llama-server") or shutil.which("llama-server.exe")
    if found:
        server_exe = Path(found)

if not server_exe and not args.dry_run:
    print(f"ERROR: llama-server executable not found in {tools_path} or PATH.", file=sys.stderr)
    print("Run the setup script or install llama.cpp first.", file=sys.stderr)
    sys.exit(1)

exe_str = str(server_exe) if server_exe else "llama-server"

cmd = [
    exe_str,
    "-m", args.model_path,
    "--host", args.host,
    "--port", str(args.port),
    "-ngl", str(args.gpu_layers),
    "-c", str(args.context_size),
    "--jinja",
]

if not args.no_mmproj and args.mmproj_path:
    cmd += ["--mmproj", args.mmproj_path]

if args.thinking:
    cmd += ["--reasoning", "on"]
else:
    cmd += ["--reasoning", "off"]

if not args.no_auth:
    cmd += ["--api-key", args.api_key]

print(f"Starting llama-server (thinking={'ON' if args.thinking else 'OFF'}, vision={'OFF' if args.no_mmproj else 'ON'}) ...")
print(f"Command: {' '.join(cmd)}")

if args.dry_run:
    print("[DRY-RUN] Command constructed successfully.")
    sys.exit(0)

try:
    proc = subprocess.run(cmd)
    sys.exit(proc.returncode)
except KeyboardInterrupt:
    print("\nllama-server stopped by user.")
    sys.exit(0)
except Exception as e:
    print(f"ERROR running llama-server: {e}", file=sys.stderr)
    sys.exit(1)
