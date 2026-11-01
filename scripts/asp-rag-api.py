"""
ASP RAG bridge for Chum — Shape-Aware Multi-Representation Retrieval.

Integrates Agent Semantic Protocol (ASP) 4-stage Tree Retrieval,
Centroid-Subtracted Dense Reranking (CSDR), and ShapeRouter.

Supports two namespaces on the same process:
  chum_docs    — reference documents (from dedicated folder, desktop app, or scanned dirs)
  chum_session — live session memory (Q&A pairs, transcript chunks, vision events)

Endpoints:
  GET  /health     — liveness probe + shape capabilities + per-namespace stats
  GET  /docs-dir   — inspection of dedicated docs folder and discovered documents
  POST /index      — index document list {"documents":[{"id","title","text"}], "namespace":"chum_docs"}
  POST /index-dir  — index local host directory {"dir_path":"...", "recursive":true}
  POST /retrieve   — shape-aware retrieval {"query","namespace":"chum_docs","top_k":5,"token_budget":2000}
  POST /clean      — zero-footprint wipe of in-memory indices, trees, chunks, and staged data

Start with: start-asp-rag.sh (Linux) or start-asp-rag.ps1 (Windows)
Clean with: clean-rag-data.sh (Linux) or clean-rag-data.ps1 (Windows)
"""
from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from datetime import datetime, timezone
import os
import re
import shutil
from typing import Any, Optional

# Ensure non-root cache for HuggingFace to prevent permission errors
os.environ.setdefault("HF_HOME", os.path.expanduser("~/.cache/huggingface_user"))

import uvicorn
from fastapi import Depends, FastAPI, HTTPException
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from pydantic import BaseModel

# ASP Primitives & Retrievers
ASP_AVAILABLE = False
RetrievalContext = None
build_index = None
retrieve = None
TreeRetriever = None
DocumentTree = None
TreeNode = None
NodeType = None
TableMetadata = None
ShapeRouter = None
RetrievalShape = None

try:
    # Vector shape (CSDR, hybrid_dense_bm25, flat_dense)
    from asp.retrieval.vector.vector_retriever import RetrievalContext, build_index, retrieve
    import asp.retrieval.vector.arms  # noqa: F401 — registers csdr, apex, hybrid_dense_bm25, flat_dense

    # Document Tree shape (4-stage pipeline with round-robin seeding & 3-phase traversal)
    from asp.retrieval.document_tree.retriever import TreeRetriever
    from asp.retrieval.document_tree.schema import DocumentTree, NodeType, TableMetadata, TreeNode

    # Autonomous Shape Routing
    from asp.agents.shape_router import ShapeRouter
    from asp.core.shapes import RetrievalShape

    ASP_AVAILABLE = True
except ImportError as err:
    print(f"[asp-rag] Note: ASP library import returned: {err}. Operating with native fallback.", flush=True)

MODEL_NAME = os.environ.get("ASP_VECTOR_EMBED_MODEL", "BAAI/bge-small-en-v1.5")
RAG_API_KEY = os.environ.get("RAG_API_KEY", "llm")

# Dedicated RAG docs directory (defaults to ../rag_docs if present)
_DEFAULT_DOCS_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "rag_docs"))
DOCS_DIR = os.environ.get("ASP_RAG_DOCS_DIR", _DEFAULT_DOCS_DIR if os.path.isdir(_DEFAULT_DOCS_DIR) else "")

# Staging directory for temporary files
_DEFAULT_STAGING = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "data", "rag_staging"))
STAGING_DIR = os.environ.get("ASP_RAG_STAGING_DIR", _DEFAULT_STAGING)

VALID_NAMESPACES = {"chum_docs", "chum_session"}

SUPPORTED_EXTENSIONS = {
    # Documents & markup
    ".txt", ".md", ".markdown", ".pdf", ".rst", ".log",
    # Web & frontend
    ".html", ".htm", ".css", ".js", ".jsx", ".ts", ".tsx",
    # Python & data formats
    ".py", ".pyi", ".json", ".csv", ".yaml", ".yml", ".toml", ".xml", ".ini",
    # Systems & backend code
    ".cs", ".xaml", ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp",
    ".rs", ".go", ".java", ".kt", ".sql", ".graphql",
    # Shell & DevOps
    ".sh", ".bash", ".ps1", ".bat", ".cmd", ".dockerfile",
}

