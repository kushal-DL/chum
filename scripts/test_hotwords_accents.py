"""
test_hotwords_accents.py

Runs two experiments for every hotword in hotwords.txt:
  1. BASELINE  — whisper-large-v3 with NO prompt (pure model knowledge)
  2. PROMPTED  — whisper-large-v3 with our initial-prompt.txt active

Audio is synthesized in 7 accent groups using edge-tts (Microsoft Neural TTS):
  American male, American female, British, Indian, Australian,
  Singapore/Chinese-influenced, Irish.

The test generates 8 sentences that together cover all 66 hotwords, in all 7
voices, and transcribes each under both conditions — 2 × 7 × 8 = 112 API calls.

Outputs:
  - Per-hotword accuracy table: baseline vs prompted, per voice
  - Clear recommendation for each word: keep in prompt / already known / drop

Requirements:
  pip install edge-tts pydub requests
  ffmpeg in PATH
  Whisper v3 server running on http://127.0.0.1:8000
  Qwen3.5 server may remain on 8002 (not used here)

Usage:
  python scripts/test_hotwords_accents.py
  python scripts/test_hotwords_accents.py --skip-audio-gen   (reuse cached WAV files)
"""

import argparse
import asyncio
import io
import re
import subprocess
import sys
import time
import wave
from pathlib import Path

import edge_tts
import requests

# ── Config ─────────────────────────────────────────────────────────────────────
WHISPER_URL = "http://127.0.0.1:8000"
AUDIO_DIR   = Path(r"C:\Users\Doomlord\AppData\Local\Temp\whisper_accent_test")
REPO_ROOT   = Path(__file__).parent.parent

VOICES = [
    ("en-US-GuyNeural",     "American♂"),
    ("en-US-JennyNeural",   "American♀"),
    ("en-GB-SoniaNeural",   "British"),
    ("en-IN-NeerjaNeural",  "Indian"),
    ("en-AU-NatashaNeural", "Australian"),
    ("en-SG-WayneNeural",   "Singapore"),   # closest to Chinese-accented English in edge-tts
    ("en-IE-ConnorNeural",  "Irish"),
]

# 8 sentences, together covering all 66 hotwords from hotwords.txt
TEST_SENTENCES = [
    {
        "id":    "S1_k8s",
        "text":  (
            "Our Kubernetes platform uses KEDA for autoscaling, with Helm charts deployed via ArgoCD. "
            "We run Istio as the service mesh, Prometheus and Grafana for observability, "
            "and OpenTelemetry for distributed tracing."
        ),
        "words": ["Kubernetes", "KEDA", "autoscaling", "Helm", "ArgoCD",
                  "Istio", "Prometheus", "Grafana", "observability", "OpenTelemetry"],
    },
    {
        "id":    "S2_data",
        "text":  (
            "The Databricks Lakehouse uses Delta Lake tables with Unity Catalog, "
            "MLflow for experiment tracking, and OpenLineage for data lineage. "
            "We are migrating from Hudi to Iceberg table formats."
        ),
        "words": ["Databricks", "Lakehouse", "Delta Lake", "Unity Catalog",
                  "MLflow", "OpenLineage", "Hudi", "Iceberg"],
    },
    {
        "id":    "S3_stream",
        "text":  (
            "We process events with Apache Kafka, Kinesis streams, and AWS EventBridge. "
            "Flink handles real-time analytics with data landing in Redshift, Snowflake, and BigQuery. "
            "Terraform manages all infrastructure, and dbt handles transformations."
        ),
        "words": ["Kafka", "Kinesis", "EventBridge", "Flink", "Redshift",
                  "Snowflake", "BigQuery", "Terraform", "dbt"],
    },
    {
        "id":    "S4_llm",
        "text":  (
            "Our RAG pipeline uses LangChain and LangGraph for orchestration, "
            "LlamaIndex for document indexing, and Langfuse for tracing. "
            "Embeddings are stored in Pinecone, Weaviate, ChromaDB, and Milvus."
        ),
        "words": ["RAG", "LangChain", "LangGraph", "LlamaIndex", "Langfuse",
                  "Pinecone", "Weaviate", "ChromaDB", "Milvus"],
    },
    {
        "id":    "S5_models",
        "text":  (
            "We benchmarked OpenAI, Anthropic Claude, Gemini, and Mistral models. "
            "Teams applied LoRA fine-tuning via Hugging Face, tracked in MLOps and LLMOps dashboards "
            "with AutoML experiments registered in the FeatureStore."
        ),
        "words": ["OpenAI", "Anthropic", "Claude", "Gemini", "Mistral",
                  "LoRA", "Hugging Face", "MLOps", "LLMOps", "AutoML", "FeatureStore"],
    },
    {
        "id":    "S6_orch",
        "text":  (
            "Our agentic multiagent framework runs on Kubeflow with Airflow for scheduling. "
            "ETL pipelines handle DDL and DML migrations. "
            "Pub/Sub handles event routing across microservices with full ERP integration."
        ),
        "words": ["agentic", "multiagent", "Kubeflow", "Airflow", "ETL",
                  "DDL", "DML", "Pub/Sub", "microservices", "ERP"],
    },
    {
        "id":    "S7_biz",
        "text":  (
            "Accenture partners with Infosys and ZS Associates on Order-to-Cash and pharma analytics, "
            "using ERP systems with Blackline and CreditManager. "
            "We ensure traceability and orchestrator reliability across all processes."
        ),
        "words": ["Accenture", "Infosys", "ZS Associates", "Order-to-Cash", "pharma",
                  "Blackline", "CreditManager", "traceability", "orchestrator"],
    },
    {
        "id":    "S8_misc",
        "text":  (
            "AWS Step Functions orchestrate our microservices pipelines. "
            "WASAPI handles Windows audio loopback capture. "
            "OpenAI Whisper provides the transcription layer. "
            "The solution uses Pub/Sub for real-time event streaming."
        ),
        "words": ["Step Functions", "WASAPI", "Whisper"],
    },
]


