"""
Evaluate the fine-tuned Whisper model against technical-jargon test sentences.
Sends each MP3 to the local whisper_api_server.py (port 8000) and reports
word error rate per domain and overall.
"""
import json, pathlib, re, requests

WHISPER_URL = "http://127.0.0.1:8000/v1/audio/transcriptions"
MANIFEST    = pathlib.Path(__file__).parent / "manifest.json"

def normalise(text: str) -> str:
    """Lowercase, strip punctuation, collapse whitespace."""
    text = text.lower()
    text = re.sub(r"[^\w\s]", "", text)
    return re.sub(r"\s+", " ", text).strip()

def wer(ref: str, hyp: str) -> float:
    """Simple word-error rate (edit distance / ref length)."""
    r = ref.split()
    h = hyp.split()
    # Dynamic programming
    d = [[0] * (len(h) + 1) for _ in range(len(r) + 1)]
    for i in range(len(r) + 1):
        d[i][0] = i
    for j in range(len(h) + 1):
        d[0][j] = j
    for i in range(1, len(r) + 1):
        for j in range(1, len(h) + 1):
            cost = 0 if r[i-1] == h[j-1] else 1
            d[i][j] = min(d[i-1][j] + 1, d[i][j-1] + 1, d[i-1][j-1] + cost)
    return d[len(r)][len(h)] / max(len(r), 1)

def transcribe(audio_path: str) -> str:
    with open(audio_path, "rb") as f:
        resp = requests.post(
            WHISPER_URL,
            files={"file": (pathlib.Path(audio_path).name, f, "audio/mpeg")},
            data={"model": "whisper-large-v3-turbo"},
            timeout=120,
        )
    resp.raise_for_status()
    return resp.json().get("text", "").strip()

def main():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))
    print(f"\n{'Domain':<14} {'WER':>6}  Reference / Hypothesis")
    print("-" * 90)

    total_wer = 0.0
    for rec in manifest:
        ref = normalise(rec["text"])
        try:
            raw_hyp = transcribe(rec["file"])
        except Exception as e:
            print(f"{rec['domain']:<14} {'ERR':>6}  {e}")
            continue

        hyp  = normalise(raw_hyp)
        err  = wer(ref, hyp)
        total_wer += err
        status = "OK" if err < 0.15 else ("WARN" if err < 0.35 else "FAIL")
        print(f"{rec['domain']:<14} {err:>5.1%}  [{status}]")
        print(f"  REF: {ref}")
        print(f"  HYP: {hyp}")
        print()

    avg = total_wer / len(manifest)
    print("-" * 90)
    print(f"{'AVERAGE':<14} {avg:>5.1%}  ({len(manifest)} domains)")

if __name__ == "__main__":
    main()
