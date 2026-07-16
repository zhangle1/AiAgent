import importlib.util
import json
import shutil
import sys
import traceback
from pathlib import Path


PROVIDER = "llamaindex"


def main():
    try:
        payload = json.loads(sys.stdin.read() or "{}")
        command = payload.get("command")
        if command == "preflight":
            write_json(preflight(), 0)
        if command == "initialize":
            write_json(initialize(payload), 0)
        if command == "add_documents":
            write_json(add_documents(payload), 0)
        if command == "reindex":
            write_json(reindex(payload), 0)
        if command == "search":
            write_json(search(payload), 0)

        write_json(error("unknown_command", f"Unknown command: {command}"), 2)
    except Exception as exc:
        write_json(
            error(
                "worker_exception",
                str(exc),
                {"traceback": traceback.format_exc(limit=8)},
            ),
            1,
        )


def preflight():
    dependencies = {
        "llama_index.core": module_available("llama_index.core"),
        "llama_index.embeddings.openai": module_available("llama_index.embeddings.openai"),
    }
    return {
        "ok": all(dependencies.values()),
        "provider": PROVIDER,
        "dependencies": dependencies,
        "python": sys.version.split()[0],
    }


def initialize(payload):
    ensure_dependencies()
    persist_dir = require_path(payload, "persist_dir")
    file_paths = [str(Path(x)) for x in payload.get("file_paths") or []]
    if not file_paths:
        return error("empty_documents", "No input files were provided.")

    from llama_index.core import SimpleDirectoryReader, VectorStoreIndex

    documents = SimpleDirectoryReader(input_files=file_paths).load_data()
    embed_model = build_embed_model(payload.get("embedding") or {})
    index = VectorStoreIndex.from_documents(documents, embed_model=embed_model)
    Path(persist_dir).mkdir(parents=True, exist_ok=True)
    index.storage_context.persist(persist_dir=str(persist_dir))
    return success(
        "initialize",
        persist_dir,
        document_count=len(file_paths),
        chunk_count=count_index_docs(index),
    )


def add_documents(payload):
    ensure_dependencies()
    persist_dir = require_path(payload, "persist_dir")
    file_paths = [str(Path(x)) for x in payload.get("file_paths") or []]
    if not file_paths:
        return error("empty_documents", "No input files were provided.")

    from llama_index.core import SimpleDirectoryReader

    index = load_index(persist_dir, payload)
    documents = SimpleDirectoryReader(input_files=file_paths).load_data()
    for document in documents:
        index.insert(document)
    index.storage_context.persist(persist_dir=str(persist_dir))
    return success(
        "add_documents",
        persist_dir,
        document_count=len(file_paths),
        chunk_count=count_index_docs(index),
    )


def reindex(payload):
    persist_dir = require_path(payload, "persist_dir")
    if Path(persist_dir).exists():
        shutil.rmtree(persist_dir)
    return initialize(payload)


def search(payload):
    ensure_dependencies()
    persist_dir = require_path(payload, "persist_dir")
    query = (payload.get("query") or "").strip()
    if not query:
        return error("empty_query", "Query is required.")

    index = load_index(persist_dir, payload)
    top_k = int(payload.get("top_k") or 5)
    retriever = index.as_retriever(similarity_top_k=max(1, top_k))
    nodes = retriever.retrieve(query)
    citations = []
    content_parts = []
    for node in nodes:
        text = node.get_content() if hasattr(node, "get_content") else str(node)
        snippet = text.strip()
        if len(snippet) > 1200:
            snippet = snippet[:1200].rstrip() + "..."
        metadata = getattr(node.node, "metadata", {}) if hasattr(node, "node") else {}
        score = getattr(node, "score", None)
        citations.append(
            {
                "score": score,
                "text": snippet,
                "metadata": metadata or {},
            }
        )
        content_parts.append(snippet)

    content = "\n\n".join(content_parts)
    return {
        "ok": True,
        "provider": PROVIDER,
        "action": "search",
        "query": query,
        "answer": content,
        "content": content,
        "citations": citations,
    }


def load_index(persist_dir, payload):
    from llama_index.core import Settings, StorageContext, load_index_from_storage

    embed_model = build_embed_model(payload.get("embedding") or {})
    Settings.embed_model = embed_model
    storage_context = StorageContext.from_defaults(persist_dir=str(persist_dir))
    return load_index_from_storage(storage_context, embed_model=embed_model)


def build_embed_model(config):
    provider = normalize_provider(config.get("provider") or config.get("binding"))
    model = config.get("model") or "text-embedding-3-small"
    api_key = config.get("api_key") or "EMPTY"
    base_url = config.get("base_url") or None
    dimensions = parse_int(config.get("dimension") or config.get("dimensions"))

    if provider in ("openai", "openai_compatible", "dashscope", "siliconflow", "jina", "openrouter", "gemini", "lm_studio", ""):
        from llama_index.embeddings.openai import OpenAIEmbedding

        kwargs = {
            "model": model,
            "api_key": api_key,
        }
        if base_url:
            kwargs["api_base"] = base_url.rstrip("/")
        if dimensions:
            kwargs["dimensions"] = dimensions
        return OpenAIEmbedding(**kwargs)

    raise RuntimeError(f"Unsupported embedding provider for LlamaIndex worker: {provider}")


def normalize_provider(provider):
    return str(provider or "").strip().replace("-", "_").lower()


def require_path(payload, key):
    value = payload.get(key)
    if not value:
        raise RuntimeError(f"{key} is required.")
    return Path(value)


def module_available(name):
    try:
        return importlib.util.find_spec(name) is not None
    except ModuleNotFoundError:
        return False


def ensure_dependencies():
    status = preflight()
    if not status["ok"]:
        missing = [name for name, ok in status["dependencies"].items() if not ok]
        raise RuntimeError("Missing Python dependencies: " + ", ".join(missing))


def count_index_docs(index):
    try:
        return len(index.docstore.docs)
    except Exception:
        return 0


def parse_int(value):
    try:
        return int(value) if value not in (None, "") else None
    except (TypeError, ValueError):
        return None


def success(action, persist_dir, document_count=0, chunk_count=0):
    return {
        "ok": True,
        "provider": PROVIDER,
        "action": action,
        "persist_dir": str(persist_dir),
        "document_count": document_count,
        "chunk_count": chunk_count,
    }


def error(code, message, details=None):
    return {
        "ok": False,
        "provider": PROVIDER,
        "error": {
            "code": code,
            "message": message,
            "details": details or {},
        },
    }


def write_json(payload, exit_code):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False))
    sys.exit(exit_code)


if __name__ == "__main__":
    main()
