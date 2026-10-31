"""
ASP RAG bridge for Chum.

Supports two namespaces on the same process:
  chum_docs    — user-loaded reference documents (PDF, TXT, MD)
  chum_session — live session memory (Q&A pairs, transcript chunks, vision events)

Endpoints:
  GET  /health   — liveness probe + per-namespace stats
  POST /index    {"documents":[{"id","title","text"}], "namespace":"chum_docs"}
               → {"indexed": N}
  POST /retrieve {"query","namespace":"chum_docs","top_k":5,"token_budget":2000}
               → {"context":"...","sources":[...],"dropped_count":N}

Start with: start-asp-rag.ps1
"""
from __future__ import annotations

import asyncio
import os
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from typing import Any, Optional

import uvicorn
from fastapi import Depends, FastAPI, HTTPException
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from pydantic import BaseModel

# ASP Vector shape
from asp.retrieval.vector.vector_retriever import RetrievalContext, build_index, retrieve
import asp.retrieval.vector.arms  # noqa: F401 — registers all built-in arms

MODEL_NAME = os.environ.get("ASP_VECTOR_EMBED_MODEL", "BAAI/bge-small-en-v1.5")
RAG_API_KEY = os.environ.get("RAG_API_KEY", "")

VALID_NAMESPACES = {"chum_docs", "chum_session"}


@dataclass
class _Doc:
    title: str
    text: str


@dataclass
class _Namespace:
    corpus: dict[str, _Doc] = field(default_factory=dict)
    index: Any = None
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)


_namespaces: dict[str, _Namespace] = {
    "chum_docs":    _Namespace(),
    "chum_session": _Namespace(),
}


def _build_sync(ns_name: str, ns: _Namespace) -> None:
    if not ns.corpus:
        ns.index = None
        return
    pool = min(200, len(ns.corpus))
    ns.index = build_index(
        ns.corpus,
        name=f"chum_{ns_name}",
        model_name=MODEL_NAME,
        use_cache=False,  # always re-embed so content changes are picked up
    )
    print(f"[asp-rag] [{ns_name}] Index ready: {len(ns.corpus)} docs, dim {ns.index.dense.shape[1]}", flush=True)


@asynccontextmanager
async def lifespan(app: FastAPI):
    yield


app = FastAPI(lifespan=lifespan)
_security = HTTPBearer(auto_error=False)


def _check_auth(creds: Optional[HTTPAuthorizationCredentials] = Depends(_security)) -> None:
    if not RAG_API_KEY:
        return
    if creds is None or creds.credentials != RAG_API_KEY:
        raise HTTPException(401, "Unauthorized")


def _resolve_namespace(name: str) -> _Namespace:
    if name not in VALID_NAMESPACES:
        raise HTTPException(400, f"Unknown namespace '{name}'. Valid: {sorted(VALID_NAMESPACES)}")
    return _namespaces[name]


class IndexRequest(BaseModel):
    documents: list[dict]
    namespace: str = "chum_docs"


class RetrieveRequest(BaseModel):
    query: str
    namespace: str = "chum_docs"
    top_k: int = 5
    token_budget: int = 2000


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "namespaces": {
            name: {
                "docs": len(ns.corpus),
                "ready": ns.index is not None,
            }
            for name, ns in _namespaces.items()
        },
    }


@app.post("/index")
async def index_docs(req: IndexRequest, _=Depends(_check_auth)):
    ns = _resolve_namespace(req.namespace)
    async with ns.lock:
        ns.corpus = {
            doc["id"]: _Doc(
                title=doc.get("title") or doc["id"],
                text=doc.get("text", ""),
            )
            for doc in req.documents
            if (doc.get("text") or "").strip()
        }
        await asyncio.get_event_loop().run_in_executor(None, _build_sync, req.namespace, ns)
    return {"indexed": len(ns.corpus)}


@app.post("/retrieve")
async def retrieve_context(req: RetrieveRequest, _=Depends(_check_auth)):
    ns = _resolve_namespace(req.namespace)

    if ns.index is None:
        return {"context": None, "sources": [], "dropped_count": 0}

    top_k = max(1, min(req.top_k, len(ns.corpus)))
    pool = min(200, len(ns.corpus))

    # Fresh context per query so dropped_tokens counter is per-query
    ctx = RetrievalContext(
        index=ns.index,
        model_name=MODEL_NAME,
        params={"pool": pool, "gamma": 0.25, "centroid_k": pool},
    )

    doc_ids = retrieve("apex", req.query, ctx, top_k)

    # Assemble context string within token budget (estimate: 4 chars ≈ 1 token)
    char_budget = req.token_budget * 4
    parts: list[str] = []
    sources: list[str] = []
    dropped = 0
    remaining = char_budget

    for doc_id in doc_ids:
        doc = ns.corpus.get(doc_id)
        if doc is None:
            continue
        chunk = f"[{doc.title}]\n{doc.text}"
        if len(chunk) <= remaining:
            parts.append(chunk)
            sources.append(doc.title)
            remaining -= len(chunk)
        else:
            dropped += 1

    if not parts:
        return {"context": None, "sources": [], "dropped_count": dropped}

    label = "MEETING MEMORY" if req.namespace == "chum_session" else "REFERENCE DOCUMENTS (RAG)"
    context = (
        f"--- {label} ---\n"
        + "\n\n".join(parts)
        + f"\n--- END {label} ---"
    )
    return {"context": context, "sources": sources, "dropped_count": dropped}


if __name__ == "__main__":
    host = os.environ.get("BRIDGE_HOST", "0.0.0.0")
    port = int(os.environ.get("BRIDGE_PORT", "8003"))
    print(f"[asp-rag] Listening on http://{host}:{port}", flush=True)
    uvicorn.run(app, host=host, port=port)
