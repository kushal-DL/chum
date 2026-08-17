"""
OpenAI-compatible Whisper transcription API for Chum.

Implements POST /v1/audio/transcriptions matching the OpenAI Audio API,
backed by a locally fine-tuned whisper-large-v3-turbo model.

Device selection (automatic):
  1. DirectML (AMD GPU via torch-directml) — tried first, fp32 for stability
  2. CPU — fallback if DirectML is unavailable or produces TDR-corrupted output

TDR detection: if the DirectML encoder silently zeroes out (Windows TDR),
the decoder produces degenerate output (all-same tokens). We detect this
per-chunk and retry on CPU; after 3 consecutive strikes we switch permanently.
"""

import io
import os
import threading
import time
from pathlib import Path

import librosa
import numpy as np
import soundfile as sf
import torch
from fastapi import FastAPI, Form, Header, HTTPException, UploadFile
from fastapi.responses import JSONResponse, PlainTextResponse
from transformers import WhisperForConditionalGeneration, WhisperProcessor

MODEL_ID = r"F:\repos\chum\models\whisper-large-v3-turbo-tech"
API_KEY = ""  # Auth disabled — server binds to 127.0.0.1 only

# Config files sit at <repo_root>/config/whisper/ — resolve relative to this script
_REPO_ROOT   = Path(__file__).parent.parent
_PROMPT_FILE = _REPO_ROOT / "config" / "whisper" / "initial-prompt.txt"
_HOTWORDS_FILE = _REPO_ROOT / "config" / "whisper" / "hotwords.txt"

# --- Device selection ---
try:
    import torch_directml
    _DML_DEVICE = torch_directml.device()
    _DML_AVAILABLE = True
    print("torch-directml found — will attempt GPU inference (fp32)")
except Exception as _dml_err:
    _DML_DEVICE = None
    _DML_AVAILABLE = False
    print(f"torch-directml not available ({_dml_err}) — CPU only")

_CPU_DEVICE = torch.device("cpu")

app = FastAPI(title="Local Whisper API (OpenAI-compatible)")

_lock = threading.Lock()
_state: dict = {}
_tdr_strikes = 0        # consecutive degenerate-output events on DirectML
_MAX_TDR_STRIKES = 3   # switch to CPU permanently after this many


@app.on_event("startup")
def load_model():
    global _tdr_strikes
    _tdr_strikes = 0

    device = _DML_DEVICE if _DML_AVAILABLE else _CPU_DEVICE
    device_label = f"DirectML ({_DML_DEVICE})" if _DML_AVAILABLE else "CPU"

    print(f"Loading {MODEL_ID} on {device_label} ...")
    # low_cpu_mem_usage streams weights to device without keeping a full CPU copy.
    # fp16 halves VRAM vs fp32 (~1.6 GB instead of ~3.2 GB). If DirectML produces
    # TDR/degenerate output on fp16, the per-chunk fallback retries on CPU (fp32).
    model = WhisperForConditionalGeneration.from_pretrained(
        MODEL_ID, torch_dtype=torch.float16, low_cpu_mem_usage=True)
    model = model.to(device).eval()
    processor = WhisperProcessor.from_pretrained(MODEL_ID)

    default_prompt = ""
    if _PROMPT_FILE.exists():
        default_prompt = _PROMPT_FILE.read_text(encoding="utf-8").strip()
        print(f"Loaded initial prompt ({len(default_prompt)} chars)")
    else:
        print(f"No initial prompt at {_PROMPT_FILE}")

    hotwords: list[str] = []
    if _HOTWORDS_FILE.exists():
        hotwords = [w.strip() for w in _HOTWORDS_FILE.read_text(encoding="utf-8").strip().split(";") if w.strip()]
        print(f"Loaded {len(hotwords)} hotwords")
    else:
        print(f"No hotwords file at {_HOTWORDS_FILE}")

    _state["processor"]      = processor
    _state["model"]          = model
    _state["device"]         = device
    _state["default_prompt"] = default_prompt
    _state["hotwords"]       = hotwords
    print(f"Model ready ({device_label}).")


