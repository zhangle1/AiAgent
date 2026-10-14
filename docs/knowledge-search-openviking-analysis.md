# AiAgent 知识检索与 OpenViking 机制对比

> 审阅稿｜2026-10-14
> 本文用于讨论现状与方案，不代表已经批准或实施改造。

## 结论摘要

目前 AiAgent 的知识能力不是单一的“语义搜索”：新目录式知识空间主要是关键词候选筛选后交给 LLM 阅读回答；旧知识库则同时保留 Wiki 工具式检索、源码文本搜索和可选的向量 RAG。语义摘要用于目录浏览和上下文组织，但它本身不等于向量语义检索。

摘要确实会消耗入库阶段的 LLM token，尤其是批量导入时重复刷新目录摘要可能放大成本；但缓存有效时并非每次查询都重新摘要。查询阶段另有成本：新目录式问答可能把较长的候选内容交给模型，旧 Wiki 检索也可能连续调用模型多轮。

OpenViking 同样会为变化的内容生成摘要并做向量化，并非“零摘要、零 token”。它更值得借鉴的是处理流水线与刷新策略：异步队列、变更驱动、目录层级摘要、渐进式读取、向量召回，以及对大目录摘要输入的采样/刷新控制。

## 当前 AiAgent 的检索路径

### 1. 新的目录式知识空间

主要逻辑位于：

- `backed/Services/Knowledge/KnowledgeResourceService.cs`
- `backed/Services/Knowledge/KnowledgeResourceSemanticService.cs`
- `backed/Services/Knowledge/KnowledgeAgentTools.cs`

问答路径 `AskAsync` 会在选定 URI 范围内收集文件，按文件名与问题中的简单词项匹配排序，扫描有限数量的候选，再选取若干文档片段交给 LLM 回答。当前实现更接近“关键词召回 + LLM 阅读”，不是 embedding 向量检索。上下文也有字符数上限，但字符上限不等于 token 精确上限。

知识 Agent 的 `search` 工具主要做标题/正文子串匹配；它适合找名称、术语或明确文本，不是语义向量搜索。

`KnowledgeResourceSemanticService` 会生成文件语义内容及目录层级摘要，帮助浏览目录、理解资源概貌；这些摘要目前不自动构成向量索引。

### 2. 传统知识库

主要逻辑位于：

- `backed/Dtos/Knowledge/KnowledgeWorkspaceDtos.cs`
- `backed/Services/Knowledge/KnowledgeAppService.cs`
- `backed/Services/Knowledge/KnowledgeWikiRetrievalService.cs`
- `backed/Services/Knowledge/KnowledgeSourceSearchService.cs`

传统知识库提供不同检索路径：

| 路径 | 机制 | 是否调用 LLM / 向量模型 |
| --- | --- | --- |
| Wiki 检索 | 模型查看目录，使用 list/search/read/cite 等工具逐步定位原文 | 多轮过程中会调用 LLM；不是向量召回 |
| 源码搜索 | 对标题和文本做精确/子串匹配 | 不需要 LLM 或 embedding |
| RAG 模式 | 对已建立的索引执行向量检索 | 使用向量索引；可再由模型组织回答 |

检索模式默认值为 `wiki`；只有选择 RAG 且存在有效索引时才走向量检索。因此界面如果笼统称为“语义搜索”，可能让人误以为所有路径都在做向量语义召回。

## 摘要和 token 成本：当前判断

### 入库/更新阶段

语义生成服务会为文件生成摘要，并向上刷新祖先目录的概览；L0 简要摘要从 L1 内容中提取，不需要再单独发起一次模型生成。较长文件会按输入预算切分并通过摘要/归并处理；目录概览也可能需要分批和归并。模型输出有长度限制，但实际 token 费用仍取决于文本长度、模型 tokenizer、提示词和供应商计费方式。

缓存以资源内容及其版本指纹为基础。内容没有变化且快照仍有效时可以复用结果；文件变化会导致相应文件和受影响目录重新计算。因此不能简单理解为“每次问答都重新摘要”。

当前后台编译队列按文档逐项处理。由此可以合理推测：一次导入同一目录的许多文件时，多个任务可能分别触发目录刷新，造成重复工作；这是从任务粒度和刷新链路推导出的风险，不是已经测得的 token 账单。实际倍率需要加调用数、输入/输出 token 和缓存命中统计后才能确认。

