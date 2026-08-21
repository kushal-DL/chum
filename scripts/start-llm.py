"""
Launches llama-server with Qwen 3.5 9B, Vulkan GPU, thinking mode OFF.
Includes --mmproj so the same server handles both text and image queries
(Chum sends both text and screen-capture images to LlmApiBaseUrl on port 8001).

Called by start-llm-api.ps1, which handles download/verify and the info banner
then delegates here so Python subprocess handles Windows argument quoting correctly
(PowerShell 5.1 mangles JSON strings containing { } passed to native exes).

Usage: python start-llm.py [--port N] [--api-key KEY | --no-auth] [--thinking]
"""
import argparse
import subprocess
import sys
from pathlib import Path

ROOT   = Path(__file__).parent.parent / "local-llm"
EXE    = ROOT / "llama.cpp" / "llama-server.exe"
_DEFAULT_MODEL  = ROOT / "models" / "Qwen_Qwen3.5-9B-Q4_K_M.gguf"
_DEFAULT_MMPROJ = ROOT / "models" / "mmproj-Qwen_Qwen3.5-9B-f16.gguf"

parser = argparse.ArgumentParser()
parser.add_argument("--host",         default="0.0.0.0")
parser.add_argument("--port",         type=int, default=8001)
parser.add_argument("--api-key",      default="chum-llm-key-2026")
parser.add_argument("--no-auth",      action="store_true")
parser.add_argument("--thinking",     action="store_true")
parser.add_argument("--model-path",   default=str(_DEFAULT_MODEL))
parser.add_argument("--no-mmproj",    action="store_true")
parser.add_argument("--mmproj-path",  default=str(_DEFAULT_MMPROJ))
parser.add_argument("--context-size", type=int, default=8192)
args = parser.parse_args()

cmd = [
    str(EXE),
    "-m",    args.model_path,
    "--host", args.host,
    "--port", str(args.port),
    "-ngl",  "999",
    "-c",    str(args.context_size),
    "--jinja",
]
if not args.no_mmproj:
    cmd += ["--mmproj", args.mmproj_path]
if not args.thinking:
    cmd += ["--chat-template-kwargs", '{"enable_thinking":false}']
if not args.no_auth:
    cmd += ["--api-key", args.api_key]

print(f"Starting llama-server (thinking={'ON' if args.thinking else 'OFF'}, vision=ON) ...")
proc = subprocess.run(cmd)
sys.exit(proc.returncode)
