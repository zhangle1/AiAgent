# 知识库工作区与独立提炼循环

日期：2026-09-17。实现范围：在现有知识库上增加独立提炼模块和工作区视图，保留原有上传、索引、检索和同步提炼接口。

后续调整：上传仅保存 raw，默认使用模型检索知识表示层；索引改为可选、手动创建。接口路由保留，新的默认行为与验收以 [知识表示层与检索](knowledge-wiki-retrieval.md) 为准。

## 使用路径

1. 打开 `/settings/knowledge`，选择 Codex CLI 或 LLM API，配置可选模型 ID、步骤预算和超时。CLI 沿用后端机器上的现有 Codex 登录、模型目录与 profile；并不自动连接浏览器用户电脑。
2. `/knowledge` 新建或打开知识库；在「管理」保存公司／项目。公司下项目留空表示公共知识，历史库归入未归属公司。知识库名称、现有文件路径和索引不移动。
3. 在「原始文件」上传和查看原文，点击「提炼知识」。按钮使用保存的提炼配置，任务在后台排队，页面轮询状态；切换页面不取消任务，再次进入可恢复状态显示。
4. 「知识」展示最近一次成功提炼的独立主题、草稿标记、生成方式和原文摘录，可跳回原始文件。旧的整篇 Markdown 派生物仍可查阅。原始文件继续分别展示原文件、解析正文和 AI 提炼。

## 模块与数据边界

```text
KnowledgeAppService（HTTP）
  ├─ KnowledgeWorkspaceService（目录归属、主题读取）
  ├─ KnowledgeCompilerSettings（配置快照）
  └─ KnowledgeCompilationWorker（有界串行队列、任务状态）
       └─ KnowledgeIngestionService（原文解析、数据库与文件保存）
            ├─ backed/Services/Knowledge/Core/KnowledgeCompiler（项目内有界 Agent loop）
            └─ KnowledgeModelAdapter
                 ├─ ICodexChatService（已有 app-server / exec profile）
                 └─ ILlmChatClient（已有 API 模型目录）

原有 KnowledgeTaskRunner → RAG pipeline → 活动索引版本保持独立
```

模型可提出 `read_source`、`propose_page`、`finish` 三种操作。宿主验证段号、预算、页面大小、标题重复和引用原文是否精确匹配；每段必须读过且至少有一条来源证据才能完成。无效操作反馈给模型在预算内修正；超时、取消或用尽预算均不提交部分草稿。精确引用校验不能证明每句生成内容都正确，结果仍为待核对草稿。

原文不覆盖。解析正文以临时文件和原子移动写到 `parsed/<documentId>/`。派生结果追加到现有 `AiKnowledgeArtifact`，其中 `EvidenceJson` 保存文档／解析版本／来源哈希、主题和不含原文的操作步骤；知识页视图再从对应主题读取证据。成功重提炼追加新版本，失败保留旧版。旧数据没有主题数组时回退展示原 Markdown。

`MetadataJson.organization` 保存公司／项目，保留同一对象内既有元数据；没有新增数据库实体字段或迁移文件。公司／项目在此阶段是目录分类，并未新增公司表、项目授权关系或租户隔离。

## 接口

所有接口位于 `/api/v1/knowledge` 并沿用现有登录边界。

| 方法与路径 | 契约 |
| --- | --- |
| GET / PUT `compiler-settings` | 提炼配置快照，默认 Codex、48 步、20 分钟 |
| PUT `{kb}/organization` | 保存公司／项目；项目必须有公司 |
| GET `{kb}/pages` | 最新成功主题，含来源文档 ID、产物 ID 和主题序号 |
| POST `{kb}/documents/{id}/compile` | 返回排队任务；同文档已有活动任务时返回原任务 |
| GET `{kb}/documents/{id}/compilation` | 最近提炼任务或 null，状态 queued / processing / success / error |
| POST `{kb}/documents/{id}/process` | 保留旧同步契约和显式 generator/model 参数，执行新提炼核心 |
| GET `{kb}/documents/{id}/content` | 保留原文解析与最近成功产物读取契约 |

原有 provider 配置、上传、reindex、检索与索引版本接口继续保留。索引进度明确过滤 `wiki_compile`，防止提炼任务替代索引状态。

## 当前限制

- 队列面向单后端实例，容量 32，串行执行；状态落库，执行队列在内存。服务重启时将中断任务标记失败，用户可重新提炼。未实现分布式抢占、自动恢复、批量提炼或取消按钮。
- 文本最多 256,000 字符，每段 16,000 字符；每页最多 12,000 字符，最多 32 页。超限显式失败，不能静默截断原文。步数范围 8–96，超时范围 1–60 分钟。
- 新主题目前按来源文档提炼；跨文档融合、冲突审阅、WikiLink 图谱、知识页检索融合和人工发布工作流未纳入本次实现。旧 RAG 仍查询原始资料的索引，派生草稿不会自动替代事实源。
- CLI 使用独立会话与只读运行模式，目录由后端指定。只读模式禁止写入，但并不是操作系统级的来源目录读取隔离。
- 原参考图片和补充文本未随当前上下文传入，本次按明确描述实现标签与目录组织，未做截图逐像素复刻。

## 参考与取舍

核对本地 `llm_wiki/src/components/layout/knowledge-tree.tsx` 及 [LLM Wiki](https://github.com/nashsu/llm_wiki) 的原始来源／派生 Wiki／配置三层结构与来源追溯设计；保留 AiAgent 的服务端存储和既有 RAG。参考 [OpenViking](https://github.com/volcengine/OpenViking) 的上下文目录组织方向，采用显式公司／项目归属。没有复制这两个项目的实现或引入第二套向量数据库。

## 验证

- `dotnet test backed.tests/AiAgent.Backend.Tests.csproj --filter FullyQualifiedName~Knowledge`：16 项通过；使用内存 SQLite、临时源文件和模拟模型，覆盖证据校验、预算、取消、分段覆盖、CLI/API 选择、元数据保留、来源删除过滤、队列去重、重启标记，以及失败保留原文／旧产物／RAG 版本。
- 前端 TypeScript 检查、Next.js 生产构建通过；包含 `/knowledge` 与 `/settings/knowledge`。
- 全量后端测试：55 项通过、2 项失败。两个 `UsageStatisticsServiceTests` 在原有 SQLite 建表阶段失败：`AUTOINCREMENT is only allowed on an INTEGER PRIMARY KEY`。与本次知识库无关的夹具和生产统计代码未修改。
- 未使用真实模型账号或生产数据库做端到端调用，也未验证浏览器视觉与原参考截图的一致性。
