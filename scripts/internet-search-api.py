"""
Google AI Mode image search bridge for Chum.

Flow:
  Chum app  POST /image { image_base64: "<base64 JPEG/PNG>" }
    -> Playwright navigates to Google AI Mode (seed query already in URL)
    -> Google AI responds to the text query (asking user to share an image)
    -> Script attaches the actual screenshot image and clicks Send
    -> Waits for Google to analyse the image
    -> Returns { response: "<AI answer text>" }

Session is saved in google-session/ so Google learns to trust this browser
profile across restarts. On first run, solve any CAPTCHA that appears.

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

# Persistent profile folder (saves cookies / Google trust across restarts)
SESSION_DIR = Path(__file__).parent / "google-session"

# Google AI Mode with the seed query already in the URL so Google responds
# immediately without needing to type anything in the chat box.
QUERY_URL = (
    "https://www.google.com/search"
    "?q=respond+to+the+image+in+the+fewest+words+possible"
    "&udm=50"
)

_context: BrowserContext | None = None
_page: Page | None = None
_lock = asyncio.Lock()

# Shadow-DOM-aware text extractor (excludes <style>/<script> noise)
DEEP_TEXT_JS = """() => {
    function getText(root) {
        if (!root) return '';
        if (root.nodeType === Node.TEXT_NODE) return root.textContent + ' ';
        if (root.nodeName === 'STYLE' || root.nodeName === 'SCRIPT' || root.nodeName === 'NOSCRIPT') return '';
        let out = '';
        if (root.shadowRoot) out += getText(root.shadowRoot);
        for (const child of (root.childNodes || [])) out += getText(child);
        return out;
    }
    return getText(document.body).replace(/\\s+/g, ' ').trim();
}"""

# Text markers derived from live DOM inspection of Google AI Mode:
#   - After every AI response: "AI can make mistakes, so double-check responses"
#   - When image turn starts: "You sent:" followed by "Share Download" (thumbnail controls)
#   - Initial response to text query contains one of these phrases:
INITIAL_RESPONSE_MARKERS = ("AI can make mistakes", "Would you like", "Please upload")
IMAGE_TURN_MARKER = "You sent:"
IMAGE_BUTTONS_SKIP = "Share Download"
RESPONSE_END_MARKER = "AI can make mistakes"


async def _launch_browser():
    global _context, _page
    SESSION_DIR.mkdir(exist_ok=True)
    pw = await async_playwright().start()

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
    )

    _page = _context.pages[0] if _context.pages else await _context.new_page()
    await _page.goto(QUERY_URL, wait_until="domcontentloaded")
    await asyncio.sleep(2)

    # Detect CAPTCHA or sign-in and wait for the user to handle it
    await _wait_for_ready()

    print("[internet-search] Browser ready -- Google AI Mode loaded.", flush=True)
    print("[internet-search] Listening on http://127.0.0.1:8002", flush=True)


async def _wait_for_ready():
    """Wait for the browser to be in a usable state (past any CAPTCHA or sign-in)."""
    for _ in range(120):  # wait up to 10 minutes
        deep = await _page.evaluate(DEEP_TEXT_JS)
        page_text = deep.lower()

        if "not a robot" in page_text or "captcha" in page_text or "unusual traffic" in page_text:
            print("[internet-search] *** CAPTCHA detected -- please solve it in the browser window ***", flush=True)
            await asyncio.sleep(5)
            continue

        if "ask anything" in page_text or any(m.lower() in page_text for m in INITIAL_RESPONSE_MARKERS):
            return  # page is ready

        await asyncio.sleep(3)

    print("[internet-search] WARNING: Browser may not be ready. Proceeding anyway.", flush=True)


async def _navigate_fresh():
    """Navigate to a fresh Google AI Mode conversation and wait for the seed response."""
    await _page.goto(QUERY_URL, wait_until="domcontentloaded")
    # Wait for Google AI to respond to the seed text query
    for _ in range(20):
        await asyncio.sleep(1.5)
        deep = await _page.evaluate(DEEP_TEXT_JS)
        if any(m in deep for m in INITIAL_RESPONSE_MARKERS):
            return
        if "not a robot" in deep.lower():
            print("[internet-search] CAPTCHA appeared -- waiting for user to solve...", flush=True)
            await _wait_for_ready()
            return
    # Proceed even if we time out -- the input area may still be usable


async def _wait_for_attach_button(timeout_s: int = 10) -> bool:
    """Wait until the 'Add files and tools' button is present in the DOM."""
    for _ in range(timeout_s * 2):
        btn = await _page.query_selector("button[aria-label='Add files and tools']")
        if btn:
            return True
        await asyncio.sleep(0.5)
    return False


async def _extract_image_response() -> str:
    """
    Poll shadow DOM until Google's image response is complete.

    After the image is sent the conversation gains a turn starting with
    "You sent:" -> "Share Download" (thumbnail UI) -> [AI response text] ->
    "AI can make mistakes, so double-check responses".
    """
    for attempt in range(30):  # up to 60 s
        await asyncio.sleep(2)
        deep = await _page.evaluate(DEEP_TEXT_JS)

        # CAPTCHA interruption
        if "not a robot" in deep.lower():
            print("[internet-search] CAPTCHA appeared mid-request!", flush=True)
            await _wait_for_ready()
            continue

        # Find the most recent image turn
        idx_sent = deep.rfind(IMAGE_TURN_MARKER)
        if idx_sent < 0:
            if attempt % 5 == 4:
                print(f"[internet-search] Waiting for image turn... {(attempt+1)*2}s", flush=True)
            continue

        # Find response-end marker that follows this image turn
        idx_end = deep.find(RESPONSE_END_MARKER, idx_sent)
        if idx_end < 0:
            if attempt % 5 == 4:
                print(f"[internet-search] Waiting for response... {(attempt+1)*2}s", flush=True)
            continue

        region = deep[idx_sent:idx_end]

        # Skip the image thumbnail controls ("You sent: N image Share Download")
        skip = region.find(IMAGE_BUTTONS_SKIP)
        if skip >= 0:
            region = region[skip + len(IMAGE_BUTTONS_SKIP):]

        response = region.strip()
        if response and len(response) > 3:
            return response

    return "No response received within 60 s -- check the browser window."


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
            await _navigate_fresh()

            # Wait for the input area to be ready
            if not await _wait_for_attach_button():
                return {"response": "Input area not ready after navigation. Check the browser window."}

            # Open the attachment menu
            add_btn = await _page.query_selector("button[aria-label='Add files and tools']")
            await add_btn.click()
            await asyncio.sleep(0.7)

            # Click 'Add images' to open the OS file picker
            add_img = await _page.query_selector("button[aria-label='Add images']")
            if not add_img:
                return {"response": "'Add images' option not found -- Google AI Mode UI may have changed. Restart the bridge."}

            async with _page.expect_file_chooser(timeout=5000) as fc_info:
                await add_img.click()
            fc = await fc_info.value
            await fc.set_files(tmp)
            await asyncio.sleep(0.8)

            # Send
            send_btn = await _page.query_selector("button[aria-label='Send']")
            if send_btn:
                await send_btn.click()
            else:
                await _page.keyboard.press("Enter")

            print("[internet-search] Image sent -- waiting for AI response...", flush=True)

            response = await _extract_image_response()
            print(f"[internet-search] Response ({len(response)} chars): {response[:80]}...", flush=True)
            return {"response": response}

        finally:
            try:
                os.unlink(tmp)
            except Exception:
                pass


@app.get("/health")
async def health():
    return {"status": "ok", "browser_ready": _page is not None}


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8002)
