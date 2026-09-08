# Knowledge Base Learning Lab

这是一个递进式知识库学习实验室，不以“尽快做完产品”为目标。每个阶段只引入一组概念，通过亲手实现、观察中间产物和完成验收问题，理解知识库从资源进入到检索证据返回的完整链路；成熟结论再逐步迁移到 AiAgent。

完整路线见 [LEARNING_ROADMAP.md](./LEARNING_ROADMAP.md)，统一术语见 [CONTEXT.md](./CONTEXT.md)。

## 当前阶段

```text
backend/   Stage 0 FastAPI 沙盒：资源、任务、L0/L1/L2 节点、透明评分
frontend/  Stage 0 React 控制台：观察导入、任务状态、分层节点与证据
```

数据仅在内存中，重启会清空。这是当前阶段刻意保留的限制，用来先看清主链路。

## 启动 Stage 0

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

打开 `http://localhost:5173`。Vite 会把 `/api` 代理到 `http://localhost:8010`。完成路线中 Stage 0 的观察题后，再开始增加数据库或向量模型。
