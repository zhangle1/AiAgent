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

## SQL Server 与 DeepSeek 配置

默认 `backend/config.json` 使用内存模式，便于学习和演示。要启用 SQL Server，请复制 `backend/config.example.json` 为 `backend/config.local.json`，填写连接字符串，并将 `storage.mode` 保持为 `sqlserver`。`config.local.json` 已忽略，不应提交。

首次以 SQL Server 模式启动时，后端会执行 `backend/schema.sql`，建立以下持久化表：

- `KnowledgeLabResource`：资源元数据和原始正文。
- `KnowledgeLabIndexJob`：索引任务、阶段、进度和错误信息。
- `KnowledgeLabContextNode`：L0、L1、L2 节点及其生成方式。
- `KnowledgeLabLlmProvider`：DeepSeek 模型、地址、超时、重试次数与加密后的 API Key。

API Key 不写入配置文件。先生成并设置一个 Fernet 加密键，再重启后端：

```powershell
py -c "from cryptography.fernet import Fernet; print(Fernet.generate_key().decode())"
setx KNOWLEDGE_LAB_CONFIG_KEY "上一步生成的值"
```

重新打开 PowerShell 并启动后端。随后在页面的“摘要生成”中选择 `DeepSeek API`，输入模型和 API Key，点击“写入并建立分层节点”。浏览器只把 Key 发送给本机后端一次；后端加密后写入 `KnowledgeLabLlmProvider.ApiKeyEncrypted`，接口和页面都不会返回它。

DeepSeek 走 OpenAI 兼容的 `https://api.deepseek.com/chat/completions`。每次资源导入会串行生成 L0 和 L1；任务轮询会展示 `llm_l0`、`llm_l1` 和重试等待。网络超时、429 和 5xx 最多重试两次，并采用指数退避；401、403、400 和空响应会直接将任务标记为失败。
