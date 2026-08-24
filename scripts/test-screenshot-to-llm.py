"""
Standalone test: capture a screen region with Python (PIL ImageGrab),
encode as base64 JPEG, and send to the local llama-server on port 8001.

Verifies that:
  1. Python-based screen capture produces a real (non-black) image.
  2. The LLM API endpoint accepts and understands images.

If the LLM gives a sensible description, the API path is fine and the
black-image bug lives in the C# capture code, not in the sending path.

Usage (from the scripts/ directory):
    python test-screenshot-to-llm.py [x y width height] [--save test.jpg]

Defaults to the full primary monitor if no region is given.
"""
import argparse
import base64
import io
import json
import sys
import urllib.request

from PIL import ImageGrab, Image

API_URL = "http://127.0.0.1:8001/v1/chat/completions"
API_KEY = "chum-llm-key-2026"
JPEG_QUALITY = 85
MAX_WIDTH = 1280


def capture(bbox=None) -> Image.Image:
    img = ImageGrab.grab(bbox=bbox, all_screens=False)
    print(f"[capture] grabbed {img.size[0]}x{img.size[1]} px, mode={img.mode}", flush=True)
    return img


def to_jpeg_b64(img: Image.Image, max_width: int = MAX_WIDTH, quality: int = JPEG_QUALITY) -> str:
    if img.width > max_width:
        ratio = max_width / img.width
        new_size = (max_width, int(img.height * ratio))
        img = img.resize(new_size, Image.LANCZOS)
        print(f"[encode]  resized to {img.size[0]}x{img.size[1]}", flush=True)

    buf = io.BytesIO()
    img.convert("RGB").save(buf, format="JPEG", quality=quality)
    b64 = base64.b64encode(buf.getvalue()).decode()
    print(f"[encode]  JPEG size {len(buf.getvalue())//1024} KB → base64 {len(b64)//1024} KB", flush=True)

    # Sanity check: measure darkness
    thumb = img.convert("L").resize((64, 64), Image.LANCZOS)
    pixels = list(thumb.getdata())
    dark = sum(1 for p in pixels if p < 20)
    print(f"[encode]  darkness check: {dark}/{len(pixels)} pixels below 20 "
          f"({100*dark/len(pixels):.0f}% dark)", flush=True)
    if dark / len(pixels) > 0.9:
        print("[encode]  WARNING: image is mostly black — capture may have failed!", flush=True)
    else:
        print("[encode]  Image looks OK (not mostly black).", flush=True)

    return b64


def ask_llm(b64: str) -> str:
    payload = {
        "model": "local",
        "max_tokens": 256,
        "messages": [
            {
                "role": "user",
                "content": [
                    {
                        "type": "text",
                        "text": (
                            "Describe this screenshot in 2-3 sentences. "
                            "What application or content is visible? "
                            "What are the dominant colours?"
                        ),
                    },
                    {
                        "type": "image_url",
                        "image_url": {"url": f"data:image/jpeg;base64,{b64}"},
                    },
                ],
            }
        ],
    }

    data = json.dumps(payload).encode()
    req = urllib.request.Request(
        API_URL,
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {API_KEY}",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            body = json.loads(resp.read())
        return body["choices"][0]["message"]["content"]
    except urllib.error.URLError as exc:
        return f"[ERROR] Could not reach LLM API: {exc}\nMake sure start-llm-api.ps1 is running."
    except Exception as exc:
        return f"[ERROR] {exc}"


def main():
    parser = argparse.ArgumentParser(description="Test screenshot → LLM pipeline")
    parser.add_argument("bbox", nargs="*", type=int,
                        help="Optional: left top width height (screen pixels)")
    parser.add_argument("--save", metavar="FILE",
                        help="Also save the captured image to this path (e.g. test.jpg)")
    args = parser.parse_args()

    if args.bbox:
        if len(args.bbox) != 4:
            sys.exit("Usage: test-screenshot-to-llm.py [left top width height]")
        left, top, w, h = args.bbox
        bbox = (left, top, left + w, top + h)
        print(f"[capture] region: {left},{top} -> {left+w},{top+h}  ({w}x{h} px)", flush=True)
    else:
        bbox = None
        print("[capture] full primary monitor", flush=True)

    img = capture(bbox)

    if args.save:
        img.save(args.save)
        print(f"[capture] saved to {args.save}", flush=True)

    b64 = to_jpeg_b64(img)

    print("\n[llm]     Sending to LLM API...", flush=True)
    response = ask_llm(b64)
    print("\n[llm]     Response:", flush=True)
    print("-" * 60)
    print(response)
    print("-" * 60)


if __name__ == "__main__":
    main()
