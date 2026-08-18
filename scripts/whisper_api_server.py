"""
OpenAI-compatible Whisper transcription API for Chum.

Implements POST /v1/audio/transcriptions matching the OpenAI Audio API,
backed by a locally fine-tuned whisper-large-v3-turbo model.

Requires torch-directml (AMD GPU). Runs fp16 only. Fails at startup if
DirectML is unavailable — CPU inference is not supported.
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
    print("torch-directml found — GPU inference (fp16)")
except Exception as _dml_err:
    raise RuntimeError(f"torch-directml required but not available: {_dml_err}") from _dml_err

app = FastAPI(title="Local Whisper API (OpenAI-compatible)")

_lock = threading.Lock()
_state: dict = {}


@app.on_event("startup")
def load_model():
    print(f"Loading {MODEL_ID} on DirectML (fp16) ...")
    model = WhisperForConditionalGeneration.from_pretrained(
        MODEL_ID, torch_dtype=torch.float16, low_cpu_mem_usage=True)
    model = model.to(_DML_DEVICE).eval()
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
    _state["default_prompt"] = default_prompt
    _state["hotwords"]       = hotwords
    print("Model ready (DirectML fp16).")


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


def _transcribe_chunk(audio_chunk: np.ndarray, generate_kwargs: dict) -> str:
    """Transcribe a single ≤30s audio chunk on DirectML (fp16)."""
    processor = _state["processor"]
    model     = _state["model"]

    inputs = processor(audio_chunk, sampling_rate=16000, return_tensors="pt")
    feats = inputs.input_features.to(_DML_DEVICE, dtype=torch.float16)

    def _move(v):
        # Only float tensors (audio features) get cast to fp16 to match the model.
        # Integer tensors (prompt_ids = token indices for embedding lookup) MUST keep
        # their integer dtype — casting them to fp16 corrupts the embedding index and
        # DirectML raises "The parameter is incorrect".
        if not isinstance(v, torch.Tensor):
            return v
        if v.is_floating_point():
            return v.to(_DML_DEVICE, dtype=torch.float16)
        return v.to(_DML_DEVICE)

    kw = {k: _move(v) for k, v in generate_kwargs.items()}

    # Whisper's decoder is capped at 448 positions total (max_target_positions).
    # decoder_input_ids = special_start_tokens (~4) + prompt_tokens + generated_tokens.
    # Compute remaining headroom so prompt + output never exceeds 448.
    prompt_len = int(kw["prompt_ids"].shape[-1]) if "prompt_ids" in kw else 0
    kw["max_new_tokens"] = max(50, 448 - prompt_len - 8)

    with _lock, torch.no_grad():
        generated_ids = model.generate(feats, **kw).cpu()

    text = processor.batch_decode(generated_ids, skip_special_tokens=True)[0].strip()
    print(f"[STT] → {repr(text)}", flush=True)
    return text


def _transcribe(audio: np.ndarray, language: str | None, prompt: str | None) -> tuple[str, float]:
    processor = _state["processor"]
    duration  = len(audio) / 16000.0
    rms_in    = float(np.sqrt(np.mean(audio ** 2)))

    audio = _normalize_audio(audio)
    print(f"[STT] {duration:.2f}s  rms_in={rms_in:.4f}  device=GPU(DML)", flush=True)

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
    return {"status": "ok", "model": MODEL_ID, "device": "GPU (DirectML fp16)"}


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
