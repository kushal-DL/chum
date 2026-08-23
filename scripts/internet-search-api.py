"""
Google AI Mode image search bridge for Chum.

Flow:
  Chum app  POST /image { image_base64: "<base64 JPEG>" }
    -> Playwright types prompt + attaches image in Google AI Mode
    -> Returns { response: "<AI answer text>" }

FIRST RUN: a Chrome window opens and asks you to sign in to Google.
           Sign in, then wait -- the API becomes ready automatically.
           Subsequent runs use the saved session (google-session/ folder).

Start with: start-internet-search.ps1
"""
import asyncio
import base64
import os
import tempfile
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import uvicorn
from playwright.async_api import async_playwright, BrowserContext, Page

# ── Session storage -- saves Google login so sign-in is only needed once ──────
SESSION_DIR = Path(__file__).parent / "google-session"

GOOGLE_AI_URL = "https://www.google.com/search?udm=50"
PROMPT = "Respond to this image in as few words as possible"

_context: BrowserContext | None = None
_page: Page | None = None
_lock = asyncio.Lock()

# Shadow-DOM-aware full page text extraction
DEEP_TEXT_JS = """() => {
    function getText(root) {
        if (!root) return '';
        if (root.nodeType === Node.TEXT_NODE) return root.textContent + ' ';
        let out = '';
        if (root.shadowRoot) out += getText(root.shadowRoot);
        for (const child of (root.childNodes || [])) out += getText(child);
        return out;
    }
    return getText(document.body).replace(/\\s+/g, ' ').trim();
}"""


async def _launch_browser():
    global _context, _page
    SESSION_DIR.mkdir(exist_ok=True)
    pw = await async_playwright().start()

    # launch_persistent_context keeps cookies/session across restarts
    _context = await pw.chromium.launch_persistent_context(
        str(SESSION_DIR),
        headless=False,
        channel="chrome",
        args=[
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
        ],
        viewport={"width": 1280, "height": 900},
        locale="en-US",
        user_agent=(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/137.0.0.0 Safari/537.36"
        ),
    )

    _page = _context.pages[0] if _context.pages else await _context.new_page()
    await _page.goto(GOOGLE_AI_URL, wait_until="domcontentloaded")
    await asyncio.sleep(2)

    # Prompt user to sign in if not already signed in
    sign_in_btn = await _page.query_selector("button[aria-label='Sign in']")
    if sign_in_btn:
        print("[internet-search] *** Please sign in to your Google account in the browser window ***", flush=True)
        print("[internet-search] After signing in, this API becomes ready automatically. (Waiting up to 5 min)", flush=True)
        for _ in range(60):
            await asyncio.sleep(5)
            sign_in_btn = await _page.query_selector("button[aria-label='Sign in']")
            if not sign_in_btn:
                print("[internet-search] Sign-in detected -- reloading AI Mode...", flush=True)
                await _page.goto(GOOGLE_AI_URL, wait_until="domcontentloaded")
                await asyncio.sleep(2)
                break
        else:
            print("[internet-search] WARNING: Not signed in -- AI responses will not work.", flush=True)

    print("[internet-search] Browser ready -- Google AI Mode loaded.", flush=True)


async def _extract_response(query_text: str) -> str:
    """
    Poll the shadow DOM until the AI response is stable and no longer 'Transcribing...'.
    The page layout after sending is:
      [query text] Copied Copy Edit [query text again] [RESPONSE] Add files and tools ...
    """
    end_marker = "Add files and tools"

    for attempt in range(30):  # up to ~60s
        await asyncio.sleep(2)
        deep = await _page.evaluate(DEEP_TEXT_JS)

        # Locate response region: between end of second query echo and the input toolbar
        idx1 = deep.find(query_text)
        if idx1 < 0:
            continue
        idx2 = deep.find(query_text, idx1 + len(query_text))
        response_start = (idx2 + len(query_text)) if idx2 > 0 else (idx1 + len(query_text))

        idx_end = deep.find(end_marker, response_start)
        response = (deep[response_start:idx_end] if idx_end > response_start else deep[response_start:response_start + 2000]).strip()

        # Remove in-progress indicator
        clean = response.replace("Transcribing...", "").strip()

        if clean and len(clean) > 3 and "Transcribing" not in response:
            return clean

        if attempt % 5 == 4:
            print(f"[internet-search] Waiting for response... {(attempt+1)*2}s", flush=True)

    return "Response not received -- check the browser window. Google AI Mode may need you to sign in."


@asynccontextmanager
async def lifespan(app: FastAPI):
    await _launch_browser()
    yield
    if _context:
        await _context.close()


app = FastAPI(lifespan=lifespan)


class ImageRequest(BaseModel):
    image_base64: str  # JPEG or PNG, base64-encoded


@app.post("/image")
async def search_image(req: ImageRequest):
    if _page is None:
        raise HTTPException(503, "Browser not ready")

    async with _lock:
        img_bytes = base64.b64decode(req.image_base64)

        with tempfile.NamedTemporaryFile(suffix=".jpg", delete=False) as f:
            f.write(img_bytes)
            tmp = f.name

        try:
            # Navigate to fresh AI Mode page
            await _page.goto(GOOGLE_AI_URL, wait_until="domcontentloaded")
            await asyncio.sleep(1.5)

            # Type prompt into the chat textarea
            ta = await _page.query_selector("textarea[placeholder='Ask anything']")
            if not ta:
                return {"response": "Chat input not found -- Google AI Mode UI may have changed."}
            await ta.click()
            await ta.fill(PROMPT)
            await asyncio.sleep(0.3)

            # Open attachment menu
            add_btn = await _page.query_selector("button[aria-label='Add files and tools']")
            if not add_btn:
                return {"response": "Attachment button not found -- Google AI Mode UI may have changed."}
            await add_btn.click()
            await asyncio.sleep(0.7)

            # Click "Add images" to open file chooser
            add_img = await _page.query_selector("button[aria-label='Add images']")
            if not add_img:
                return {"response": "'Add images' option not found -- Google AI Mode UI may have changed."}

            async with _page.expect_file_chooser(timeout=5000) as fc_info:
                await add_img.click()
            fc = await fc_info.value
            await fc.set_files(tmp)
            await asyncio.sleep(0.8)

            # Click Send
            send_btn = await _page.query_selector("button[aria-label='Send']")
            if send_btn:
                await send_btn.click()
            else:
                await _page.keyboard.press("Enter")

            print(f"[internet-search] Image sent -- waiting for AI response...", flush=True)

            response = await _extract_response(PROMPT)
            print(f"[internet-search] Response: {response[:80]}...", flush=True)
            return {"response": response}

        finally:
            try:
                os.unlink(tmp)
            except Exception:
                pass


@app.get("/health")
async def health():
    signed_in = _page is not None and await _page.query_selector("button[aria-label='Sign in']") is None
    return {"status": "ok", "browser_ready": _page is not None, "signed_in": signed_in}


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8002)