# Global router instance for sub-millisecond query shape classification
_global_router = ShapeRouter() if (ASP_AVAILABLE and ShapeRouter is not None) else None


@dataclass
class _Doc:
    title: str
    text: str


@dataclass
class _Namespace:
    corpus: dict[str, _Doc] = field(default_factory=dict)       # chunked text for vector retrieval
    raw_docs: dict[str, _Doc] = field(default_factory=dict)     # full unchunked documents
    trees: dict[str, Any] = field(default_factory=dict)         # slug -> DocumentTree
    tree_retriever: Any = None
    index: Any = None
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)


_namespaces: dict[str, _Namespace] = {
    "chum_docs":    _Namespace(),
    "chum_session": _Namespace(),
}


def _extract_text_from_file(path: str) -> str:
    """Extract text from file. Supports PDF via pypdf and UTF-8 text formats."""
    ext = os.path.splitext(path)[1].lower()
    if ext == ".pdf":
        try:
            import pypdf
            reader = pypdf.PdfReader(path)
            pages = [p.extract_text() or "" for p in reader.pages]
            return "\n".join(pages)
        except Exception as e:
            print(f"[asp-rag] Failed to read PDF '{path}': {e}", flush=True)
            return ""
    else:
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as f:
                return f.read()
        except Exception as e:
            print(f"[asp-rag] Failed to read file '{path}': {e}", flush=True)
            return ""


def _chunk_text(text: str, chunk_size: int = 1200, overlap: int = 200) -> list[str]:
    """Splits long text into overlapping chunks suitable for semantic vector retrieval."""
    text = text.strip()
    if not text:
        return []
    if len(text) <= chunk_size:
        return [text]

    chunks: list[str] = []
    start = 0
    while start < len(text):
        end = start + chunk_size
        chunk = text[start:end]
        if end < len(text):
            last_break = max(chunk.rfind("\n"), chunk.rfind(". "), chunk.rfind(" "))
            if last_break > chunk_size // 2:
                chunk = chunk[:last_break + 1]
                end = start + len(chunk)
        chunk_clean = chunk.strip()
        if chunk_clean:
            chunks.append(chunk_clean)
        start = end - overlap
        if start >= len(text) or end >= len(text):
            break
    return chunks