### 查询阶段

- 新目录式问答会把候选文档内容拼入问题上下文，存在上下文偏大的可能；其字符上限不能直接换算成固定 token 费用。
- Wiki 检索可能多轮调用模型，检索步骤越多，提示词重复发送的成本越高。
- 精确源码搜索无需模型调用；RAG 的 embedding 与回答模型成本应分别统计。

所以更准确的结论是：**摘要成本主要发生在新增/变更入库；问答成本来自召回上下文及检索轮数。两者应分别度量和优化。**

## OpenViking 如何处理

参考本地代码库 `E:\项目\know-why\OpenViking` 中的知识文档及上游文档：

- [内容提取与入库](https://github.com/volcengine/OpenViking/blob/main/docs/zh/concepts/06-extraction.md)：解析器产出内容后进入异步语义处理队列，再生成摘要并向量化。
- [上下文分层](https://github.com/volcengine/OpenViking/blob/main/docs/zh/concepts/03-context-layers.md)：以目录 L0/L1 摘要作为轻量导航，完整内容作为 L2 按需读取；目录概览由文件摘要和子目录信息组织。
- [检索流程](https://github.com/volcengine/OpenViking/blob/main/docs/zh/concepts/07-retrieval.md)：先做向量召回，再按需要使用查询规划、重排和渐进式上下文装配；不同检索入口可以选择不同精度/成本。

从机制上看，OpenViking 也会摘要、也会调用 embedding；其成本控制重点是只处理变化内容、把处理放入队列、以目录层级信息辅助导航，并避免把所有原文一次性塞入每次查询。大目录还可以限制概览输入样本，减少全量重算。

## 建议的 AiAgent 处理方向

以下是供选择的方向，不是本次已实施内容。

1. **把检索与摘要分开命名和设计。** “目录摘要/概览”用于导航与上下文压缩；“语义检索”应明确表示 embedding 向量召回。关键词搜索保留为精确匹配能力。
2. **以原文为事实来源，摘要只做导航。** 保留解析后的原文和来源定位；摘要不替代证据。回答应优先带回文件 URI、段落/页码等引用位置。
3. **建立真正的向量/混合检索路径。** 入库时对解析文本分块并计算 embedding，保存块与来源定位；查询时向量 top-K 召回，必要时融合关键词结果或重排，再把少量相关证据交给回答模型。
4. **将摘要刷新改成变更驱动并支持批处理。** 一批文件解析完成后合并刷新受影响目录；只有子项摘要实质变化时才继续刷新父目录。对超大目录限制样本或分层汇总。
5. **提供成本/质量档位。** 例如：仅解析（不生成概览）、默认目录概览、按需深度分析；图片/扫描件单独选择 VLM。让用户知道哪些操作会产生 LLM、VLM、embedding 调用。
6. **增加可观测性。** 按任务记录模型/供应商、模型版本、输入与输出 token、embedding 数量、缓存命中、重试次数、分块数和耗时；先用这些数据验证成本热点，再决定默认策略。

## 建议的讨论顺序

1. 是否把新目录空间的默认问答切换为“向量 + 关键词混合召回”，还是先保留当前关键词召回并仅增加明确标注？
2. 目录摘要是否默认生成，还是改为轻量 L0 默认、L1 按需/变更后生成？
3. 批量上传是否先做队列合并与目录刷新去重，再接入 embedding？
4. 是否需要为每次任务展示 token/模型成本，还是先在后台日志中观测？

## 代码定位

- `backed/Services/Knowledge/KnowledgeResourceService.cs`：目录式问答及候选内容组织。
- `backed/Services/Knowledge/KnowledgeAgentTools.cs`：知识 Agent 工具搜索。
- `backed/Services/Knowledge/KnowledgeResourceSemanticService.cs`：文件/目录语义内容生成与缓存刷新。
- `backed/Services/Knowledge/KnowledgeCompilationWorker.cs`：知识处理后台队列。
- `backed/Services/Knowledge/KnowledgeAppService.cs`：传统知识库检索路径分发。
- `backed/Services/Knowledge/KnowledgeWikiRetrievalService.cs`：Wiki 工具式检索。
- `backed/Services/Knowledge/KnowledgeSourceSearchService.cs`：源码文本搜索。
- `backed/Dtos/Knowledge/KnowledgeWorkspaceDtos.cs`：检索模式及摘要生成配置默认值。
