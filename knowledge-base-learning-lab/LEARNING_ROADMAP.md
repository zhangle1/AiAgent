# 知识库递进学习路线

## 目标

不是先造一个功能齐全的知识库，而是依次回答七个问题：

1. 一份材料进入系统后发生了什么？
2. 怎样保证解析结果可以复现和定位？
3. 为什么要切分，怎样判断切得好不好？
4. 检索为什么命中或漏掉某段内容？
5. 层级摘要怎样减少搜索范围和 token？
6. 知识、会话历史和长期记忆有什么不同？
7. 怎样把实验结论安全地迁移到 AiAgent？

路线按依赖关系排列，不按固定周数排列。每一阶段必须先完成“观察与验收”，再进入下一阶段。

## 总体模块

```text
资源接收模块
  → 解析产物模块
  → 上下文建模模块
  → 索引模块
  → 检索模块
  → 证据组装模块

会话观察模块
  → 候选记忆模块
  → 审核后的长期记忆
```

知识库事实不能因为出现在对话中就自动成为长期记忆，检索结果也不能脱离来源直接成为事实。

## Stage 0：看见完整链路

### 要理解的概念

- 资源、上下文节点、索引任务、召回结果、检索证据。
- 写入链路和查询链路是两条不同的链路。
- L0/L1/L2 是内容的加载深度，不是三份互不相关的数据。

### 亲手做

运行当前实验室，添加三份主题不同的短文。观察资源状态从 `queued` 到 `processing` 再到 `ready`，用相同关键词和不同问法检索。

阅读顺序：

1. 本项目 `backend/app.py` 的 `create_resource`。
2. `index_resource` 与 `build_nodes`。
3. `search`，手算一次分数。
4. OpenViking `openviking/server/routers/resources.py`。
5. OpenViking `openviking/service/resource_service.py`。
6. OpenViking `openviking/retrieve/hierarchical_retriever.py`。

### 你要能回答

- 为什么写入成功不等于资源已经可以检索？
- L0 和 L1 分别帮查询链路省掉了什么？
- 当前透明评分在哪些问法下会漏召回？

### 完成标志

你能画出“请求、任务、节点、结果”四类对象的流转图，并能指出每一步的输入、输出和失败状态。

### 对 AiAgent 的帮助

能读懂 `backed/Services/Knowledge/KnowledgeAppService.cs`、`KnowledgeTaskRunner.cs` 和 `KnowledgeRetrievalService.cs` 的职责，不再把上传、索引和检索当成一个动作。

## Stage 1：资源与持久化任务

### 要理解的概念

- 业务主数据与派生产物。
- 幂等键、内容哈希、任务状态机、重试和取消。
- 进程内异步任务与持久化队列的区别。

### 亲手做

- 用 SQLite 保存 `Resource`、`IndexJob`、`ContextNode`。
- 为资源正文计算 SHA-256；重复提交相同来源和哈希时不重复索引。
- 服务重启后，把遗留的 `processing` 任务标为可重试，而不是永远卡住。
- 给任务实现 `queued/running/succeeded/failed/cancelled` 状态机。

### 暂时不要做

不要接 Embedding、向量库、OCR，也不要支持十种文件格式。此阶段只研究生命周期和恢复。

### OpenViking 对照

- `openviking/service/task_tracker.py`
- `openviking/service/task_store.py`
- `openviking/storage/queuefs/`
- `openviking/service/resource_service.py`

### 完成标志

强制结束后端并重启，已有资源、任务和节点仍可解释；重复写入不会制造两套有效索引。

### 对 AiAgent 的帮助

为替换 `backed/Services/Knowledge/KnowledgeTaskRunner.cs` 中的进程内 `Task.Run` 提供验证过的状态机和恢复语义。

## Stage 2：标准化解析产物

### 要理解的概念

- 原始资源不等于可检索正文。
- Parser、OCR、规范化与来源定位。
- 稳定中间产物为什么比直接让索引器读原文件更重要。

