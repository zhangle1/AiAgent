"""Knowledge Lab MVP: a small, inspectable layered knowledge-base service."""

from __future__ import annotations

import asyncio
import re
from collections import Counter
from datetime import datetime, timezone
from typing import Literal
from uuid import uuid4

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

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


resources: dict[str, ResourceSummary] = {}
nodes_by_resource: dict[str, list[Node]] = {}
tasks: dict[str, TaskView] = {}

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


def build_nodes(resource: ResourceSummary, content: str) -> list[Node]:
    sentences = sentence_parts(content)
    abstract = " ".join(sentences[:1])[:180]
    overview = " ".join(sentences[: min(4, len(sentences))])[:900]
    chunks: list[Node] = []
    buffer: list[str] = []
    current_length = 0
    for sentence in sentences:
        if buffer and current_length + len(sentence) > 420:
            chunk_no = len(chunks) + 1
            chunks.append(
                Node(
                    id=f"{resource.id}:l2:{chunk_no}", resource_id=resource.id, level=2,
                    title=f"内容块 {chunk_no}", content=" ".join(buffer), chunk_no=chunk_no,
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
                title=f"内容块 {chunk_no}", content=" ".join(buffer), chunk_no=chunk_no,
            )
        )
    return [
        Node(id=f"{resource.id}:l0", resource_id=resource.id, level=0, title="L0 摘要", content=abstract),
        Node(id=f"{resource.id}:l1", resource_id=resource.id, level=1, title="L1 概览", content=overview),
        *chunks,
    ]


async def index_resource(resource_id: str, content: str, task_id: str) -> None:
    task = tasks[task_id]
    resource = resources[resource_id]
    try:
        resource.status = "processing"
        task.status, task.stage, task.progress, task.message = "processing", "parse", 20, "正在解析资源内容"
        await asyncio.sleep(0.35)
        task.stage, task.progress, task.message = "summarize", 55, "正在生成 L0 摘要与 L1 概览"
        await asyncio.sleep(0.35)
        task.stage, task.progress, task.message = "index", 80, "正在切分 L2 内容块并建立索引"
        nodes_by_resource[resource_id] = build_nodes(resource, content)
        resource.node_count = len(nodes_by_resource[resource_id])
        resource.status = "ready"
        task.status, task.stage, task.progress, task.message = "completed", "done", 100, "索引已激活"
    except Exception as exc:  # pragma: no cover - UI demo fallback
        resource.status = "failed"
        task.status, task.stage, task.message = "failed", "error", str(exc)


def resource_or_404(resource_id: str) -> ResourceSummary:
    resource = resources.get(resource_id)
    if resource is None:
        raise HTTPException(status_code=404, detail="Resource not found")
    return resource


@app.on_event("startup")
async def seed_demo() -> None:
    if resources:
        return
    await create_resource(SEED)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}


@app.get("/api/resources", response_model=list[ResourceSummary])
async def list_resources() -> list[ResourceSummary]:
    return sorted(resources.values(), key=lambda item: item.created_at, reverse=True)


@app.get("/api/resources/{resource_id}/nodes", response_model=list[Node])
async def list_nodes(resource_id: str) -> list[Node]:
    resource_or_404(resource_id)
    return nodes_by_resource.get(resource_id, [])


@app.post("/api/resources", response_model=ResourceSummary, status_code=202)
async def create_resource(payload: ResourceCreate) -> ResourceSummary:
    resource = ResourceSummary(
        id=str(uuid4()), title=payload.title.strip(), source=payload.source.strip() or "manual",
        status="queued", created_at=now(),
    )
    task = TaskView(
        id=str(uuid4()), resource_id=resource.id, status="queued", stage="queued", progress=0,
        message="等待索引任务执行",
    )
    resources[resource.id] = resource
    tasks[task.id] = task
    asyncio.create_task(index_resource(resource.id, payload.content.strip(), task.id))
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