def _build_document_tree(doc_id: str, title: str, full_text: str) -> Optional[Any]:
    """Builds an ASP DocumentTree with hierarchical section nodes, child tables, and leaf nodes."""
    if not ASP_AVAILABLE or DocumentTree is None or TreeNode is None:
        return None

    # Clean slug for identifier
    slug = re.sub(r"[^a-zA-Z0-9_-]", "_", doc_id.split("#")[0]).strip("_") or "doc"
    source_id = f"src::{slug}"
    now = datetime.now(timezone.utc)

    root_id = f"{slug}::root"
    nodes: dict[str, Any] = {}
    child_ids: list[str] = []

    lines = full_text.splitlines()
    sections: list[dict] = []
    curr_sec_title = "Overview"
    curr_sec_lines: list[str] = []

    # Regex for markdown headings, document clauses, HTML tags, or code function/class definitions
    heading_re = re.compile(
        r"^(?:#{1,4}\s+(.+)|(?:Section|Clause|Article|Appendix)\s+([A-Za-z0-9_.-]+[:\s]?.*)|<h[1-4][^>]*>(.+?)</h[1-4]>|<title>(.+?)</title>|(?:async\s+)?def\s+([A-Za-z0-9_]+).*|class\s+([A-Za-z0-9_]+).*|(?:public|private|protected|internal)?\s*(?:static\s+)?(?:class|record|struct|interface|enum)\s+([A-Za-z0-9_]+).*|function\s+([A-Za-z0-9_]+).*|(?:func|fn)\s+([A-Za-z0-9_]+).*)$",
        re.IGNORECASE,
    )

    for line in lines:
        m = heading_re.match(line.strip())
        if m:
            if curr_sec_lines:
                sections.append({"title": curr_sec_title, "content": "\n".join(curr_sec_lines)})
                curr_sec_lines = []
            matched_name = next((g for g in m.groups() if g), line.strip())
            curr_sec_title = matched_name.strip("#<> \t").strip()
        else:
            curr_sec_lines.append(line)

    if curr_sec_lines or not sections:
        sections.append({"title": curr_sec_title, "content": "\n".join(curr_sec_lines)})

    table_re = re.compile(r"^\s*\|.*\|\s*$")

    for idx, sec in enumerate(sections, 1):
        sec_id = f"{slug}::sec_{idx}"
        sec_title = sec["title"]
        sec_content = sec["content"].strip()
        if not sec_content:
            continue

        child_ids.append(sec_id)
        heading_path = f"{title} > {sec_title}"

        # Child tables detection (Phase 1 Child Table Injection in TreeRetriever)
        sec_lines = sec_content.splitlines()
        table_lines = [l for l in sec_lines if table_re.match(l)]
        sec_child_ids = []

        if len(table_lines) >= 2:
            tbl_id = f"{sec_id}::tbl"
            sec_child_ids.append(tbl_id)
            headers = [c.strip() for c in table_lines[0].strip("|").split("|")]
            table_meta = TableMetadata(
                table_columns=headers,
                row_count=max(1, len(table_lines) - 2),
                parent_section_id=sec_id,
            ) if TableMetadata else None
            tbl_node = TreeNode(
                node_id=tbl_id,
                node_type=NodeType.table if NodeType else "table",
                doc_slug=slug,
                source_id=source_id,
                title=f"Table: {sec_title}",
                content="\n".join(table_lines),
                depth=2,
                heading_path=f"{heading_path} > Table",
                token_count=max(1, int(len(table_lines) * 10)),
                char_count=sum(len(l) for l in table_lines),
                parent_node_id=sec_id,
                table_metadata=table_meta,
            )
            nodes[tbl_id] = tbl_node

        sec_node = TreeNode(
            node_id=sec_id,
            node_type=NodeType.section if NodeType else "section",
            doc_slug=slug,
            source_id=source_id,
            title=sec_title,
            content=sec_content,
            depth=1,
            heading_path=heading_path,
            token_count=max(1, int(len(sec_content.split()) * 1.3)),
            char_count=len(sec_content),
            parent_node_id=root_id,
            child_node_ids=sec_child_ids,
        )
        nodes[sec_id] = sec_node

    root_node = TreeNode(
        node_id=root_id,
        node_type=NodeType.document if NodeType else "document",
        doc_slug=slug,
        source_id=source_id,
        title=title,
        content=f"Document: {title}",
        depth=0,
        heading_path=title,
        token_count=10,
        char_count=len(title),
        child_node_ids=child_ids,
    )
    nodes[root_id] = root_node

    total_tokens = sum(n.token_count for n in nodes.values())
    return DocumentTree(
        doc_slug=slug,
        source_id=source_id,
        root=root_node,
        nodes=nodes,
        total_nodes=len(nodes),
        total_tokens=total_tokens,
        parsed_at=now,
        parse_method="deterministic_section_parser",
    )


def scan_directory_docs(dir_path: str, recursive: bool = True) -> tuple[list[dict], dict[str, _Doc]]:
    """Traverses a directory and returns: (1) chunked docs for vector index, (2) raw docs for tree index."""
    chunks_list: list[dict] = []
    raw_map: dict[str, _Doc] = {}
    if not os.path.exists(dir_path):
        return chunks_list, raw_map

    abs_dir = os.path.abspath(dir_path)
    walker = os.walk(abs_dir) if recursive else [(abs_dir, [], os.listdir(abs_dir))]
    for root, _, files in walker:
        for fname in sorted(files):
            ext = os.path.splitext(fname)[1].lower()
            if ext not in SUPPORTED_EXTENSIONS:
                continue
            full_path = os.path.join(root, fname)
            rel_path = os.path.relpath(full_path, abs_dir)
            content = _extract_text_from_file(full_path)
            if not content.strip():
                continue

            # Store full raw document for TreeRetriever
            raw_map[rel_path] = _Doc(title=rel_path, text=content)

            # Split into semantic chunks for VectorRetriever
            chunks = _chunk_text(content)
            for idx, c in enumerate(chunks):
                doc_id = f"{rel_path}#{idx+1}" if len(chunks) > 1 else rel_path
                title = f"{rel_path} [{idx+1}/{len(chunks)}]" if len(chunks) > 1 else rel_path
                chunks_list.append({
                    "id": doc_id,
                    "title": title,
                    "text": c,
                })
    return chunks_list, raw_map


