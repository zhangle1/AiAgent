import importlib.util
import html as html_lib
import json
import re
import shutil
import sys
import traceback
from pathlib import Path


PROVIDER = "llamaindex"


def configure_stdio():
    """确保 worker 与 C# 进程之间的 JSON 通信始终使用 UTF-8。"""
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")


def main():
    """Worker 入口：读取 C# stdin 传入的 JSON payload，并按 command 分发到具体动作。"""
    configure_stdio()
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
    """检查 LlamaIndex worker 的 Python 依赖和运行环境，供后端环境检测接口使用。"""
    dependencies = {
        "llama_index.core": module_available("llama_index.core"),
        "llama_index.embeddings.openai": module_available("llama_index.embeddings.openai"),
        "llama_index.embeddings.ollama": module_available("llama_index.embeddings.ollama"),
        "fitz": module_available("fitz"),
    }
    required = [
        dependencies["llama_index.core"],
        dependencies["llama_index.embeddings.openai"] or dependencies["llama_index.embeddings.ollama"],
    ]
    return {
        "ok": all(required),
        "provider": PROVIDER,
        "dependencies": dependencies,
        "python": sys.version.split()[0],
    }


def initialize(payload):
    """首次构建索引：加载文档、创建 embedding 模型、分块、生成向量索引并持久化。"""
    emit_progress("checking", 5, "Checking LlamaIndex environment.")
    ensure_dependencies()
    persist_dir = require_path(payload, "persist_dir")
    file_paths = [str(Path(x)) for x in payload.get("file_paths") or []]
    if not file_paths:
        return error("empty_documents", "No input files were provided.")

    from llama_index.core import VectorStoreIndex
    from llama_index.core.node_parser import SentenceSplitter

    documents = load_documents(file_paths)
    emit_progress("embedding", 45, f"Preparing embedding model for {len(documents)} loaded pages or documents.")
    embed_model = build_embed_model(payload.get("embedding") or {})
    retrieval = normalize_retrieval(payload.get("retrieval") or {})
    splitter = SentenceSplitter(
        chunk_size=retrieval["chunk_size"],
        chunk_overlap=retrieval["chunk_overlap"],
    )
    emit_progress("indexing", 55, "Building vector index.")
    index = VectorStoreIndex.from_documents(
        documents,
        embed_model=embed_model,
        transformations=[splitter],
    )
    emit_progress("persisting", 88, "Persisting vector index.")
    Path(persist_dir).mkdir(parents=True, exist_ok=True)
    index.storage_context.persist(persist_dir=str(persist_dir))
    export_chunks(index, persist_dir)
    emit_progress("done", 92, "Vector index persisted.")
    return success(
        "initialize",
        persist_dir,
        document_count=len(file_paths),
        chunk_count=count_index_docs(index),
    )


def add_documents(payload):
    """向已有索引追加文档：加载新文档并插入现有 LlamaIndex 索引后重新持久化。"""
    emit_progress("checking", 5, "Checking LlamaIndex environment.")
    ensure_dependencies()
    persist_dir = require_path(payload, "persist_dir")
    file_paths = [str(Path(x)) for x in payload.get("file_paths") or []]
    if not file_paths:
        return error("empty_documents", "No input files were provided.")

    documents = load_documents(file_paths)
    emit_progress("loading_index", 45, "Loading existing vector index.")
    index = load_index(persist_dir, payload)
    total = max(1, len(documents))
    for index_no, document in enumerate(documents, start=1):
        index.insert(document)
        progress = 45 + int(index_no / total * 35)
        emit_progress("inserting", progress, f"Inserted {index_no}/{total} pages or documents.")
    emit_progress("persisting", 88, "Persisting vector index.")
    index.storage_context.persist(persist_dir=str(persist_dir))
    export_chunks(index, persist_dir)
    emit_progress("done", 92, "Vector index persisted.")
    return success(
        "add_documents",
        persist_dir,
        document_count=len(file_paths),
        chunk_count=count_index_docs(index),
    )


def reindex(payload):
    """重建索引：删除当前版本的旧索引目录，然后复用 initialize 完整重建。"""
    persist_dir = require_path(payload, "persist_dir")
    if Path(persist_dir).exists():
        emit_progress("cleanup", 12, "Removing old vector index.")
        shutil.rmtree(persist_dir)
    return initialize(payload)


