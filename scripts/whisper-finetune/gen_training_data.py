"""
Generate synthetic TTS training data for Whisper fine-tuning.

Produces WAV files + a metadata.jsonl with (audio_path, transcript) pairs.
Uses edge-tts with multiple voices for acoustic diversity.

Output: scripts/whisper-finetune/training_data/
"""

import asyncio
import json
import os
import random
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent
VOCAB_FILE = SCRIPT_DIR / "vocab.json"
OUT_DIR = SCRIPT_DIR / "training_data"
METADATA_FILE = OUT_DIR / "metadata.jsonl"

VOICES = [
    "en-US-GuyNeural",
    "en-US-JennyNeural",
    "en-GB-SoniaNeural",
]

# Sentence templates — {term} is replaced with the technical term
TEMPLATES = [
    "We are using {term} in our production environment.",
    "The team recently deployed {term} to handle our workloads.",
    "Our architecture relies on {term} for scalability.",
    "We need to configure {term} properly for this use case.",
    "The engineering team is evaluating {term} for the new project.",
    "{term} provides the foundation for our cloud infrastructure.",
    "Monitoring {term} metrics is critical for reliability.",
    "We decided to migrate our workloads to {term}.",
    "The pipeline uses {term} to process data in real time.",
    "Our team has extensive experience with {term}.",
    "We integrated {term} into our existing workflow last quarter.",
    "Performance testing shows {term} handles the load effectively.",
    "The documentation for {term} covers the key configuration options.",
    "We use {term} alongside our existing tooling.",
    "Troubleshooting {term} requires understanding the underlying architecture.",
    "Our data platform is built on {term} for scalable processing.",
    "We trained the model using {term} as the orchestration layer.",
    "The {term} cluster is running across multiple availability zones.",
    "Security policies for {term} are managed through role-based access control.",
    "We are rolling out {term} as part of our platform modernization effort.",
]

# Multi-term templates for richer context
MULTI_TEMPLATES = [
    "Our stack uses {t1} and {t2} for the data processing layer.",
    "We deployed {t1} on top of {t2} to handle the ingestion pipeline.",
    "{t1} integrates well with {t2} in our architecture.",
    "The team uses {t1} for orchestration and {t2} for model serving.",
    "We combine {t1} with {t2} to build scalable machine learning pipelines.",
    "Migrating from {t1} to {t2} improved our latency by thirty percent.",
    "Our observability stack includes {t1}, {t2}, and distributed tracing.",
    "We use {t1} for streaming and {t2} for batch workloads.",
]


def load_vocab():
    data = json.loads(VOCAB_FILE.read_text(encoding="utf-8"))
    all_terms = []
    for category, terms in data.items():
        all_terms.extend(terms)
    return data, all_terms


def build_sentences(vocab_data, all_terms, target=1200):
    """Build a list of (sentence, terms_used) tuples."""
    rng = random.Random(42)
    sentences = []

    # Single-term sentences — one per template per term (sub-sampled)
    for category, terms in vocab_data.items():
        for term in terms:
            # Pick 2-4 templates per term
            chosen = rng.sample(TEMPLATES, min(3, len(TEMPLATES)))
            for tmpl in chosen:
                sentences.append((tmpl.replace("{term}", term), [term]))

    # Multi-term sentences
    pairs_needed = target // 4
    term_list = list(all_terms)
    for _ in range(pairs_needed):
        t1, t2 = rng.sample(term_list, 2)
        tmpl = rng.choice(MULTI_TEMPLATES)
        s = tmpl.replace("{t1}", t1).replace("{t2}", t2)
        sentences.append((s, [t1, t2]))

    rng.shuffle(sentences)
    return sentences[:target]