def _build_sync(ns_name: str, ns: _Namespace) -> None:
    """Builds both TreeRetriever (if documents present) and VectorRetriever."""
    if not ns.corpus and not ns.raw_docs:
        ns.index = None
        ns.tree_retriever = None
        return

    # 1. Build DocumentTree instances & TreeRetriever
    if ASP_AVAILABLE and TreeRetriever is not None and DocumentTree is not None:
        tree_list = []
        for doc_id, doc in ns.raw_docs.items():
            tree = _build_document_tree(doc_id, doc.title, doc.text)
            if tree:
                ns.trees[tree.doc_slug] = tree
                tree_list.append(tree)
        if tree_list:
            ns.tree_retriever = TreeRetriever(tree_list, expansion_cap=8)
            print(f"[asp-rag] [{ns_name}] TreeRetriever ready: {len(tree_list)} trees, "
                  f"{sum(t.total_nodes for t in tree_list)} nodes (4-stage pipeline ready)", flush=True)
        else:
            ns.tree_retriever = None

    # 2. Build Vector Index
    if not ASP_AVAILABLE or build_index is None:
        print(f"[asp-rag] [{ns_name}] ASP vector package not found. Running in fallback mode.", flush=True)
        ns.index = "fallback"
        return

    try:
        ns.index = build_index(
            ns.corpus,
            name=f"chum_{ns_name}",
            model_name=MODEL_NAME,
            use_cache=False,  # always re-embed so modifications are active immediately
        )
        print(f"[asp-rag] [{ns_name}] Vector index ready: {len(ns.corpus)} chunks, dim {ns.index.dense.shape[1]} (CSDR ready)", flush=True)
    except Exception as ex:
        print(f"[asp-rag] [{ns_name}] Vector build failed ({ex}). Falling back to tree/keyword.", flush=True)
        ns.index = "fallback"


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Auto-index dedicated docs directory on startup if configured and non-empty
    if DOCS_DIR and os.path.isdir(DOCS_DIR):
        print(f"[asp-rag] Dedicated docs directory: {DOCS_DIR}", flush=True)
        chunks, raw = scan_directory_docs(DOCS_DIR, recursive=True)
        if chunks:
            print(f"[asp-rag] Discovered {len(raw)} documents ({len(chunks)} chunks) in {DOCS_DIR}. Indexing chum_docs...", flush=True)
            ns = _namespaces["chum_docs"]
            async with ns.lock:
                ns.raw_docs = raw
                ns.corpus = {d["id"]: _Doc(title=d["title"], text=d["text"]) for d in chunks}
                await asyncio.get_event_loop().run_in_executor(None, _build_sync, "chum_docs", ns)
        else:
            print(f"[asp-rag] Dedicated docs directory '{DOCS_DIR}' is empty (ready for files).", flush=True)
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


class IndexDirRequest(BaseModel):
    dir_path: str
    namespace: str = "chum_docs"
    recursive: bool = True
    clear_existing: bool = False


class RetrieveRequest(BaseModel):
    query: str
    namespace: str = "chum_docs"
    top_k: int = 5
    token_budget: int = 2000


class CleanRequest(BaseModel):
    namespaces: list[str] = ["chum_docs", "chum_session"]
    clean_staging: bool = True


@app.get("/health")
async def health():
    features = []
    if ASP_AVAILABLE:
        features.extend([
            "tree_retriever_4stage",
            "round_robin_multi_doc_seeding",
            "3phase_tree_traversal",
            "child_table_injection",
            "cross_reference_resolution",
            "shape_router",
            "csdr_vector",
            "hybrid_dense_bm25",
        ])
    else:
        features.append("keyword_fallback")

    return {
        "status": "ok",
        "asp_available": ASP_AVAILABLE,
        "asp_version": "0.1.0" if ASP_AVAILABLE else None,
        "features": features,
        "docs_dir": DOCS_DIR,
        "namespaces": {
            name: {
                "raw_docs": len(ns.raw_docs),
                "chunks": len(ns.corpus),
                "trees": len(ns.trees),
                "tree_ready": ns.tree_retriever is not None,
                "vector_ready": ns.index is not None and ns.index != "fallback",
            }
            for name, ns in _namespaces.items()
        },
    }


@app.get("/docs-dir")
async def get_docs_dir_info():
    exists = bool(DOCS_DIR and os.path.isdir(DOCS_DIR))
    file_list = []
    if exists:
        for root, _, files in os.walk(DOCS_DIR):
            for f in files:
                ext = os.path.splitext(f)[1].lower()
                if ext in SUPPORTED_EXTENSIONS:
                    file_list.append(os.path.relpath(os.path.join(root, f), DOCS_DIR))
    return {
        "docs_dir": DOCS_DIR,
        "exists": exists,
        "supported_files_count": len(file_list),
        "files": file_list[:100],
    }