### 亲手做

- 第一批只支持 Markdown、TXT、PDF。
- 每次解析输出 `document.md` 和 `manifest.json`。
- manifest 至少保存：资源哈希、解析器版本、页码或章节、字符区间、质量警告。
- PDF 文本覆盖率过低时只标记 `needs_ocr`，先不自动 OCR。
- 编写包含标题、列表、表格、空白页和中英文的固定样本。

### OpenViking 对照

- `openviking/parse/`
- `openviking/utils/resource_processor.py`
- `openviking/utils/media_processor.py`
- `openviking/utils/path_safety.py`

### 完成标志

任意内容块都能反查到原资源的页码或章节；相同资源和相同解析配置得到相同产物哈希。

### 对 AiAgent 的帮助

落实 `design/knowledge-rag-roadmap/knowledge-rag-roadmap.md` 中“RAG worker 只消费标准化中间产物”的目标。

## Stage 3：切分、Embedding 与索引版本

### 要理解的概念

- chunk size、overlap、标题感知切分和语义边界。
- dense embedding、相似度、维度和模型漂移。
- 索引版本必须绑定输入与配置快照。

### 亲手做

- 先实现两个切分 Adapter：固定长度、标题感知。
- 保存稳定 `chunk_id`、source locator 和 token 数。
- 接一个 Embedding Adapter；同时保留确定性的 fake Adapter 用于测试。
- 建立不可变 `IndexVersion`，记录资源哈希、parser、chunk 参数、模型、维度。
- 新版本成功后才切换 active version，失败不覆盖旧版本。

### OpenViking 对照

- `openviking/utils/embedding_input.py`
- `openviking/utils/embedding_utils.py`
- `openviking/storage/vectordb/`
- `openviking/service/reindex_executor.py`

### 完成标志

同一问题可以对比两种切分策略；模型或维度变化会创建新版本；旧版本仍可检索和回滚。

### 对 AiAgent 的帮助

补强 `AiKnowledgeIndexVersion` 的配置快照，并消除 `chunks.jsonl` 依赖文件名猜测文档归属的问题。

## Stage 4：先评测，再做混合检索

### 要理解的概念

- Recall@K、MRR、引用正确率、空结果率和 P95 延迟。
- 关键词召回与向量召回各自擅长什么。
- RRF、rerank、阈值、去重和上下文预算。

### 亲手做

- 建立至少 10 份脱敏文档、30 个问题及标准证据位置。
- 分别跑关键词、向量和混合检索，保存每次实验配置与指标。
- 用 RRF 合并候选，再增加可选 reranker。
- 结果必须包含资源、页码/章节、chunk、分数和命中路径。
- 证据不足时返回“证据不足”，不要让模型补写答案。

### OpenViking 对照

- `openviking/retrieve/hierarchical_retriever.py`
- `openviking/retrieve/retrieval_stats.py`
- `openviking/utils/search_filters.py`
- `benchmark/`

### 完成标志

能用数据解释某项修改提高或降低了什么，而不是凭一次对话感觉效果更好。

### 对 AiAgent 的帮助

可逐步替换 `backed/Services/Chat/Retrieval/KnowledgeRetrievalService.cs` 的逐库 Top-K 合并，并建立可回归的检索质量门槛。

## Stage 5：层级上下文与递归检索

### 要理解的概念

- 目录、文档、章节和内容块组成的上下文树。
- L0/L1/L2、搜索入口、分数传播、展开预算和收敛。
- “先定位区域，再读取内容”与全库 Top-K 的差别。

### 亲手做

- 从 manifest 构建目录/文档/章节树。
- 为目录和章节生成 L0/L1，保留生成模型、Prompt 与来源版本。
- 实现两种模式：quick 全局搜索；thinking 目录召回后递归展开。
- 记录每次查询访问过的节点、为何展开、为何停止。
- 用 Stage 4 的评测集比较层级检索与扁平检索。

