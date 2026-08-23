"""
Google AI Mode image search bridge for Chum.

Flow:
  Chum app POST /image { image_base64: "<base64 JPEG/PNG>" }
    → Playwright uploads image to Google AI Mode
    → Returns { response: "<AI answer text>" }

Start with: start-internet-search.ps1
"""
import asyncio
import base64
import os
import tempfile
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import uvicorn
from playwright.async_api import async_playwright, Browser, BrowserContext, Page

_browser: Browser | None = None
_context: BrowserContext | None = None
_page: Page | None = None
_lock = asyncio.Lock()

GOOGLE_AI_URL = "https://www.google.com/search?udm=50"


async def _launch_browser():
    global _browser, _context, _page
    pw = await async_playwright().start()
    _browser = await pw.chromium.launch(
        headless=False,
        channel="chrome",
        args=[
            "--incognito",
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
        ],
    )
    _context = await _browser.new_context(
        viewport={"width": 1280, "height": 900},
        locale="en-US",
        user_agent=(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/137.0.0.0 Safari/537.36"
        ),
    )
    _page = await _context.new_page()
    await _page.goto(GOOGLE_AI_URL, wait_until="domcontentloaded")

    # Dismiss cookie/consent dialogs (EU regions, some enterprise networks)
    for sel in ["button:has-text('Accept all')", "button:has-text('I agree')", "#L2AGLb"]:
        try:
            await _page.click(sel, timeout=2000)
            break
        except Exception:
            pass

    print("[internet-search] Browser ready — Google AI Mode loaded", flush=True)


@asynccontextmanager
async def lifespan(app: FastAPI):
    await _launch_browser()
    yield
    if _browser:
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
            # Fresh AI Mode page before each query
            await _page.goto(GOOGLE_AI_URL, wait_until="domcontentloaded")

            # Click the Google Lens / image search button in the search bar
            for cam_sel in [
                "button[aria-label*='Search by image']",
                "button[aria-label*='camera']",
                "button[aria-label*='lens']",
                "button[aria-label*='Lens']",
                "[jsaction*='lens']",
                ".NZmxZe",
                ".Gdd5U",
            ]:
                try:
                    await _page.click(cam_sel, timeout=1500)
                    break
                except Exception:
                    continue

            # Upload the image via file chooser
            uploaded = False
            for upload_sel in [
                "text=Upload a file",
                "text=Upload",
                "text=upload",
                "[aria-label*='Upload']",
                ".DV7the",
            ]:
                try:
                    async with _page.expect_file_chooser(timeout=3000) as fc_info:
                        await _page.click(upload_sel, timeout=2000)
                    fc = await fc_info.value
                    await fc.set_files(tmp)
                    uploaded = True
                    break
                except Exception:
                    continue

            if not uploaded:
                # Fallback: target any visible file input directly
                for fi in await _page.query_selector_all("input[type=file]"):
                    try:
                        await fi.set_input_files(tmp)
                        uploaded = True
                        break
                    except Exception:
                        continue

            if not uploaded:
                return {
                    "response": (
                        "Could not upload image — Google's UI may have changed. "
                        "Check the browser window and restart start-internet-search.ps1."
                    )
                }

            # Wait for Google AI answer to appear
            response_text = None
            for resp_sel in [
                ".xyqXXe",
                ".RDApEe",
                ".Cp74Ic",
                "c-wiz .NN3Bqe",
                ".wDYxhc",
                ".kno-rdesc span",
                ".IZ6rdc",
                "[data-attrid='wa:/description'] .BNeawe",
            ]:
                try:
                    el = await _page.wait_for_selector(resp_sel, timeout=25000)
                    if el:
                        text = (await el.inner_text()).strip()
                        if len(text) > 10:
                            response_text = text
                            break
                except Exception:
                    continue

            if not response_text:
                # Last resort: grab the first substantive chunk from the results area
                try:
                    raw = (await _page.inner_text("#rso, main, #search")).strip()
                    response_text = raw[:2000] if raw else "No response extracted."
                except Exception:
                    response_text = "No response — check the browser window for the Google AI result."

            return {"response": response_text}

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