@app.post("/index")
async def index_docs(req: IndexRequest, _=Depends(_check_auth)):
    ns = _resolve_namespace(req.namespace)
    async with ns.lock:
        # Populate raw documents for TreeRetriever
        for doc in req.documents:
            text = (doc.get("text") or "").strip()
            if not text:
                continue
            doc_id = doc["id"]
            title = doc.get("title") or doc_id
            ns.raw_docs[doc_id] = _Doc(title=title, text=text)

            # Chunk into corpus for VectorRetriever
            chunks = _chunk_text(text)
            for idx, c in enumerate(chunks):
                cid = f"{doc_id}#{idx+1}" if len(chunks) > 1 else doc_id
                ctitle = f"{title} [{idx+1}/{len(chunks)}]" if len(chunks) > 1 else title
                ns.corpus[cid] = _Doc(title=ctitle, text=c)

        await asyncio.get_event_loop().run_in_executor(None, _build_sync, req.namespace, ns)
    return {"raw_docs": len(ns.raw_docs), "chunks": len(ns.corpus), "trees": len(ns.trees)}


@app.post("/index-dir")
async def index_directory(req: IndexDirRequest, _=Depends(_check_auth)):
    if not os.path.isdir(req.dir_path):
        raise HTTPException(400, f"Directory '{req.dir_path}' does not exist.")
    ns = _resolve_namespace(req.namespace)
    chunks, raw = scan_directory_docs(req.dir_path, recursive=req.recursive)
    if not chunks and not raw:
        return {"indexed": 0, "message": "No valid documents found in directory."}

    async with ns.lock:
        if req.clear_existing:
            ns.corpus.clear()
            ns.raw_docs.clear()
            ns.trees.clear()

        ns.raw_docs.update(raw)
        for d in chunks:
            ns.corpus[d["id"]] = _Doc(title=d["title"], text=d["text"])

        await asyncio.get_event_loop().run_in_executor(None, _build_sync, req.namespace, ns)

    return {
        "indexed_documents": len(raw),
        "indexed_chunks": len(chunks),
        "total_documents": len(ns.raw_docs),
        "total_chunks": len(ns.corpus),
        "total_trees": len(ns.trees),
    }