### OpenViking 对照

- `README_CN.md` 的 Viking URI 和三层上下文说明。
- `openviking/core/namespace.py`
- `openviking/retrieve/hierarchical_retriever.py`
- `openviking/server/routers/search.py`

### 完成标志

层级检索在长文档或大型知识库上降低扫描和上下文量，同时不明显降低标准问题的 Recall@K。

### 对 AiAgent 的帮助

为项目知识、代码仓库 Markdown、知识库文档提供统一的“上下文节点”检索模型，但仍保留 AiAgent 的项目 ID 和权限体系。

## Stage 6：区分知识与记忆

### 要理解的概念

- 会话原文、记忆观察、候选记忆和长期记忆。
- 用户全局、项目个人、项目共享三种作用域。
- Prompt injection、敏感信息、证据与人工审核。

### 亲手做

- 将一次会话记录为 Observation，但不直接参与长期检索。
- 离线生成候选，保留来源会话与证据。
- 只有人工 approve 的候选才能变为 active memory。
- 长期记忆使用相同检索基础设施，但使用独立作用域、生命周期和排序策略。
- 在回答中展示本轮用了哪些知识证据、哪些记忆。

### OpenViking 与 AiAgent 对照

- OpenViking：`openviking/session/memory/`、`openviking/session/session.py`。
- AiAgent：`backed/Services/Memory/MemoryService.cs`、`MemoryCandidateService.cs`、`MemoryCandidateHostedService.cs`。

### 完成标志

你能证明未审核候选不会进入 Prompt；删除项目访问权后，相关项目记忆与知识均不可召回。

### 对 AiAgent 的帮助

AiAgent 已有候选审核闭环，应保留它；只把检索、证据追踪和质量统计能力共享给知识与记忆模块。

## Stage 7：迁移到 AiAgent

### 迁移顺序

1. 先迁移稳定概念和数据契约，不迁移 UI。
2. 接入标准化解析产物与 manifest。
3. 将索引任务迁移为持久化后台队列。
4. 扩展索引版本配置快照。
5. 在旧检索接口后接入新的混合检索 Adapter，灰度对比。
6. 指标稳定后增加 L0/L1 与递归检索。
7. 最后统一知识和记忆的观测界面，不合并权限与生命周期。

### 建议在 AiAgent 保持的模块接口

```text
IngestionModule.submit(resource, scope) -> job_id
IngestionModule.status(job_id) -> job

RetrievalModule.search(query, scope, budget) -> evidence[]

MemoryModule.observe(turn, scope) -> observation_id
MemoryModule.review(candidate, decision) -> memory
```

接口需要明确权限、错误、幂等和性能语义；Parser、Embedding、Vector Store、Reranker 是内部 seam 上的 Adapter，不应泄漏到 Controller 或聊天调用方。

### 灰度要求

- 同一查询同时记录旧检索和新检索结果，但只把旧结果提供给用户。
- 离线比较 Recall@K、引用正确率、延迟和成本。
- 新实现达到门槛后再按用户或项目灰度。
- 保留旧索引读取能力和一键回退，不原地覆盖激活版本。

## 你现在先做的三件事

### 1. 完成 Stage 0，不加新技术

亲自启动实验室，准备三份短文和五个问题，记录每次检索为什么命中或漏掉。重点是理解对象和状态流转。

### 2. 建立第一份评测记录

在 `experiments/0001-baseline/` 保存测试材料说明、问题、预期证据和实际结果。即使当前算法很简单，也从第一天形成“修改前有基线”的习惯。

### 3. 再实现 SQLite 持久化

只完成 Resource、IndexJob、ContextNode 三张表以及重启恢复。不要同时加入文件上传、Embedding 或向量数据库。

完成这三件事后，再进入 Stage 2；否则后面的向量检索只会把不可观察的复杂度提前引入。