def search(payload):
    """执行检索：加载已持久化的索引，根据 query 召回相似 chunk 并返回引用片段。"""
    ensure_dependencies()
    persist_dir = require_path(payload, "persist_dir")
    query = (payload.get("query") or "").strip()
    if not query:
        return error("empty_query", "Query is required.")

    index = load_index(persist_dir, payload)
    retrieval = normalize_retrieval(payload.get("retrieval") or {})
    top_k = int(payload.get("top_k") or retrieval["top_k"] or 5)
    candidate_multiplier = retrieval["vector_candidate_multiplier"]
    retriever = index.as_retriever(similarity_top_k=max(1, top_k * candidate_multiplier))
    nodes = retriever.retrieve(query)
    citations = []
    content_parts = []
    for node in nodes:
        text = node.get_content() if hasattr(node, "get_content") else str(node)
        snippet = text.strip()
        if not snippet:
            continue

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
    """从 persist_dir 加载已保存的 LlamaIndex 索引，并绑定当前 embedding 模型。"""
    from llama_index.core import Settings, StorageContext, load_index_from_storage

    embed_model = build_embed_model(payload.get("embedding") or {})
    Settings.embed_model = embed_model
    storage_context = StorageContext.from_defaults(persist_dir=str(persist_dir))
    return load_index_from_storage(storage_context, embed_model=embed_model)


def load_documents(file_paths):
    """加载源文件：PDF 优先用 PyMuPDF 按页转 Document，其他文件交给 SimpleDirectoryReader。"""
    from llama_index.core import Document, SimpleDirectoryReader

    total_units = count_load_units(file_paths)
    loaded_units = 0
    documents = []
    emit_progress("loading", 15, f"Loading {len(file_paths)} source files.")

    for file_path in file_paths:
        path = Path(file_path)
        if path.suffix.lower() == ".pdf" and module_available("fitz"):
            import fitz

            with fitz.open(str(path)) as pdf:
                page_count = len(pdf)
                for page_index in range(page_count):
                    page = pdf.load_page(page_index)
                    text = extract_pdf_page_text(page)
                    documents.append(
                        Document(
                            text=text,
                            metadata={
                                "file_path": str(path),
                                "file_name": path.name,
                                "page_label": page_index + 1,
                            },
                        )
                    )
                    loaded_units += 1
                    progress = 15 + int(loaded_units / max(1, total_units) * 25)
                    emit_progress("loading", progress, f"Loaded PDF page {page_index + 1}/{page_count}: {path.name}")
            continue

        documents.extend(SimpleDirectoryReader(input_files=[str(path)]).load_data())
        loaded_units += 1
        progress = 15 + int(loaded_units / max(1, total_units) * 25)
        emit_progress("loading", progress, f"Loaded file {loaded_units}/{total_units}: {path.name}")

    return documents


def extract_pdf_page_text(page):
    """从 PDF 页面提取文本；普通 text 乱码时回退到 HTML 实体文本。"""
    text = page.get_text("text") or ""
    if not looks_garbled(text):
        return text

    html_text = page.get_text("html") or ""
    fallback = html_to_plain_text(html_text)
    return fallback if fallback.strip() else text


def looks_garbled(text):
    """判断抽取文本是否疑似乱码，主要检测 Unicode replacement 字符占比。"""
    stripped = text.strip()
    if not stripped:
        return False

    replacement_count = stripped.count("\ufffd")
    if replacement_count == 0:
        return False

    return replacement_count / max(1, len(stripped)) > 0.08


def html_to_plain_text(html_text):
    """把 PyMuPDF HTML 输出中的实体字符还原为纯文本。"""
    value = re.sub(r"</p\s*>", "\n", html_text, flags=re.IGNORECASE)
    value = re.sub(r"<br\s*/?>", "\n", value, flags=re.IGNORECASE)
    value = re.sub(r"<[^>]+>", "", value)
    value = html_lib.unescape(value)
    lines = [line.strip() for line in value.splitlines()]
    return "\n".join(line for line in lines if line)


def count_load_units(file_paths):
    """估算加载进度总量：PDF 按页数统计，其他文件按文件数统计。"""
    total = 0
    for file_path in file_paths:
        path = Path(file_path)
        if path.suffix.lower() == ".pdf" and module_available("fitz"):
            try:
                import fitz

                with fitz.open(str(path)) as pdf:
                    total += max(1, len(pdf))
                continue
            except Exception:
                pass
        total += 1
    return max(1, total)


def build_embed_model(config):
    """根据后端传入的 embedding 配置创建 LlamaIndex embedding 模型实例。"""
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

    if provider == "ollama":
        from llama_index.embeddings.ollama import OllamaEmbedding

        return OllamaEmbedding(
            model_name=model,
            base_url=normalize_ollama_base_url(base_url),
        )

    raise RuntimeError(f"Unsupported embedding provider for LlamaIndex worker: {provider}")


def normalize_provider(provider):
    """统一 provider 标识格式，兼容 openai-compatible 这类横线命名。"""
    return str(provider or "").strip().replace("-", "_").lower()


def normalize_ollama_base_url(base_url):
    """将 Ollama embedding/chat 接口地址归一化为 Ollama 服务根地址。"""
    value = str(base_url or "http://localhost:11434").strip().rstrip("/")
    for suffix in ("/api/embed", "/api/embeddings", "/api/generate", "/api/chat", "/v1/embeddings", "/v1"):
        if value.lower().endswith(suffix):
            return value[: -len(suffix)].rstrip("/")
    return value


