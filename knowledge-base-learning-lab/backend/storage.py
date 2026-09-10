"""Optional SQL Server persistence for the Knowledge Lab."""

from __future__ import annotations

import asyncio
import json
from pathlib import Path
from typing import Any


class MemoryStore:
    async def initialize(self) -> None:
        return None

    async def load_state(self) -> dict[str, Any]:
        return {"resources": [], "tasks": [], "nodes": [], "provider": None}

    async def create_resource(self, resource: dict[str, Any], content: str) -> None:
        return None

    async def update_resource(self, resource: dict[str, Any]) -> None:
        return None

    async def create_task(self, task: dict[str, Any]) -> None:
        return None

    async def update_task(self, task: dict[str, Any]) -> None:
        return None

    async def replace_nodes(self, resource_id: str, nodes: list[dict[str, Any]]) -> None:
        return None

    async def save_provider(self, provider: dict[str, Any]) -> None:
        return None


class SqlServerStore:
    def __init__(self, connection_string: str, schema_path: Path) -> None:
        self._connection_string = connection_string
        self._schema_path = schema_path

    def _connect(self):
        try:
            import pyodbc
        except ImportError as exc:
            raise RuntimeError("SQL Server mode requires pyodbc. Run the backend startup script again.") from exc
        return pyodbc.connect(self._connection_string, autocommit=False)

    async def initialize(self) -> None:
        await asyncio.to_thread(self._initialize_sync)

    def _initialize_sync(self) -> None:
        batches = [item.strip() for item in self._schema_path.read_text(encoding="utf-8").split("\nGO\n") if item.strip()]
        with self._connect() as connection:
            cursor = connection.cursor()
            for batch in batches:
                cursor.execute(batch)
            connection.commit()

    async def load_state(self) -> dict[str, Any]:
        return await asyncio.to_thread(self._load_state_sync)

    def _load_state_sync(self) -> dict[str, Any]:
        with self._connect() as connection:
            cursor = connection.cursor()
            resources = self._rows(cursor, "SELECT Id, Title, Source, Content, Status, NodeCount, CreatedAt FROM dbo.KnowledgeLabResource")
            tasks = self._rows(cursor, "SELECT Id, ResourceId, Status, Stage, Progress, Message FROM dbo.KnowledgeLabIndexJob")
            nodes = self._rows(cursor, "SELECT Id, ResourceId, [Level], Title, Content, ChunkNo, Generator FROM dbo.KnowledgeLabContextNode")
            provider = self._rows(cursor, "SELECT TOP 1 Provider, IsEnabled, Model, ApiBase, ApiKeyEncrypted, TimeoutSeconds, MaxRetries FROM dbo.KnowledgeLabLlmProvider WHERE Provider = 'deepseek'")
        return {"resources": resources, "tasks": tasks, "nodes": nodes, "provider": provider[0] if provider else None}

    @staticmethod
    def _rows(cursor, query: str) -> list[dict[str, Any]]:
        cursor.execute(query)
        names = [column[0] for column in cursor.description]
        return [dict(zip(names, row)) for row in cursor.fetchall()]

    async def create_resource(self, resource: dict[str, Any], content: str) -> None:
        await asyncio.to_thread(self._create_resource_sync, resource, content)

    def _create_resource_sync(self, resource: dict[str, Any], content: str) -> None:
        with self._connect() as connection:
            connection.cursor().execute(
                "INSERT INTO dbo.KnowledgeLabResource (Id, Title, Source, Content, Status, NodeCount, CreatedAt, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                resource["id"], resource["title"], resource["source"], content, resource["status"], resource["node_count"], resource["created_at"], resource["created_at"],
            )
            connection.commit()

    async def update_resource(self, resource: dict[str, Any]) -> None:
        await asyncio.to_thread(self._update_resource_sync, resource)

    def _update_resource_sync(self, resource: dict[str, Any]) -> None:
        with self._connect() as connection:
            connection.cursor().execute(
                "UPDATE dbo.KnowledgeLabResource SET Status = ?, NodeCount = ?, UpdatedAt = SYSUTCDATETIME() WHERE Id = ?",
                resource["status"], resource["node_count"], resource["id"],
            )
            connection.commit()

    async def create_task(self, task: dict[str, Any]) -> None:
        await asyncio.to_thread(self._create_task_sync, task)

    def _create_task_sync(self, task: dict[str, Any]) -> None:
        with self._connect() as connection:
            connection.cursor().execute(
                "INSERT INTO dbo.KnowledgeLabIndexJob (Id, ResourceId, Status, Stage, Progress, Message, CreatedAt, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?, SYSUTCDATETIME(), SYSUTCDATETIME())",
                task["id"], task["resource_id"], task["status"], task["stage"], task["progress"], task["message"],
            )
            connection.commit()

    async def update_task(self, task: dict[str, Any]) -> None:
        await asyncio.to_thread(self._update_task_sync, task)

    def _update_task_sync(self, task: dict[str, Any]) -> None:
        with self._connect() as connection:
            connection.cursor().execute(
                "UPDATE dbo.KnowledgeLabIndexJob SET Status = ?, Stage = ?, Progress = ?, Message = ?, UpdatedAt = SYSUTCDATETIME() WHERE Id = ?",
                task["status"], task["stage"], task["progress"], task["message"], task["id"],
            )
            connection.commit()

    async def replace_nodes(self, resource_id: str, nodes: list[dict[str, Any]]) -> None:
        await asyncio.to_thread(self._replace_nodes_sync, resource_id, nodes)

    def _replace_nodes_sync(self, resource_id: str, nodes: list[dict[str, Any]]) -> None:
        with self._connect() as connection:
            cursor = connection.cursor()
            cursor.execute("DELETE FROM dbo.KnowledgeLabContextNode WHERE ResourceId = ?", resource_id)
            cursor.executemany(
                "INSERT INTO dbo.KnowledgeLabContextNode (Id, ResourceId, [Level], Title, Content, ChunkNo, Generator, CreatedAt) VALUES (?, ?, ?, ?, ?, ?, ?, SYSUTCDATETIME())",
                [(node["id"], node["resource_id"], node["level"], node["title"], node["content"], node.get("chunk_no"), node["generator"]) for node in nodes],
            )
            connection.commit()

    async def save_provider(self, provider: dict[str, Any]) -> None:
        await asyncio.to_thread(self._save_provider_sync, provider)

    def _save_provider_sync(self, provider: dict[str, Any]) -> None:
        with self._connect() as connection:
            connection.cursor().execute(
                "MERGE dbo.KnowledgeLabLlmProvider AS target USING (SELECT ? AS Provider) AS source ON target.Provider = source.Provider WHEN MATCHED THEN UPDATE SET IsEnabled = ?, Model = ?, ApiBase = ?, ApiKeyEncrypted = COALESCE(?, target.ApiKeyEncrypted), TimeoutSeconds = ?, MaxRetries = ?, UpdatedAt = SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT (Provider, IsEnabled, Model, ApiBase, ApiKeyEncrypted, TimeoutSeconds, MaxRetries, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?, ?, SYSUTCDATETIME());",
                provider["provider"], provider["is_enabled"], provider["model"], provider["api_base"], provider.get("api_key_encrypted"), provider["timeout_seconds"], provider["max_retries"], provider["provider"], provider["is_enabled"], provider["model"], provider["api_base"], provider.get("api_key_encrypted"), provider["timeout_seconds"], provider["max_retries"],
            )
            connection.commit()


def load_settings(base_directory: Path) -> dict[str, Any]:
    default = json.loads((base_directory / "config.json").read_text(encoding="utf-8"))
    local_path = base_directory / "config.local.json"
    if local_path.exists():
        local = json.loads(local_path.read_text(encoding="utf-8"))
        for key, value in local.items():
            if isinstance(value, dict) and isinstance(default.get(key), dict):
                default[key].update(value)
            else:
                default[key] = value
    return default


def create_store(settings: dict[str, Any], base_directory: Path):
    storage = settings.get("storage") or {}
    if storage.get("mode") == "memory":
        return MemoryStore()
    if storage.get("mode") == "sqlserver":
        connection_string = str(storage.get("sql_server_connection_string") or "").strip()
        if not connection_string:
            raise RuntimeError("SQL Server mode requires storage.sql_server_connection_string in config.local.json.")
        return SqlServerStore(connection_string, base_directory / "schema.sql")
    raise RuntimeError("storage.mode must be either 'memory' or 'sqlserver'.")
