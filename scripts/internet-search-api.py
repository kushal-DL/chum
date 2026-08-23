"""
Google AI Mode image search bridge for Chum.

Flow:
  Chum app  POST /image { image_base64: "<base64 JPEG/PNG>" }
    -> Playwright navigates to Google AI Mode (seed query already in URL)
    -> Google AI responds to the text query (asking user to share an image)
    -> Script attaches the actual screenshot image and clicks Send
    -> Waits for Google to analyse the image
    -> Returns { response: "<AI answer text>" }

Two launch modes (set CDP_URL environment variable to switch):

  DEFAULT (no CDP_URL): Playwright opens a persistent Chrome profile in
    google-session/. On first run, solve any CAPTCHA that appears and the
    session is saved for future runs.

  CDP mode (CDP_URL=http://localhost:9222): Playwright attaches to your
    already-running Chrome browser. Start Chrome with:
        chrome.exe --remote-debugging-port=9222
    In this mode your existing Google sign-in is used -- no CAPTCHA.

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
from playwright.async_api import async_playwright, Browser, BrowserContext, Page

# Persistent profile folder (saves cookies / Google trust across restarts)
SESSION_DIR = Path(__file__).parent / "google-session"

# Google AI Mode with the seed query already in the URL so Google responds
# immediately without needing to type anything in the chat box.
QUERY_URL = (
    "https://www.google.com/search"
    "?q=respond+to+the+image+in+the+fewest+words+possible"
    "&udm=50"
)

# Set CDP_URL=http://localhost:9222 to attach to your already-running Chrome.
CDP_URL = os.environ.get("CDP_URL", "")

_browser: Browser | None = None
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

# Text markers derived from live DOM inspection of Google AI Mode.
# "Good response" / "Bad response" feedback buttons appear after EVERY AI turn —
# more reliable than "AI can make mistakes" which is absent in some layouts.
INITIAL_RESPONSE_MARKERS = ("Good response", "Bad response", "AI can make mistakes",
                             "Would you like", "Please upload")
IMAGE_TURN_MARKER = "You sent:"
IMAGE_BUTTONS_SKIP = "Share Download"
# Ordered list of candidate end-of-turn markers; first one found wins.
RESPONSE_END_MARKERS = ("Good response", "Bad response", "AI can make mistakes")


async def _launch_browser():
    global _browser, _context, _page
    pw = await async_playwright().start()

    if CDP_URL:
        # Attach to user's already-running Chrome (no CAPTCHA, uses existing Google session)
        print(f"[internet-search] Attaching to Chrome at {CDP_URL} ...", flush=True)
        _browser = await pw.chromium.connect_over_cdp(CDP_URL)
        # Use the first context (the user's existing browsing context)
        _context = _browser.contexts[0] if _browser.contexts else await _browser.new_context()
        _page = await _context.new_page()
    else:
        # Launch a persistent Chromium profile (session saved across restarts).
        # Using bundled Chromium (no channel=) avoids the --no-sandbox Chrome warning
        # while the persistent profile still builds Google trust across runs.
        SESSION_DIR.mkdir(exist_ok=True)
        _context = await pw.chromium.launch_persistent_context(
            str(SESSION_DIR),
            headless=False,
            args=[
                "--disable-blink-features=AutomationControlled",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-gpu",  # software rendering so GDI/DXGI can capture this window
            ],
            viewport={"width": 1280, "height": 900},
            locale="en-US",
        )
        _page = _context.pages[0] if _context.pages else await _context.new_page()

    await _page.goto(QUERY_URL, wait_until="domcontentloaded")
    await asyncio.sleep(2)

    # Detect CAPTCHA and wait for the user to handle it (persistent mode only)
    if not CDP_URL:
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


def _find_marker_positions(deep: str) -> list[int]:
    """
    Find positions of end-of-turn markers in the deep text.

    Google shows "Good response" / "Bad response" feedback buttons after each AI turn.
    "AI can make mistakes" is a fallback for layouts that omit the feedback buttons.
    We pick whichever candidate marker appears at least twice and use those positions.
    """
    for marker in RESPONSE_END_MARKERS:
        positions: list[int] = []
        search_from = 0
        while True:
            idx = deep.find(marker, search_from)
            if idx < 0:
                break
            positions.append(idx)
            search_from = idx + len(marker)
        if len(positions) >= 2:
            return positions  # use the first marker that has 2+ occurrences
    return []


async def _extract_image_response() -> str:
    """
    Poll shadow DOM until Google responds to the uploaded image.

    Each AI turn ends with feedback buttons ("Good response", "Bad response") or
    "AI can make mistakes". The seed-query response is turn 1; image analysis is
    turn 2. We extract text between those two end-of-turn markers.
    """
    for attempt in range(30):  # up to ~60 s
        await asyncio.sleep(2)
        deep = await _page.evaluate(DEEP_TEXT_JS)

        # CAPTCHA check
        if "not a robot" in deep.lower():
            print("[internet-search] CAPTCHA appeared mid-request!", flush=True)
            await _wait_for_ready()
            continue

        positions = _find_marker_positions(deep)
        if len(positions) < 2:
            if attempt % 5 == 4:
                print(
                    f"[internet-search] Waiting for image response... {(attempt+1)*2}s "
                    f"(page: {len(deep)} chars)",
                    flush=True,
                )
                print(f"[internet-search] Page snippet: {repr(deep[:400])}", flush=True)
            continue

        # Determine end marker length from whichever marker was chosen
        marker_len = next(
            len(m) for m in RESPONSE_END_MARKERS
            if deep.find(m, positions[0]) == positions[0]
        )

        # Text between turn 1 end and turn 2 end = image analysis
        region = deep[positions[0] + marker_len:positions[1]].strip()

        # Strip "You sent: N image Share Download" prefix if present
        for prefix in (IMAGE_TURN_MARKER, IMAGE_BUTTONS_SKIP):
            idx_p = region.find(prefix)
            if idx_p >= 0:
                region = region[idx_p + len(prefix):].strip()

        # Truncate at the UI chrome that follows every response.
        # Order matters: "Copy Share" appears before "public link" in the text.
        for tail in (" Copy Share", " public link", " Facebook ", " Like ", " Dislike "):
            idx_t = region.find(tail)
            if idx_t > 0:
                region = region[:idx_t]
                break
        region = region.strip()

        if region and len(region) > 5:
            return region

        if attempt % 5 == 4:
            print(f"[internet-search] Response region empty at {(attempt+1)*2}s", flush=True)

    return "No response received within 60 s -- check the browser window."


@asynccontextmanager
async def lifespan(app: FastAPI):
    await _launch_browser()
    yield
    if _context and not CDP_URL:
        await _context.close()
    if _browser and CDP_URL:
        await _browser.close()


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
