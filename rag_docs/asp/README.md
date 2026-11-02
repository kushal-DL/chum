# Dedicated ASP RAG Documents Directory

Place reference documents here to be used for Chum's ASP RAG semantic retrieval.

## How It Works
- **Automatic Loading**: When `start-asp-rag.sh` (Linux) or `start-asp-rag.ps1` (Windows) is launched, all supported documents in this folder and **all subfolders** are recursively scanned, chunked, and indexed.
- **Subfolders**: You can organize documents by category (e.g., `rag_docs/architecture/`, `rag_docs/policies/`, `rag_docs/specs/`). All subdirectories are traversed recursively.
- **Supported Formats**:
  - **Documents & Markup**: `.pdf`, `.txt`, `.md`, `.markdown`, `.rst`, `.log`
  - **Code & Repositories**: `.py`, `.pyi`, `.cs`, `.xaml`, `.c`, `.cpp`, `.cc`, `.cxx`, `.h`, `.hpp`, `.rs`, `.go`, `.java`, `.kt`, `.sql`, `.graphql`
  - **Web & Frontend**: `.html`, `.htm`, `.css`, `.js`, `.jsx`, `.ts`, `.tsx`
  - **Data & Configuration**: `.json`, `.csv`, `.yaml`, `.yml`, `.toml`, `.xml`, `.ini`, `.sh`, `.bash`, `.ps1`, `.bat`, `.cmd`, `.dockerfile`
- **Dynamic Ingestion**: You can also dynamically index this folder or any other folder at runtime via `POST /index-dir` or via the Chum desktop app.
- **Zero-Footprint Cleanup**: Run `clean-rag-data.sh` (Linux) or `clean-rag-data.ps1` (Windows) — or click "Clear All / Clean RAG" in Chum — to wipe all in-memory chunks and reset the vector index with no leftover data.