def require_path(payload, key):
    """从 payload 读取必填路径字段，缺失时抛出明确错误。"""
    value = payload.get(key)
    if not value:
        raise RuntimeError(f"{key} is required.")
    return Path(value)


def module_available(name):
    """检查指定 Python 模块是否可 import，避免环境检测时直接抛异常。"""
    try:
        return importlib.util.find_spec(name) is not None
    except ModuleNotFoundError:
        return False


def ensure_dependencies():
    """确认核心依赖可用；缺失时抛出异常阻止索引或检索继续执行。"""
    status = preflight()
    if not status["ok"]:
        missing = [name for name, ok in status["dependencies"].items() if not ok]
        raise RuntimeError("Missing Python dependencies: " + ", ".join(missing))


def count_index_docs(index):
    """统计 LlamaIndex docstore 中的节点数量，用作 chunk_count 展示。"""
    try:
        return len(index.docstore.docs)
    except Exception:
        return 0


def export_chunks(index, persist_dir):
    """将 LlamaIndex docstore 节点导出为 chunks.jsonl，供 C# 导入 ai_knowledge_chunk。"""
    output_path = Path(persist_dir) / "chunks.jsonl"
    chunk_no = 0
    with output_path.open("w", encoding="utf-8") as writer:
        for node in index.docstore.docs.values():
            text = node.get_content() if hasattr(node, "get_content") else getattr(node, "text", "") or ""
            text = str(text).strip()
            if not text:
                continue

            metadata = getattr(node, "metadata", {}) or {}
            chunk_no += 1
            writer.write(
                json.dumps(
                    {
                        "chunk_no": chunk_no,
                        "content": text,
                        "title": metadata.get("title") or metadata.get("section"),
                        "token_count": estimate_token_count(text),
                        "page_no": parse_int(metadata.get("page_label") or metadata.get("page_no")),
                        "file_path": metadata.get("file_path"),
                        "file_name": metadata.get("file_name"),
                        "metadata": metadata,
                    },
                    ensure_ascii=False,
                )
                + "\n"
            )


def estimate_token_count(text):
    """粗略估算 token 数，中文场景先按字符数折算，避免导入阶段依赖 tokenizer。"""
    stripped = text.strip()
    if not stripped:
        return 0
    return max(1, int(len(stripped) / 1.6))


def parse_int(value):
    """将配置值安全转换为 int，转换失败时返回 None。"""
    try:
        return int(value) if value not in (None, "") else None
    except (TypeError, ValueError):
        return None


def normalize_retrieval(config):
    """规范化检索和分块配置，给缺省值并修正非法 chunk 参数。"""
    chunk_size = parse_int(config.get("chunk_size")) or 512
    chunk_overlap = parse_int(config.get("chunk_overlap")) or 50
    if chunk_size < 64:
        chunk_size = 64
    if chunk_overlap < 0:
        chunk_overlap = 0
    if chunk_overlap >= chunk_size:
        chunk_overlap = max(0, chunk_size // 5)

    return {
        "retrieval_profile": str(config.get("retrieval_profile") or "hybrid").strip().lower(),
        "top_k": max(1, parse_int(config.get("top_k")) or 5),
        "vector_candidate_multiplier": max(1, parse_int(config.get("vector_candidate_multiplier")) or 2),
        "keyword_candidate_multiplier": max(1, parse_int(config.get("keyword_candidate_multiplier")) or 2),
        "chunk_size": chunk_size,
        "chunk_overlap": chunk_overlap,
    }


def success(action, persist_dir, document_count=0, chunk_count=0):
    """构造成功响应 JSON，返回给 C# Pipeline 解析。"""
    return {
        "ok": True,
        "provider": PROVIDER,
        "action": action,
        "persist_dir": str(persist_dir),
        "document_count": document_count,
        "chunk_count": chunk_count,
    }


def error(code, message, details=None):
    """构造失败响应 JSON，保留错误码、错误信息和可选诊断详情。"""
    return {
        "ok": False,
        "provider": PROVIDER,
        "error": {
            "code": code,
            "message": message,
            "details": details or {},
        },
    }


def emit_progress(stage, progress, message):
    """输出一行进度事件 JSON；C# 会逐行读取并通过 WebSocket 推给前端。"""
    payload = {
        "type": "progress",
        "stage": stage,
        "progress": max(0, min(99, int(progress))),
        "message": message,
    }
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def write_json(payload, exit_code):
    """输出最终结果 JSON 并按指定退出码结束 worker 进程。"""
    sys.stdout.write(json.dumps(payload, ensure_ascii=False))
    sys.exit(exit_code)


if __name__ == "__main__":
    main()