@app.post("/retrieve")
async def retrieve_context(req: RetrieveRequest, _=Depends(_check_auth)):
    ns = _resolve_namespace(req.namespace)

    if ns.index is None and ns.tree_retriever is None:
        return {"context": None, "sources": [], "dropped_count": 0, "strategy": "empty"}

    top_k = max(1, req.top_k)
    char_budget = req.token_budget * 4

    strategy = "fallback"
    selected_shape = "vector"
    routing_reasoning = ""
    parts: list[str] = []
    sources: list[str] = []
    dropped = 0
    remaining = char_budget
    seen_content_hashes: set[int] = set()

    if ASP_AVAILABLE and _global_router is not None:
        decision = _global_router.route(req.query)
        detected_shapes = _global_router.detect_shapes(req.query)
        selected_shape = decision.selected_shape.value
        routing_reasoning = decision.reasoning

        is_tree_query = (
            decision.selected_shape == RetrievalShape.document_tree
            or RetrievalShape.document_tree in detected_shapes
        )
        is_multi_shape = len(detected_shapes) > 1

        tree_nodes = []
        if is_tree_query and ns.tree_retriever is not None:
            strategy = "asp_tree_4stage"
            tree_nodes = ns.tree_retriever.retrieve(req.query, k=top_k)

        vector_doc_ids = []
        if (not is_tree_query or is_multi_shape or not tree_nodes) and ns.index and ns.index != "fallback":
            if not strategy.startswith("asp_tree"):
                strategy = "asp_vector_csdr"
            pool = min(200, len(ns.corpus))
            ctx = RetrievalContext(
                index=ns.index,
                model_name=MODEL_NAME,
                params={"pool": pool, "gamma": 0.25, "centroid_k": pool},
            )
            # Use Centroid-Subtracted Dense Reranking (CSDR / APEX) with small-corpus guard
            vector_doc_ids = retrieve("csdr", req.query, ctx, top_k)

        if is_multi_shape and tree_nodes and vector_doc_ids:
            strategy = "asp_multi_shape_fanout"

        # 1. Format Tree Nodes (Path-augmented with Injected Tables / Ancestors)
        for node in tree_nodes:
            hdr = node.heading_path or node.title
            tag = " [Child Table]" if getattr(node, "node_type", None) == NodeType.table else ""
            chunk = f"[{hdr}{tag}]\n{node.content}"
            chash = hash(node.content.strip())
            if chash in seen_content_hashes:
                continue
            seen_content_hashes.add(chash)

            if len(chunk) <= remaining:
                parts.append(chunk)
                sources.append(f"{hdr}{tag}")
                remaining -= len(chunk)
            else:
                dropped += 1

        # 2. Format Vector Chunks
        for doc_id in vector_doc_ids:
            doc = ns.corpus.get(doc_id)
            if not doc:
                continue
            chunk = f"[{doc.title}]\n{doc.text}"
            chash = hash(doc.text.strip())
            if chash in seen_content_hashes:
                continue
            seen_content_hashes.add(chash)

            if len(chunk) <= remaining:
                parts.append(chunk)
                sources.append(doc.title)
                remaining -= len(chunk)
            else:
                dropped += 1

    # 3. Fallback Keyword Match if no ASP results or in fallback mode
    if not parts and ns.corpus:
        strategy = "keyword_fallback"
        query_words = set(req.query.lower().split())
        scored = []
        for doc_id, doc in ns.corpus.items():
            text_lower = doc.text.lower()
            score = sum(1 for w in query_words if w in text_lower)
            scored.append((score, doc_id, doc))
        scored.sort(key=lambda x: x[0], reverse=True)

        for _, _, doc in scored[:top_k]:
            chunk = f"[{doc.title}]\n{doc.text}"
            if len(chunk) <= remaining:
                parts.append(chunk)
                sources.append(doc.title)
                remaining -= len(chunk)
            else:
                dropped += 1

    if not parts:
        return {
            "context": None,
            "sources": [],
            "dropped_count": dropped,
            "strategy": strategy,
            "selected_shape": selected_shape,
            "routing_reasoning": routing_reasoning,
        }

    label = "MEETING MEMORY" if req.namespace == "chum_session" else "REFERENCE DOCUMENTS (RAG)"
    context = (
        f"--- {label} ---\n"
        + "\n\n".join(parts)
        + f"\n--- END {label} ---"
    )

    return {
        "context": context,
        "sources": sources,
        "dropped_count": dropped,
        "strategy": strategy,
        "selected_shape": selected_shape,
        "routing_reasoning": routing_reasoning,
    }


@app.post("/clean")
async def clean_rag_data(req: CleanRequest = CleanRequest(), _=Depends(_check_auth)):
    """Zero-footprint cleanup: wipes in-memory indexes, trees, corpora, and temporary staging artifacts."""
    cleared_ns = []
    for name in req.namespaces:
        if name in _namespaces:
            ns = _namespaces[name]
            async with ns.lock:
                ns.corpus.clear()
                ns.raw_docs.clear()
                ns.trees.clear()
                ns.tree_retriever = None
                ns.index = None
            cleared_ns.append(name)
            print(f"[asp-rag] Cleared namespace '{name}' (0 docs, 0 trees, index reset)", flush=True)

    staged_removed = 0
    if req.clean_staging and STAGING_DIR and os.path.isdir(STAGING_DIR):
        try:
            for item in os.listdir(STAGING_DIR):
                item_path = os.path.join(STAGING_DIR, item)
                if os.path.isfile(item_path) or os.path.islink(item_path):
                    os.unlink(item_path)
                    staged_removed += 1
                elif os.path.isdir(item_path):
                    shutil.rmtree(item_path)
                    staged_removed += 1
            if staged_removed > 0:
                print(f"[asp-rag] Removed {staged_removed} staged items from {STAGING_DIR}", flush=True)
        except Exception as ex:
            print(f"[asp-rag] Error cleaning staging directory: {ex}", flush=True)

    return {
        "status": "cleaned",
        "cleared_namespaces": cleared_ns,
        "staged_items_removed": staged_removed,
    }


if __name__ == "__main__":
    host = os.environ.get("BRIDGE_HOST", "0.0.0.0")
    port = int(os.environ.get("BRIDGE_PORT", "8003"))
    print(f"[asp-rag] Listening on http://{host}:{port}", flush=True)
    uvicorn.run(app, host=host, port=port)