def _check_auth(authorization: str | None):
    if not API_KEY:
        return
    if authorization != f"Bearer {API_KEY}":
        raise HTTPException(status_code=401, detail={"error": {"message": "Invalid API key", "type": "invalid_request_error"}})


def _load_audio(raw_bytes: bytes) -> tuple[np.ndarray, int]:
    """Decode arbitrary audio bytes to a 16kHz mono float32 array."""
    try:
        audio, sr = sf.read(io.BytesIO(raw_bytes), dtype="float32")
    except Exception:
        from pydub import AudioSegment
        seg = AudioSegment.from_file(io.BytesIO(raw_bytes)).set_channels(1).set_frame_rate(16000)
        samples = np.array(seg.get_array_of_samples()).astype(np.float32) / (1 << (8 * seg.sample_width - 1))
        return samples, 16000

    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if sr != 16000:
        audio = librosa.resample(audio, orig_sr=sr, target_sr=16000)
        sr = 16000
    return audio, sr


_CHUNK_SAMPLES = 16000 * 30  # Whisper's native 30-second window


def _normalize_audio(audio: np.ndarray, target_rms: float = 0.1) -> np.ndarray:
    """Scale audio so RMS hits target_rms. Whisper hallucinates on quiet input."""
    rms = float(np.sqrt(np.mean(audio ** 2)))
    if rms < 1e-6:
        return audio
    return audio * (target_rms / rms)


def _is_degenerate(generated_ids: torch.Tensor) -> bool:
    """
    Detect TDR-corrupted output: when the DirectML encoder silently zeroes out,
    the decoder emits only padding/EOS tokens — all identical or fewer than 3 tokens.
    """
    if generated_ids.shape[1] <= 3:
        return True
    # All generated IDs are the same value (e.g. all pad_token_id)
    first = generated_ids[:, 0:1]
    return bool((generated_ids == first).all())


def _transcribe_chunk(audio_chunk: np.ndarray, generate_kwargs: dict) -> str:
    """Transcribe a single ≤30s audio chunk, with DirectML→CPU TDR fallback."""
    global _tdr_strikes

    processor = _state["processor"]
    model     = _state["model"]
    device    = _state["device"]

    inputs = processor(audio_chunk, sampling_rate=16000, return_tensors="pt")

    def _run_on(dev):
        feats = inputs.input_features.to(dev)
        # Move any tensor kwargs (e.g. prompt_ids) to the same device
        kw = {k: (v.to(dev) if isinstance(v, torch.Tensor) else v)
              for k, v in generate_kwargs.items()}
        with _lock, torch.no_grad():
            ids = model.generate(feats, **kw)
        return ids.cpu()

    generated_ids = _run_on(device)

    # TDR detection: degenerate output on a non-CPU device → retry on CPU
    if device != _CPU_DEVICE and _is_degenerate(generated_ids):
        _tdr_strikes += 1
        print(f"[WARN] Possible GPU TDR (strike {_tdr_strikes}/{_MAX_TDR_STRIKES}) — retrying chunk on CPU", flush=True)

        if _tdr_strikes >= _MAX_TDR_STRIKES:
            print("[WARN] Switching to CPU permanently after repeated TDR", flush=True)
            model.to(_CPU_DEVICE)
            _state["device"] = _CPU_DEVICE
            _state["model"]  = model

        generated_ids = _run_on(_CPU_DEVICE)
    elif device != _CPU_DEVICE:
        # Successful GPU inference — slowly bleed strike counter back down
        _tdr_strikes = max(0, _tdr_strikes - 1)

    return processor.batch_decode(generated_ids, skip_special_tokens=True)[0].strip()