async def tts_sentence(text: str, voice: str, out_wav: Path):
    """Run edge-tts for one sentence and convert to 16kHz mono WAV."""
    with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as f:
        tmp_mp3 = f.name
    try:
        proc = await asyncio.create_subprocess_exec(
            sys.executable, "-m", "edge_tts",
            "--voice", voice, "--text", text, "--write-media", tmp_mp3,
            stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL,
        )
        await proc.communicate()
        if proc.returncode != 0:
            return False
        # Convert to 16kHz mono WAV
        ffmpeg = await asyncio.create_subprocess_exec(
            "ffmpeg", "-y", "-i", tmp_mp3, "-ar", "16000", "-ac", "1", str(out_wav),
            stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL,
        )
        await ffmpeg.communicate()
        return ffmpeg.returncode == 0 and out_wav.exists() and out_wav.stat().st_size > 500
    finally:
        try:
            os.unlink(tmp_mp3)
        except OSError:
            pass


async def generate_all(sentences, voices, out_dir: Path):
    out_dir.mkdir(parents=True, exist_ok=True)
    records = []
    total = len(sentences) * len(voices)
    done = 0

    # Semaphore to limit concurrent edge-tts processes
    sem = asyncio.Semaphore(4)

    async def do_one(idx, sentence, voice):
        nonlocal done
        voice_slug = voice.split("-")[1].lower() + "_" + voice.split("-")[2].lower()
        wav_name = f"{idx:05d}_{voice_slug}.wav"
        wav_path = out_dir / wav_name

        if wav_path.exists() and wav_path.stat().st_size > 500:
            async with sem:
                pass
        else:
            async with sem:
                ok = await tts_sentence(sentence, voice, wav_path)
            if not ok:
                return None

        return {"audio_path": str(wav_path), "transcript": sentence}

    tasks = []
    for idx, (sentence, _terms) in enumerate(sentences):
        for voice in voices:
            tasks.append(do_one(idx, sentence, voice))

    for coro in asyncio.as_completed(tasks):
        result = await coro
        done += 1
        if result:
            records.append(result)
        if done % 100 == 0 or done == total:
            print(f"  {done}/{total} generated  ({len(records)} OK)", flush=True)

    return records


def main():
    print("=== Whisper Training Data Generator ===")
    print(f"Vocab: {VOCAB_FILE}")
    print(f"Output: {OUT_DIR}\n")

    vocab_data, all_terms = load_vocab()
    total_terms = len(all_terms)
    print(f"Vocabulary: {total_terms} terms across {len(vocab_data)} categories")

    sentences = build_sentences(vocab_data, all_terms, target=500)
    print(f"Sentences planned: {len(sentences)}")
    print(f"TTS voices: {len(VOICES)}")
    print(f"Total audio files to generate: {len(sentences) * len(VOICES)}\n")

    # Verify edge-tts is available (invoked as python -m edge_tts on Windows)
    result = subprocess.run([sys.executable, "-m", "edge_tts", "--version"],
                            capture_output=True)
    if result.returncode != 0:
        subprocess.run([sys.executable, "-m", "pip", "install", "edge-tts", "-q"])

    # Verify ffmpeg is available
    result = subprocess.run(["ffmpeg", "-version"], capture_output=True)
    if result.returncode != 0:
        print("ERROR: ffmpeg not found. Install it and ensure it's on PATH.")
        sys.exit(1)

    print("Generating TTS audio (this takes ~10-20 minutes)...")
    records = asyncio.run(generate_all(sentences, VOICES, OUT_DIR))

    # Shuffle and write metadata
    random.Random(99).shuffle(records)
    METADATA_FILE.write_text(
        "\n".join(json.dumps(r, ensure_ascii=False) for r in records),
        encoding="utf-8"
    )

    print(f"\nDone: {len(records)} audio files → {METADATA_FILE}")
    print(f"Training samples available: {len(records)}")

    # Print 3 sample records
    print("\nSample records:")
    for r in records[:3]:
        print(f"  {Path(r['audio_path']).name}: {r['transcript'][:80]}")


if __name__ == "__main__":
    main()