# ── Word matching ───────────────────────────────────────────────────────────────
# Known phonetic variants Whisper commonly produces for these terms
_ALIASES = {
    "LangChain":      ["langchain", "lang chain", "lang-chain", "linechain", "lain chain"],
    "LangGraph":      ["langgraph", "lang graph", "lang-graph"],
    "LlamaIndex":     ["llamaindex", "llama index", "llama-index"],
    "Langfuse":       ["langfuse", "lang fuse"],
    "ChromaDB":       ["chromadb", "chroma db", "chroma-db"],
    "FeatureStore":   ["featurestore", "feature store", "feature-store"],
    "Kubeflow":       ["kubeflow", "kube flow", "kube-flow"],
    "OpenLineage":    ["openlineage", "open lineage", "open-lineage"],
    "OpenTelemetry":  ["opentelemetry", "open telemetry", "open-telemetry"],
    "Hugging Face":   ["hugging face", "huggingface", "hugging-face"],
    "Delta Lake":     ["delta lake", "deltalake", "delta-lake"],
    "Unity Catalog":  ["unity catalog", "unitycatalog"],
    "ZS Associates":  ["zs associates", "z s associates", "zee s associates"],
    "Order-to-Cash":  ["order-to-cash", "order to cash", "order2cash"],
    "Step Functions": ["step functions", "stepfunctions", "step function"],
    "Pub/Sub":        ["pub/sub", "pub sub", "pubsub", "pub-sub"],
    "MLflow":         ["mlflow", "ml flow", "ml-flow", "em el flow", "emiflow"],
    "MLOps":          ["mlops", "ml ops", "ml-ops"],
    "LLMOps":         ["llmops", "llm ops", "llm-ops"],
    "AutoML":         ["automl", "auto ml", "auto-ml"],
    "ArgoCD":         ["argocd", "argo cd", "argo-cd"],
    "dbt":            ["dbt", " dbt ", "d.b.t"],
    "RAG":            ["rag", " rag ", "r.a.g"],
    "KEDA":           ["keda", " keda "],
    "WASAPI":         ["wasapi", "waz api", "was api", "wah sapi"],
    "ERP":            ["erp", " erp "],
    "DDL":            ["ddl", " ddl "],
    "DML":            ["dml", " dml "],
    "ETL":            ["etl", " etl "],
}


def _check_word(word: str, transcript: str) -> bool:
    t   = transcript.lower()
    key = word.lower()
    if key in t:
        return True
    for alias in _ALIASES.get(word, []):
        if alias.lower() in t:
            return True
    return False