def _transcribe(audio: np.ndarray, language: str | None, prompt: str | None) -> tuple[str, float]:
    processor = _state["processor"]
    duration  = len(audio) / 16000.0
    rms_in    = float(np.sqrt(np.mean(audio ** 2)))

    audio = _normalize_audio(audio)
    device_label = "GPU" if _state["device"] != _CPU_DEVICE else "CPU"
    print(f"[STT] {duration:.2f}s  rms_in={rms_in:.4f}  device={device_label}", flush=True)

    base_kwargs: dict = {
        "language": language or "english",
        "condition_on_prev_tokens": False,
        # Explicit temperature required: _retrieve_avg_logprobs crashes with TypeError
        # if generation_config.temperature is None (happens when not passed explicitly).
        "temperature": 0.0,
        # Filter fan-noise / silence chunks Whisper isn't confident are speech (default 0.6).
        "no_speech_threshold": 0.4,
        # Catch repetition loops early — triggers fallback when gzip ratio > threshold (default 2.4).
        "compression_ratio_threshold": 1.8,
        # Discard low-confidence transcriptions (default -1.0).
        "logprob_threshold": -1.2,
    }

    # Effective prompt: request takes priority, else use default from config file.
    # Whisper's decoder prompt is hard-capped at 223 tokens — truncate if needed.
    effective_prompt = prompt if prompt else _state.get("default_prompt", "")

    def _make_prompt_ids(text: str) -> torch.Tensor:
        raw_ids = processor.tokenizer.encode(text, add_special_tokens=False)
        if len(raw_ids) > 223:
            raw_ids = raw_ids[:223]
            text = processor.tokenizer.decode(raw_ids, skip_special_tokens=True)
        # Return on CPU — _transcribe_chunk moves it to the active device
        return processor.get_prompt_ids(text, return_tensors="pt")

    if effective_prompt:
        base_kwargs["prompt_ids"] = _make_prompt_ids(effective_prompt)

    chunks = [audio[i:i + _CHUNK_SAMPLES] for i in range(0, len(audio), _CHUNK_SAMPLES)]
    parts: list[str] = []
    context_prompt = effective_prompt

    for chunk in chunks:
        kw = dict(base_kwargs)
        if context_prompt:
            kw["prompt_ids"] = _make_prompt_ids(context_prompt)
        chunk_text = _transcribe_chunk(chunk, kw)
        parts.append(chunk_text)
        context_prompt = chunk_text[-200:] if chunk_text else context_prompt

    text = " ".join(parts)
    return text, duration


@app.get("/health")
def health():
    device_label = "GPU (DirectML)" if _state.get("device") != _CPU_DEVICE else "CPU"
    return {"status": "ok", "model": MODEL_ID, "device": device_label}


@app.get("/v1/models")
def list_models(authorization: str | None = Header(default=None)):
    _check_auth(authorization)
    return {
        "object": "list",
        "data": [{"id": MODEL_ID, "object": "model", "owned_by": "local"}],
    }


@app.post("/v1/audio/transcriptions")
async def create_transcription(
    file: UploadFile,
    model: str = Form(default=MODEL_ID),
    language: str | None = Form(default=None),
    prompt: str | None = Form(default=None),
    response_format: str = Form(default="json"),
    temperature: float = Form(default=0.0),
    authorization: str | None = Header(default=None),
):
    _check_auth(authorization)

    raw_bytes = await file.read()
    try:
        audio, _ = _load_audio(raw_bytes)
    except Exception as e:
        raise HTTPException(status_code=400, detail={"error": {"message": f"Could not decode audio: {e}", "type": "invalid_request_error"}})

    if len(audio) == 0:
        raise HTTPException(status_code=400, detail={"error": {"message": "Empty audio", "type": "invalid_request_error"}})

    t0 = time.time()
    text, duration = _transcribe(audio, language, prompt)
    elapsed = time.time() - t0

    if response_format == "text":
        return PlainTextResponse(text)

    if response_format == "verbose_json":
        return JSONResponse({
            "task": "transcribe",
            "language": language or "english",
            "duration": duration,
            "text": text,
            "segments": [{"id": 0, "start": 0.0, "end": duration, "text": text}],
        })

    return JSONResponse({"text": text})
