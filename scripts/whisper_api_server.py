"""
OpenAI-compatible Whisper transcription API for Chum.

Implements POST /v1/audio/transcriptions matching the OpenAI Audio API
(https://platform.openai.com/docs/api-reference/audio/createTranscription),
backed by openai/whisper-large-v3-turbo running on an AMD GPU via DirectML.

Architecture note: the encoder runs on the GPU (fp16) where nearly all the
compute is (32 encoder layers vs 4 decoder layers for the turbo model), and
the decoder generation loop runs on CPU. This works around a DirectML bug
where the autoregressive decoder's math diverges from CPU after ~7 tokens,
collapsing into repetition loops / "!!!!" garbage. See transcribe.py for the
original diagnosis.
"""

import copy
import io
import os
import secrets
import threading
import time
from pathlib import Path

import librosa
import numpy as np
import soundfile as sf
import torch
import torch_directml
from fastapi import FastAPI, Form, Header, HTTPException, UploadFile
from fastapi.responses import JSONResponse, PlainTextResponse
from transformers import WhisperForConditionalGeneration, WhisperProcessor

MODEL_ID = r"F:\repos\chum\models\whisper-large-v3-turbo-tech"
API_KEY = os.environ.get("WHISPER_API_KEY", "").strip()

# Config files sit at <repo_root>/config/whisper/ — resolve relative to this script
_REPO_ROOT   = Path(__file__).parent.parent
_PROMPT_FILE = _REPO_ROOT / "config" / "whisper" / "initial-prompt.txt"
_HOTWORDS_FILE = _REPO_ROOT / "config" / "whisper" / "hotwords.txt"

app = FastAPI(title="Local Whisper API (OpenAI-compatible)")

_lock = threading.Lock()
_state = {}


@app.on_event("startup")
def load_model():
    print(f"Loading {MODEL_ID} ...")
    dml_device = torch_directml.device()

    model_cpu = WhisperForConditionalGeneration.from_pretrained(MODEL_ID, torch_dtype=torch.float32)
    model_cpu.eval()

    encoder_gpu = copy.deepcopy(model_cpu.model.encoder).half().to(dml_device)
    encoder_gpu.eval()

    processor = WhisperProcessor.from_pretrained(MODEL_ID)

    # Load domain vocabulary from config files — applied automatically to every request
    default_prompt = ""
    if _PROMPT_FILE.exists():
        default_prompt = _PROMPT_FILE.read_text(encoding="utf-8").strip()
        print(f"Loaded initial prompt ({len(default_prompt)} chars, {_PROMPT_FILE.name})")
    else:
        print(f"No initial prompt found at {_PROMPT_FILE}")

    hotwords: list[str] = []
    if _HOTWORDS_FILE.exists():
        hotwords = [w.strip() for w in _HOTWORDS_FILE.read_text(encoding="utf-8").strip().split(";") if w.strip()]
        print(f"Loaded {len(hotwords)} hotwords ({_HOTWORDS_FILE.name})")
    else:
        print(f"No hotwords file found at {_HOTWORDS_FILE}")

    _state["processor"]      = processor
    _state["model_cpu"]      = model_cpu
    _state["encoder_gpu"]    = encoder_gpu
    _state["dml_device"]     = dml_device
    _state["default_prompt"] = default_prompt
    _state["hotwords"]       = hotwords
    print(f"Model ready on {dml_device} (encoder) / cpu (decoder).")


def _check_auth(authorization: str | None):
    if not API_KEY:
        return  # no key configured -> auth disabled (localhost only)
    if authorization != f"Bearer {API_KEY}":
        raise HTTPException(status_code=401, detail={"error": {"message": "Invalid API key", "type": "invalid_request_error"}})


def _load_audio(raw_bytes: bytes) -> tuple[np.ndarray, int]:
    """Decode arbitrary audio bytes to a 16kHz mono float32 array."""
    try:
        audio, sr = sf.read(io.BytesIO(raw_bytes), dtype="float32")
    except Exception:
        # Fall back to ffmpeg via pydub for formats libsndfile can't read (mp3, m4a, webm, ...)
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


def _transcribe_chunk(audio_chunk: np.ndarray, generate_kwargs: dict) -> str:
    """Transcribe a single ≤30s audio chunk. Called with _lock already released."""
    processor   = _state["processor"]
    model_cpu   = _state["model_cpu"]
    encoder_gpu = _state["encoder_gpu"]
    dml_device  = _state["dml_device"]

    inputs = processor(audio_chunk, sampling_rate=16000, return_tensors="pt")
    input_features_gpu = inputs.input_features.to(device=dml_device, dtype=torch.float16)

    with _lock, torch.no_grad():
        encoder_outputs = encoder_gpu(input_features_gpu)
        encoder_outputs.last_hidden_state = encoder_outputs.last_hidden_state.to("cpu").float()
        generated_ids = model_cpu.generate(encoder_outputs=encoder_outputs, **generate_kwargs)

    return processor.batch_decode(generated_ids, skip_special_tokens=True)[0].strip()


def _transcribe(audio: np.ndarray, language: str | None, prompt: str | None) -> tuple[str, float]:
    processor = _state["processor"]
    duration  = len(audio) / 16000.0

    # Build the base generate kwargs
    base_kwargs: dict = {"condition_on_prev_tokens": False}
    if language:
        base_kwargs["language"] = language

    # Effective prompt: request takes priority, else use default from config file.
    # Whisper's decoder prompt is hard-capped at 223 tokens — truncate if needed.
    effective_prompt = prompt if prompt else _state.get("default_prompt", "")

    def _make_prompt_ids(text: str):
        raw_ids = processor.tokenizer.encode(text, add_special_tokens=False)
        if len(raw_ids) > 223:
            raw_ids = raw_ids[:223]
            text = processor.tokenizer.decode(raw_ids, skip_special_tokens=True)
        return processor.get_prompt_ids(text, return_tensors="pt")

    if effective_prompt:
        base_kwargs["prompt_ids"] = _make_prompt_ids(effective_prompt)

    # Split audio into 30s chunks; feed previous chunk's transcript back as
    # rolling context so terminology carries across chunk boundaries.
    chunks = [audio[i:i + _CHUNK_SAMPLES] for i in range(0, len(audio), _CHUNK_SAMPLES)]
    parts: list[str] = []
    context_prompt = effective_prompt

    for chunk in chunks:
        kw = dict(base_kwargs)
        if context_prompt:
            kw["prompt_ids"] = _make_prompt_ids(context_prompt)
        chunk_text = _transcribe_chunk(chunk, kw)
        parts.append(chunk_text)
        # roll the last ~200 chars as context for the next chunk
        context_prompt = chunk_text[-200:] if chunk_text else context_prompt

    text = " ".join(parts)
    return text, duration


@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_ID}


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
        return JSONResponse(
            {
                "task": "transcribe",
                "language": language or "english",
                "duration": duration,
                "text": text,
                # Best-effort: one segment spanning the whole clip. This server does not
                # compute real per-segment timestamps.
                "segments": [
                    {
                        "id": 0,
                        "start": 0.0,
                        "end": duration,
                        "text": text,
                    }
                ],
            }
        )

    # default: "json"
    return JSONResponse({"text": text})