# ── Audio generation ────────────────────────────────────────────────────────────
async def _gen_mp3(text: str, voice: str, out_path: Path) -> None:
    communicate = edge_tts.Communicate(text, voice=voice, rate="-5%")
    await communicate.save(str(out_path))


def _mp3_to_wav(mp3: Path, wav: Path) -> bool:
    result = subprocess.run(
        ["ffmpeg", "-y", "-i", str(mp3), "-ar", "16000", "-ac", "1", "-f", "wav", str(wav)],
        capture_output=True,
    )
    return result.returncode == 0


def generate_all_audio(skip_existing: bool = True) -> dict[tuple[str, str], Path]:
    """Returns {(sentence_id, voice_id): wav_path}"""
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    results: dict[tuple[str, str], Path] = {}
    tasks: list[tuple] = []

    for s in TEST_SENTENCES:
        for voice_id, _label in VOICES:
            wav = AUDIO_DIR / f"{s['id']}__{voice_id}.wav"
            results[(s["id"], voice_id)] = wav
            if skip_existing and wav.exists():
                continue
            tasks.append((s["id"], s["text"], voice_id, wav))

    if tasks:
        print(f"\nGenerating {len(tasks)} audio files via edge-tts ...")

        async def _run_all():
            for sid, text, voice_id, wav in tasks:
                mp3 = wav.with_suffix(".mp3")
                try:
                    await _gen_mp3(text, voice_id, mp3)
                    ok = _mp3_to_wav(mp3, wav)
                    mp3.unlink(missing_ok=True)
                    status = "ok" if ok else "ffmpeg FAIL"
                except Exception as e:
                    status = f"ERR: {e}"
                print(f"  {voice_id:<28} {sid:<16} {status}")

        asyncio.run(_run_all())
    else:
        print(f"All {len(results)} audio files already cached.")

    return results


# ── Transcription ───────────────────────────────────────────────────────────────
def transcribe(wav_path: Path, no_prompt: bool) -> tuple[str, float]:
    with open(wav_path, "rb") as f:
        data = f.read()
    t0 = time.perf_counter()
    resp = requests.post(
        f"{WHISPER_URL}/v1/audio/transcriptions",
        files={"file": ("audio.wav", data, "audio/wav")},
        data={"model": "whisper-large-v3", "response_format": "json", "no_prompt": str(no_prompt).lower()},
        timeout=120,
    )
    elapsed = time.perf_counter() - t0
    resp.raise_for_status()
    return resp.json().get("text", ""), elapsed


# ── Reporting ───────────────────────────────────────────────────────────────────
def _bar(n: int, total: int, width: int = 7) -> str:
    filled = round(n / total * width)
    return "█" * filled + "░" * (width - filled)


def print_report(
    word_results: dict[str, dict[str, list[bool]]],
    voice_labels: list[str],
):
    n_voices = len(voice_labels)
    conds    = ["baseline", "prompted"]

    header = (
        f"\n{'Hotword':<22}  "
        f"{'Baseline':^16}  "
        f"{'Prompted':^16}  "
        f"Delta  Recommendation"
    )
    print(header)
    print("─" * 90)

    keepers, maybe_drop, drop = [], [], []

    for word in sorted(word_results.keys()):
        r = word_results[word]
        b_hits = sum(r.get("baseline", []))
        p_hits = sum(r.get("prompted", []))
        b_frac = b_hits / n_voices
        p_frac = p_hits / n_voices

        b_str = f"{b_hits}/{n_voices} {_bar(b_hits, n_voices)}"
        p_str = f"{p_hits}/{n_voices} {_bar(p_hits, n_voices)}"
        delta = p_hits - b_hits
        delta_str = f"{'+' if delta > 0 else ''}{delta:+d}"

        if b_hits >= n_voices - 1:
            rec = "drop (already known)"
            drop.append(word)
        elif delta >= 2 and p_hits >= n_voices - 2:
            rec = "KEEP (prompt helps a lot)"
            keepers.append(word)
        elif delta >= 1:
            rec = "keep (partial improvement)"
            maybe_drop.append(word)
        else:
            rec = "neutral / review"
            maybe_drop.append(word)

        print(f"  {word:<22}  {b_str:<16}  {p_str:<16}  {delta_str:<6}  {rec}")

    print("\n" + "=" * 90)
    print(f"  Words prompt helps significantly ({len(keepers)}): {', '.join(keepers) or 'none'}")
    print(f"  Words Whisper already knows ({len(drop)})  : {', '.join(drop) or 'none'}")
    print(f"  Marginal / review ({len(maybe_drop)})           : {', '.join(maybe_drop) or 'none'}")
    print("=" * 90)
    print("\nSUGGESTED action:")
    print(f"  Remove from initial-prompt.txt & hotwords.txt: {', '.join(drop)}")
    print(f"  Keep in prompt (prompt provides real benefit): {', '.join(keepers)}")


