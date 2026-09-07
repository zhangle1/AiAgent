# Knowledge Lab MVP

一个用于研究知识库软件核心逻辑的最小可运行项目。它借鉴 OpenViking 的分层上下文思想，但不依赖或复制 OpenViking 源码：资源写入后生成 L0 摘要、L1 概览和 L2 内容块；检索先匹配资源概览，再返回相关内容块与来源证据。

## 结构

```text
backend/   FastAPI：资源、异步索引任务、分层节点、检索 API
frontend/  React + Vite：资源导入、任务状态、检索控制台
```

## 启动

后端（Python 3.10+）：

```powershell
cd backend
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
uvicorn app:app --reload --port 8010
```

前端（Node 18+）：

```powershell
cd frontend
npm install
npm run dev
```

打开 `http://localhost:5173`。Vite 会把 `/api` 代理到 `http://localhost:8010`。

## 这是 MVP，不是生产实现

- 数据只存于进程内存，重启会清空。
- 检索使用可解释的关键词评分，而非真实 Embedding/向量数据库。
- 异步任务使用 `asyncio.create_task`，生产环境应迁移到持久化队列。
- 当前不包含认证、多租户、文件上传、OCR、权限过滤和索引版本回滚。

下一阶段可依次替换：持久化资源/任务 → 受控文件上传与解析 Markdown → Embedding/向量库 → 混合检索与 rerank → 用户/项目 ACL → 版本化索引与评测集。
