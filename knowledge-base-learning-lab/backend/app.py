"""Knowledge Lab MVP: a small, inspectable layered knowledge-base service."""

from __future__ import annotations

import asyncio
import json
import os
import random
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Literal
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from uuid import uuid4

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from cryptography.fernet import Fernet, InvalidToken
from pydantic import BaseModel, Field

from storage import create_store, load_settings

BASE_DIRECTORY = Path(__file__).resolve().parent
settings = load_settings(BASE_DIRECTORY)
store = create_store(settings, BASE_DIRECTORY)

app = FastAPI(title="Knowledge Lab MVP", version="0.1.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


class ResourceCreate(BaseModel):
    title: str = Field(min_length=2, max_length=120)
    source: str = Field(default="manual", max_length=120)
    content: str = Field(min_length=40, max_length=50_000)


class ResourceSummary(BaseModel):
    id: str
    title: str
    source: str
    status: Literal["queued", "processing", "ready", "failed"]
    node_count: int = 0
    created_at: datetime


class Node(BaseModel):
    id: str
    resource_id: str
    level: Literal[0, 1, 2]
    title: str
    content: str
    chunk_no: int | None = None
    generator: Literal["rule", "deepseek"] = "rule"


class TaskView(BaseModel):
    id: str
    resource_id: str
    status: Literal["queued", "processing", "completed", "failed"]
    stage: str
    progress: int
    message: str


class SearchRequest(BaseModel):
    query: str = Field(min_length=2, max_length=500)
    limit: int = Field(default=5, ge=1, le=10)


class SearchHit(BaseModel):
    resource_id: str
    resource_title: str
    level: Literal[0, 1, 2]
    title: str
    excerpt: str
    score: float
    reason: str


class LlmConfigUpdate(BaseModel):
    mode: Literal["rule", "deepseek"] = "rule"
    model: str = Field(default="deepseek-chat", min_length=1, max_length=120)
    timeout_seconds: float = Field(default=45, ge=5, le=180)
    max_retries: int = Field(default=2, ge=0, le=4)


class LlmConfigView(LlmConfigUpdate):
    api_key_configured: bool
    endpoint: str


class LlmConfigRequest(LlmConfigUpdate):
    api_key: str | None = Field(default=None, max_length=500)


resources: dict[str, ResourceSummary] = {}
nodes_by_resource: dict[str, list[Node]] = {}
tasks: dict[str, TaskView] = {}
resource_contents: dict[str, str] = {}
llm_config = LlmConfigUpdate()
llm_api_key: str | None = None
llm_api_base = "https://api.deepseek.com"

DEFAULT_DEEPSEEK_API_BASE = "https://api.deepseek.com"


class DeepSeekRequestError(RuntimeError):
    def __init__(self, message: str, retryable: bool) -> None:
        super().__init__(message)
        self.retryable = retryable

SEED = ResourceCreate(
    title="知识库软件的最小设计",
    source="seed://architecture-notes",
    content=(
        "知识库软件应将权限、资源元数据、解析产物、索引版本和检索证据分开管理。"
        "写入链路先接收受控来源，再解析为标准化 Markdown 与来源清单。"
        "随后生成目录摘要、文档概览和可引用内容块，并为每一层建立检索索引。"
        "查询链路先按用户和项目过滤可访问范围，再用关键词和向量召回候选，最后重排并控制上下文预算。"
        "任何回答都应携带文档、章节或页码证据；证据不足时应明确说明。"
    ),
)


def now() -> datetime:
    return datetime.now(timezone.utc)


def terms(text: str) -> list[str]:
    """Return English words plus Chinese phrases and bigrams for a transparent demo scorer."""
    normalized = text.lower()
    tokens = re.findall(r"[a-zA-Z][a-zA-Z0-9_-]{1,}", normalized)
    for phrase in re.findall(r"[\u4e00-\u9fff]{2,}", normalized):
        tokens.append(phrase)
        tokens.extend(phrase[index : index + 2] for index in range(len(phrase) - 1))
    return tokens


def sentence_parts(text: str) -> list[str]:
    return [item.strip() for item in re.split(r"(?<=[。！？.!?])\s*", text) if item.strip()]


def build_nodes(
    resource: ResourceSummary,
    content: str,
    abstract: str | None = None,
    overview: str | None = None,
    summary_generator: Literal["rule", "deepseek"] = "rule",
) -> list[Node]:
    sentences = sentence_parts(content)
    abstract = (abstract or " ".join(sentences[:1]))[:180]
    overview = (overview or " ".join(sentences[: min(4, len(sentences))]))[:900]
    chunks: list[Node] = []
    buffer: list[str] = []
    current_length = 0
    for sentence in sentences:
        if buffer and current_length + len(sentence) > 420:
            chunk_no = len(chunks) + 1
            chunks.append(
                Node(
                    id=f"{resource.id}:l2:{chunk_no}", resource_id=resource.id, level=2,
                    title=f"内容块 {chunk_no}", content=" ".join(buffer), chunk_no=chunk_no, generator="rule",
                )
            )
            buffer, current_length = [], 0
        buffer.append(sentence)
        current_length += len(sentence)
    if buffer:
        chunk_no = len(chunks) + 1
        chunks.append(
            Node(
                id=f"{resource.id}:l2:{chunk_no}", resource_id=resource.id, level=2,
                title=f"内容块 {chunk_no}", content=" ".join(buffer), chunk_no=chunk_no, generator="rule",
            )
        )
    return [
        Node(id=f"{resource.id}:l0", resource_id=resource.id, level=0, title="L0 摘要", content=abstract, generator=summary_generator),
        Node(id=f"{resource.id}:l1", resource_id=resource.id, level=1, title="L1 概览", content=overview, generator=summary_generator),
        *chunks,
    ]


def deepseek_config_view() -> LlmConfigView:
    return LlmConfigView(
        **llm_config.model_dump(),
        api_key_configured=bool(llm_api_key),
        endpoint=f"{llm_api_base.rstrip('/')}/chat/completions",
    )


def fernet() -> Fernet:
    env_name = str((settings.get("security") or {}).get("api_key_encryption_key_env") or "").strip()
    raw_key = os.environ.get(env_name) if env_name else None
    if not raw_key:
        raise HTTPException(status_code=409, detail=f"请先设置用于加密数据库密钥的环境变量 {env_name or 'KNOWLEDGE_LAB_CONFIG_KEY'} 并重启后端。")
    try:
        return Fernet(raw_key.encode("utf-8"))
    except ValueError as exc:
        raise HTTPException(status_code=409, detail="数据库密钥加密环境变量不是有效的 Fernet key。") from exc


def compact_text(value: str, limit: int) -> str:
    return re.sub(r"\s+", " ", value).strip()[:limit]


def deepseek_call_sync(prompt: str) -> str:
    if not llm_api_key:
        raise DeepSeekRequestError("DeepSeek API Key 尚未写入数据库配置。", retryable=False)
    payload = json.dumps(
        {
            "model": llm_config.model,
            "temperature": 0,
            "messages": [
                {"role": "system", "content": "你是知识库的上下文摘要器。只输出要求的中文正文，不要解释你的工作。"},
                {"role": "user", "content": prompt},
            ],
        },
        ensure_ascii=False,
    ).encode("utf-8")
    request = Request(
        f"{llm_api_base.rstrip('/')}/chat/completions",
        data=payload,
        headers={"Authorization": f"Bearer {llm_api_key}", "Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlopen(request, timeout=llm_config.timeout_seconds) as response:
            body = json.loads(response.read().decode("utf-8"))
    except HTTPError as exc:
        retryable = exc.code == 429 or exc.code >= 500
        raise DeepSeekRequestError(f"DeepSeek 返回 HTTP {exc.code}。", retryable=retryable) from exc
    except (TimeoutError, URLError) as exc:
        raise DeepSeekRequestError("DeepSeek 网络请求超时或暂时不可达。", retryable=True) from exc
    try:
        return str(body["choices"][0]["message"]["content"]).strip()
    except (KeyError, IndexError, TypeError) as exc:
        raise DeepSeekRequestError("DeepSeek 返回了无法识别的响应格式。", retryable=False) from exc


async def update_task(task: TaskView, status: str, stage: str, progress: int, message: str) -> None:
    task.status, task.stage, task.progress, task.message = status, stage, progress, message
    await store.update_task(task.model_dump())


async def deepseek_completion(task: TaskView, stage: str, progress: int, prompt: str) -> str:
    for attempt in range(llm_config.max_retries + 1):
        await update_task(task, "processing", stage, progress, f"正在调用 DeepSeek 生成 {stage.upper()}（第 {attempt + 1} 次）。")
        try:
            return await asyncio.to_thread(deepseek_call_sync, prompt)
        except DeepSeekRequestError as exc:
            if not exc.retryable or attempt >= llm_config.max_retries:
                raise
            delay = min(8.0, 1.0 * (2**attempt)) + random.uniform(0, 0.3)
            await update_task(task, "processing", f"{stage}_retry", progress, f"{exc}，将在 {delay:.1f} 秒后重试。")
            await asyncio.sleep(delay)
    raise RuntimeError("DeepSeek 调用未返回结果。")


async def generate_deepseek_summaries(task: TaskView, content: str) -> tuple[str, str]:
    l0_prompt = f"请用一句话概括以下资源，让检索系统判断是否值得展开。最多 80 个中文字符。\n\n资源正文：\n{content}"
    abstract = compact_text(await deepseek_completion(task, "llm_l0", 45, l0_prompt), 180)
    l1_prompt = f"请概览以下资源的主题、结构和适用问题，保留关键约束和术语。最多 320 个中文字符。\n\n资源正文：\n{content}"
    overview = compact_text(await deepseek_completion(task, "llm_l1", 65, l1_prompt), 900)
    if not abstract or not overview:
        raise DeepSeekRequestError("DeepSeek 返回了空摘要。", retryable=False)
    return abstract, overview


async def index_resource(resource_id: str, content: str, task_id: str) -> None:
    task = tasks[task_id]
    resource = resources[resource_id]
    try:
        resource.status = "processing"
        await store.update_resource(resource.model_dump())
        await update_task(task, "processing", "parse", 20, "正在解析资源内容")
        await asyncio.sleep(0.35)
        if llm_config.mode == "deepseek":
            abstract, overview = await generate_deepseek_summaries(task, content)
            summary_generator: Literal["rule", "deepseek"] = "deepseek"
        else:
            await update_task(task, "processing", "summarize", 55, "使用规则生成 L0 摘要与 L1 概览")
            await asyncio.sleep(0.35)
            abstract, overview, summary_generator = None, None, "rule"
        await update_task(task, "processing", "index", 80, "正在切分 L2 内容块并建立索引")
        nodes_by_resource[resource_id] = build_nodes(resource, content, abstract, overview, summary_generator)
        await store.replace_nodes(resource_id, [node.model_dump() for node in nodes_by_resource[resource_id]])
        resource.node_count = len(nodes_by_resource[resource_id])
        resource.status = "ready"
        await store.update_resource(resource.model_dump())
        await update_task(task, "completed", "done", 100, "索引已激活")
    except Exception as exc:  # pragma: no cover - UI demo fallback
        resource.status = "failed"
        await store.update_resource(resource.model_dump())
        await update_task(task, "failed", "error", task.progress, str(exc)[:900])


def resource_or_404(resource_id: str) -> ResourceSummary:
    resource = resources.get(resource_id)
    if resource is None:
        raise HTTPException(status_code=404, detail="Resource not found")
    return resource


async def restore_persisted_state() -> None:
    global llm_config, llm_api_key, llm_api_base
    await store.initialize()
    state = await store.load_state()
    for item in state["resources"]:
        resource = ResourceSummary(
            id=str(item["Id"]), title=item["Title"], source=item["Source"], status=item["Status"],
            node_count=item["NodeCount"], created_at=item["CreatedAt"],
        )
        resources[resource.id] = resource
        resource_contents[resource.id] = item["Content"]
    for item in state["tasks"]:
        task = TaskView(
            id=str(item["Id"]), resource_id=str(item["ResourceId"]), status=item["Status"], stage=item["Stage"],
            progress=item["Progress"], message=item["Message"],
        )
        tasks[task.id] = task
    for item in state["nodes"]:
        node = Node(
            id=item["Id"], resource_id=str(item["ResourceId"]), level=item["Level"], title=item["Title"],
            content=item["Content"], chunk_no=item["ChunkNo"], generator=item["Generator"],
        )
        nodes_by_resource.setdefault(node.resource_id, []).append(node)
    provider = state.get("provider")
    if provider is not None:
        llm_config = LlmConfigUpdate(
            mode="deepseek" if provider["IsEnabled"] else "rule",
            model=provider["Model"], timeout_seconds=provider["TimeoutSeconds"], max_retries=provider["MaxRetries"],
        )
        llm_api_base = provider["ApiBase"] or DEFAULT_DEEPSEEK_API_BASE
        if provider.get("ApiKeyEncrypted"):
            try:
                llm_api_key = fernet().decrypt(provider["ApiKeyEncrypted"].encode("utf-8")).decode("utf-8")
            except InvalidToken as exc:
                raise RuntimeError("无法解密数据库中的 DeepSeek API Key，请检查 KNOWLEDGE_LAB_CONFIG_KEY。") from exc
    for task in list(tasks.values()):
        if task.status in {"queued", "processing"} and task.resource_id in resource_contents:
            task.status, task.stage, task.progress, task.message = "queued", "queued", 0, "服务重启后重新进入索引队列"
            await store.update_task(task.model_dump())
            asyncio.create_task(index_resource(task.resource_id, resource_contents[task.resource_id], task.id))


@app.on_event("startup")
async def seed_demo() -> None:
    await restore_persisted_state()
    if resources:
        return
    await create_resource(SEED)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok", "storage": str((settings.get("storage") or {}).get("mode") or "memory")}


@app.get("/api/llm/config", response_model=LlmConfigView)
async def get_llm_config() -> LlmConfigView:
    return deepseek_config_view()


@app.put("/api/llm/config", response_model=LlmConfigView)
async def update_llm_config(payload: LlmConfigRequest) -> LlmConfigView:
    global llm_config, llm_api_key
    storage_mode = str((settings.get("storage") or {}).get("mode") or "memory")
    encrypted_key = None
    normalized_key = payload.api_key.strip() if payload.api_key else ""
    if normalized_key:
        if storage_mode != "sqlserver":
            raise HTTPException(status_code=409, detail="请先在 config.local.json 启用 SQL Server 模式，再把 DeepSeek API Key 写入数据库。")
        encrypted_key = fernet().encrypt(normalized_key.encode("utf-8")).decode("utf-8")
    llm_config = LlmConfigUpdate(
        mode=payload.mode, model=payload.model.strip(), timeout_seconds=payload.timeout_seconds, max_retries=payload.max_retries,
    )
    if normalized_key:
        llm_api_key = normalized_key
    await store.save_provider(
        {
            "provider": "deepseek", "is_enabled": llm_config.mode == "deepseek", "model": llm_config.model,
            "api_base": llm_api_base, "api_key_encrypted": encrypted_key,
            "timeout_seconds": int(llm_config.timeout_seconds), "max_retries": llm_config.max_retries,
        }
    )
    return deepseek_config_view()


@app.get("/api/resources", response_model=list[ResourceSummary])
async def list_resources() -> list[ResourceSummary]:
    return sorted(resources.values(), key=lambda item: item.created_at, reverse=True)


@app.get("/api/resources/{resource_id}/nodes", response_model=list[Node])
async def list_nodes(resource_id: str) -> list[Node]:
    resource_or_404(resource_id)
    return nodes_by_resource.get(resource_id, [])


@app.post("/api/resources", response_model=ResourceSummary, status_code=202)
async def create_resource(payload: ResourceCreate) -> ResourceSummary:
    if llm_config.mode == "deepseek" and not llm_api_key:
        raise HTTPException(status_code=409, detail="DeepSeek 模式已启用，但数据库中没有可用的 API Key。请先保存 LLM 配置。")
    resource = ResourceSummary(
        id=str(uuid4()), title=payload.title.strip(), source=payload.source.strip() or "manual",
        status="queued", created_at=now(),
    )
    task = TaskView(
        id=str(uuid4()), resource_id=resource.id, status="queued", stage="queued", progress=0,
        message="等待索引任务执行",
    )
    resources[resource.id] = resource
    resource_contents[resource.id] = payload.content.strip()
    tasks[task.id] = task
    await store.create_resource(resource.model_dump(), resource_contents[resource.id])
    await store.create_task(task.model_dump())
    asyncio.create_task(index_resource(resource.id, resource_contents[resource.id], task.id))
    return resource


@app.get("/api/tasks", response_model=list[TaskView])
async def list_tasks() -> list[TaskView]:
    return list(tasks.values())[-12:][::-1]


@app.post("/api/search", response_model=list[SearchHit])
async def search(payload: SearchRequest) -> list[SearchHit]:
    query_terms = terms(payload.query)
    if not query_terms:
        return []
    query_counts = Counter(query_terms)
    directory_scores: list[tuple[str, float]] = []
    for resource_id, node_list in nodes_by_resource.items():
        overview = next((node for node in node_list if node.level == 1), None)
        if overview is None:
            continue
        score = sum(min(query_counts[term], terms(overview.content).count(term)) for term in query_counts)
        if score:
            directory_scores.append((resource_id, float(score)))

    hits: list[SearchHit] = []
    for resource_id, directory_score in sorted(directory_scores, key=lambda item: item[1], reverse=True):
        resource = resources[resource_id]
        for node in nodes_by_resource[resource_id]:
            node_terms = terms(node.content)
            lexical_score = sum(min(query_counts[term], node_terms.count(term)) for term in query_counts)
            if lexical_score == 0:
                continue
            score = lexical_score + (directory_score * 0.25 if node.level == 2 else directory_score * 0.5)
            hits.append(
                SearchHit(
                    resource_id=resource_id, resource_title=resource.title, level=node.level, title=node.title,
                    excerpt=node.content[:280], score=round(score, 2),
                    reason="先命中文档概览，再在该资源内展开相关节点",
                )
            )
    return sorted(hits, key=lambda item: item.score, reverse=True)[: payload.limit]