# ── Main ────────────────────────────────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-audio-gen", action="store_true",
                        help="Skip audio generation, reuse cached WAV files")
    parser.add_argument("--baseline-only", action="store_true",
                        help="Only run baseline (no-prompt) condition, skip prompted")
    args = parser.parse_args()

    # Health check
    try:
        r = requests.get(f"{WHISPER_URL}/health", timeout=5)
        model = r.json().get("model", "?")
        print(f"Whisper server OK — model: {model}")
    except Exception as e:
        print(f"ERROR: Whisper server not reachable at {WHISPER_URL}: {e}")
        sys.exit(1)

    voice_labels = [label for _, label in VOICES]

    # Generate audio
    audio_map = generate_all_audio(skip_existing=not args.skip_audio_gen)

    # Per-word results: word -> condition -> [hit per voice]
    word_results: dict[str, dict[str, list[bool]]] = {}
    for s in TEST_SENTENCES:
        for w in s["words"]:
            word_results.setdefault(w, {"baseline": [], "prompted": []})

    conditions = ["baseline"] if args.baseline_only else ["baseline", "prompted"]

    total_calls = len(TEST_SENTENCES) * len(VOICES) * len(conditions)
    call_n = 0

    for cond in conditions:
        no_prompt = (cond == "baseline")
        print(f"\n{'='*60}")
        print(f"  Condition: {cond.upper()} (no_prompt={no_prompt})")
        print(f"{'='*60}")
        rtfs = []

        for s in TEST_SENTENCES:
            print(f"\n  Sentence: {s['id']}")
            print(f"  Text    : {s['text'][:80]}...")
            print(f"  Words   : {', '.join(s['words'])}")
            print()

            for voice_id, label in VOICES:
                call_n += 1
                wav = audio_map.get((s["id"], voice_id))
                if not wav or not wav.exists():
                    print(f"    [{call_n:3d}/{total_calls}]  {label:<14}  SKIP (no audio)")
                    for w in s["words"]:
                        word_results[w][cond].append(False)
                    continue

                try:
                    text, elapsed = transcribe(wav, no_prompt)
                    dur_s = wav.stat().st_size / (16000 * 2)
                    rtf = elapsed / dur_s if dur_s > 0 else 0
                    rtfs.append(rtf)

                    hits   = [w for w in s["words"] if _check_word(w, text)]
                    misses = [w for w in s["words"] if not _check_word(w, text)]
                    for w in s["words"]:
                        word_results[w][cond].append(_check_word(w, text))

                    hit_str  = ", ".join(hits)   or "(none)"
                    miss_str = ", ".join(misses) or "(none)"
                    print(f"    [{call_n:3d}/{total_calls}]  {label:<14}  RTF={rtf:.2f}  "
                          f"✓{len(hits)}/{len(s['words'])}  transcript: {text[:90]}")
                    if misses:
                        print(f"                            missed: {miss_str}")

                except Exception as e:
                    print(f"    [{call_n:3d}/{total_calls}]  {label:<14}  ERROR: {e}")
                    for w in s["words"]:
                        word_results[w][cond].append(False)

        if rtfs:
            avg_rtf = sum(rtfs) / len(rtfs)
            print(f"\n  Average RTF this condition: {avg_rtf:.3f}  ({'real-time' if avg_rtf < 1 else 'SLOWER than real-time'})")

    # Final report
    print("\n\n" + "=" * 90)
    print("  HOTWORD ACCURACY REPORT — whisper-large-v3")
    print("=" * 90)
    print(f"  Voices tested ({len(VOICES)}): {', '.join(voice_labels)}")
    print_report(word_results, voice_labels)


if __name__ == "__main__":
    main()
