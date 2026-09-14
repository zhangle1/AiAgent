# LLM Wiki 架构研究与 AiAgent 落地建议

> 调研日期：2026-09-14
> 上游仓库：[`nashsu/llm_wiki`](https://github.com/nashsu/llm_wiki)
> 固定源码版本：[`e8082119649e6a8e1cf85eaf289adcabfdf39d4e`](https://github.com/nashsu/llm_wiki/tree/e8082119649e6a8e1cf85eaf289adcabfdf39d4e)（应用版本 `0.6.11`）

## 结论

LLM Wiki 最值得 AiAgent 借鉴的不是桌面技术栈，而是“原文层 → 持久 Wiki 层 → 检索/Agent 层”的知识编译模式：导入时先把知识整理成带来源、类型、双向链接和目录的 Markdown，再把关键词、向量和图关系组合用于查询。它与只在问答时检索原始 chunk 的 RAG 可以并存，而不是二选一。[上游模式说明](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/llm-wiki.md) [应用 README](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/README.md#what-is-this)

对当前 .NET 9 + Next.js AiAgent，建议保留现有 SqlSugar 元数据、版本化 chunk、LlamaIndex Python Worker 和服务端多用户边界，只吸收四项能力：可追溯的 Wiki 派生物、两阶段异步编译、关键词/向量/图的融合检索、确定性写入与质量门禁。不要直接移植 Tauri、本地文件即数据库、前端持有模型密钥或 LanceDB Rust 实现。

## 最小必需配置与可选服务

| 能力 | 是否必需 | 最小配置 | 说明 |
| --- | --- | --- | --- |
| 文档导入与本地解析 | 必需 | 项目目录、支持的文件类型 | PDF/Office/电子书由 Rust 解析器转成文本；普通 UTF-8 文本直接读取。[读取分派源码](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/fs.rs#L12-L123) |
| Wiki 编译 LLM | 必需 | provider、model；远程模型通常还需 API key，Ollama/本地 CLI 可不需远程 key | 导入至少包含分析和生成两次模型调用，较复杂输入还可能触发 Review 或截断修复调用。[导入主流程](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest.ts#L1019-L1439) |
| Embedding API | 可选，默认可关闭 | endpoint、model；按供应商配置 key，另可配维度、chunk、重叠、并发和 batch size | 关闭后仍有关键词和图检索；开启时通常调用 OpenAI-compatible embeddings，也支持 Google 原生和特定火山引擎形态。[Embedding 配置字段](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/components/settings/settings-types.ts#L20-L39) [Embedding 实现](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/search.rs#L1066-L1368) |
| 视觉模型 | 可选 | 可复用主 LLM，或独立 provider/model/key | 只用于导入图片说明；并非文本 Wiki 的硬依赖。[多模态配置](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/components/settings/settings-types.ts#L41-L54) |
| MinerU | 可选 | Cloud token，或 Local API endpoint/token/backend | 面向复杂 PDF；失败会回退内置解析器。[MinerU 分支与回退](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest.ts#L687-L736) |
| Web Search | 可选 | Tavily 或 SerpApi key，或 SearXNG URL | 只用于 Deep Research，不影响本地资料导入和问答。[功能说明](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/README.md#13-deep-research) |
| 本地 HTTP API / MCP | 可选 | 桌面应用运行、开启 API 与 MCP、建议 token | MCP 不重复实现检索，而是代理到 `127.0.0.1:19828/api/v1`。[MCP README](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/mcp-server/README.md) |

上游支持 OpenAI、Anthropic Messages、Gemini `generateContent/streamGenerateContent`、Azure OpenAI、Ollama OpenAI-compatible、Custom OpenAI/Anthropic wire，以及 Claude Code/Codex CLI 子进程；各分支的 URL、鉴权头和流解析不是同一协议的简单换名。[provider 分派源码](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/llm-providers.ts#L913-L1128) 官方接口也印证了这些差异：[OpenAI Embeddings](https://developers.openai.com/api/reference/resources/embeddings/methods/create)、[Claude Streaming Messages](https://platform.claude.com/docs/en/build-with-claude/streaming)、[Gemini 内容生成](https://ai.google.dev/gemini-api/docs/generate-content/text-generation)、[Ollama OpenAI compatibility](https://docs.ollama.com/api/openai-compatibility)。

## 架构与处理流

```text
React 19 / Vite UI
  ├─ Zustand：项目、设置、队列、会话、Review 状态
  ├─ LLM 编排：分析、Wiki 生成、修复、Lint、Deep Research
  └─ Tauri invoke / local HTTP API
       ↓
Tauri 2 / Rust
  ├─ 文件、PDF/Office/电子书解析与图片提取
  ├─ Rust Chat Agent、项目锁、Source Watch
  ├─ 关键词 + 向量 + WikiLink 图融合检索
  └─ 项目内 LanceDB
       ↓
项目目录
  ├─ raw/sources/          不修改的原始来源
  ├─ purpose.md/schema.md  目标与编译规则
  ├─ wiki/                 LLM 维护的 Markdown 知识层
  └─ .llm-wiki/            队列、缓存、会话、Review、向量库等运行状态
```

技术依赖由 `package.json`、`Cargo.toml` 和 Tauri 配置共同定义：React/Vite/Milkdown/Graphology/Sigma 位于前端；文件解析、HTTP、监视与 LanceDB 位于 Rust；构建产物由 Tauri 打包。[前端依赖](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/package.json) [Rust 依赖](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/Cargo.toml) [Tauri 配置](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/tauri.conf.json)

单个来源的核心处理顺序如下：

1. 根据扩展名选择内置解析器；PDF 可先走 MinerU，失败回退内置 PDFium；解析结果写邻接 `.cache`。
2. 并行读取来源、`schema.md`、`purpose.md`、`wiki/index.md`、`wiki/overview.md`，再用 SHA-256 ingest cache 判断是否跳过不变来源。[读取与缓存判断](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest.ts#L663-L773) [缓存实现](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest-cache.ts)
3. 第一次 LLM 调用只输出实体、概念、论点、关联和矛盾分析；第二次根据分析输出严格的 `---FILE:` / `---REVIEW:` 块。
4. 在项目锁和 commit 边界内解析、校验并写文件；截断块可单独修复，`index.md` 与 `log.md` 有确定性补写，缺失来源摘要也会补建，只有完整结果才进入 cache。[FILE 块解析](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest.ts#L394-L559) [写入与门禁](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest.ts#L1165-L1439)
5. Embedding 开启时，把标题、标题路径和 Markdown chunk 合并后向量化，再写入 LanceDB；关键词结果与向量结果用 RRF 融合，最后预留一部分窗口做一跳 WikiLink 图扩展。[chunk embedding](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/embedding.ts#L1-L24) [融合检索](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/search.rs#L327-L658)

## 格式解析边界

| 格式 | 上游实际路径 | 关键边界 |
| --- | --- | --- |
| PDF | PDFium 内置文本/图片提取；可选 MinerU Cloud/Local | 复杂布局与 OCR 更依赖 MinerU；MinerU 失败回退。 |
| DOC/DOCX/DOCM | AnyDoc 优先；`office_oxide` 兼容旧 DOC；`docx-rs`/ZIP XML 保留标题、列表、表格和粗斜体 | 解析结果是 Markdown 近似，不是原版式重建。 |
| PPT/PPTX、ODS/ODT/ODP、XLS/XLSX/XLSM/XLSB、RTF | AnyDoc，部分格式再由 ZIP/XML 或 calamine 兼容解析 | 任意二进制不会因宽松探测而直接送入解析器。[Office 白名单与分派](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/fs.rs#L12-L123) |
| EPUB/MOBI | Rust `epub` / `mobi` + `html2text` | 仅支持可解析的无 DRM 内容；代码显式限制文件、展开体积、章节数和输出文本大小。[电子书实现](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/ebook.rs#L1-L28) |
| Org | 内置 Org → Markdown 转换 | 源代码块只作为文本保留，不执行。[Org 测试契约](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/fs.rs#L2239-L2259) |
| 图片、音视频 | 默认先生成文件占位描述；图片可另走视觉 caption | 音视频没有转写管线，不能把“支持导入”误解为理解其内容。[读取分派](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/fs.rs#L92-L122) |
| Markdown、代码、JSON/CSV/YAML 等 | UTF-8 直接读取 | 非 UTF-8、锁定文件或未知二进制会失败；前端文件分类不等于后端有专用语义解析器。[文件分类](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/file-types.ts) |

MinerU 官方当前支持 CLI/API/WebUI，输入覆盖 PDF、图片、DOCX、PPTX、XLSX；本地 API 暴露异步任务接口。它适合作为可选重型解析后端，不应成为基础链路的单点依赖。[MinerU Quick Usage](https://github.com/opendatalab/MinerU/blob/master/docs/en/usage/quick_usage.md) [MinerU Quick Start](https://github.com/opendatalab/MinerU/blob/master/docs/en/quick_start/index.md)

## 存储、索引与部署

Wiki 的稳定交换格式是普通 Markdown：每页 YAML frontmatter 保存 `type/title/sources`，正文用 `[[wikilink]]`，`index.md` 是内容目录，`log.md` 是时间线；原始来源与派生 Wiki 分离，天然兼容 Obsidian 和 Git。[三层与文件约定](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/README.md#what-we-kept-from-the-original) [项目目录](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/README.md#project-structure)

运行状态位于 `.llm-wiki/`，包括 ingest cache、聊天、Review、Lint、警告日志和 `.llm-wiki/lancedb`。当前向量表 `wiki_chunks_v2` 每个 chunk 一行，保存 `page_id/chunk_index/chunk_text/heading_path/vector`；向量维度固定在 Arrow schema 中，因此切换不同维度的模型需要重建，源码也拒绝维度不一致的批次。[向量库存储](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/vectorstore.rs#L49-L68) [chunk schema 与校验](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/vectorstore.rs#L350-L532) LanceDB 官方将 OSS 版定位为进程内嵌数据库，并要求查询向量与存量向量使用相同模型和维度；这与上游实现相符。[LanceDB Quickstart](https://docs.lancedb.com/quickstart) [Vector Search](https://docs.lancedb.com/search/vector-search)

上游是跨平台桌面部署，而不是 Web 服务部署。源码构建需要 Node.js 20+、Rust 1.88+、`protoc`，再编译 MCP 包、Vite 前端和 Tauri 安装包；发布格式为 macOS DMG、Windows MSI、Linux DEB/AppImage。[构建说明](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/README.md#build-from-source) Tauri 在 Windows 开发还要求 Microsoft C++ Build Tools 与 WebView2。[Tauri prerequisites](https://v2.tauri.app/start/prerequisites/)

## 局限与风险

- **模型成本不是一次调用。** 两阶段 ingest 是最低成本，Review、长文分块汇总、截断修复、图片 caption 都可能增加调用量；必须按来源记录 token、重试和最终产物版本。
- **LLM 输出仍需当作不可信结构化输入。** 上游专门实现路径白名单、FILE 块闭合检测、截断修复、项目锁和确定性 index/log，说明仅靠 prompt 不能保证可写性。[路径与 FILE 解析](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src/lib/ingest.ts#L394-L559)
- **派生 Wiki 不等于事实源。** 它是可丢弃、可重建、可能幻觉或过时的视图；回答必须保留到原始来源/页码/chunk 的可点击链路，不能只引用二次摘要。
- **关键词检索较朴素。** 实现主要是包含匹配、停用词与 CJK bigram/单字，不等于 BM25；适合与现有 LlamaIndex 语义检索互补，不宜替换它。[关键词评分](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/src-tauri/src/commands/search.rs#L818-L999)
- **本地单用户假设明显。** 项目状态和密钥由桌面 Store/项目目录保存，本地 API 默认回环地址；直接搬到多租户服务器会缺少租户隔离、配额、审计和集中密钥管理。[API/MCP 安全模型](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/mcp-server/README.md#security-model)
- **README 的召回率提升属于项目自述。** 仓库 README 声称向量搜索使总体 recall 从 58.2% 提升到 71.4%，但本次未发现可复现实验配置与完整数据集，因此不能作为 AiAgent 选型基线。[原始声明](https://github.com/nashsu/llm_wiki/blob/e8082119649e6a8e1cf85eaf289adcabfdf39d4e/README.md#7-optimized-query-retrieval-pipeline)
- **上游变化快。** 本文固定到具体 commit；实施前应重新核对 provider wire、MinerU 接口和依赖版本。

## 与当前 AiAgent 的差异

| 维度 | 当前 AiAgent | LLM Wiki | 处理建议 |
| --- | --- | --- | --- |
| 产品形态 | .NET 9 API + Next.js 16，多用户服务端 | Tauri 本地桌面、项目目录即工作区 | 保持现有前后端边界，不迁移 Tauri。 |
| 原始索引 | LlamaIndex Python Worker；版本目录产出 `chunks.jsonl`，再物化到 `ai_knowledge_chunk` | Rust 本地解析 + LanceDB chunk 表 | 复用现有 Worker 和版本表，不引入第二套向量真源。[现有 RAG 门面](../backed/Services/Rag/RagService.cs) [现有物化器](../backed/Services/Knowledge/KnowledgeIndexMaterializer.cs) |
| 结构化检索 | 已有页码范围读取、语义检索与 citation context | Wiki 页面、关键词、向量、WikiLink 图融合 | 在 `KnowledgeRetrievalService` 增加 Wiki page/graph retriever，再统一融合证据。[现有检索服务](../backed/Services/Chat/Retrieval/KnowledgeRetrievalService.cs) |
| 知识派生物 | 以文档、chunk、索引版本为主 | LLM 持续维护 Markdown Wiki | 新增“可重建派生层”，不要覆盖原文或 chunk。 |
| 状态与权限 | SqlSugar 实体、服务端目录校验、用户边界 | 本地 JSON/Markdown/文件锁 | Wiki 记录必须带 `KnowledgeBaseId/UserId/IndexVersionId`，写入仍走 Service 与原子替换。 |
| 前端 | Next.js App Router | React/Vite/Zustand 三栏桌面 | 只复用交互概念：任务进度、Review、来源预览、图谱；API 仍放 `front/lib/*-api.ts`。 |

## 分阶段落地

### P0：先建立可追溯 Wiki 派生层

1. 为知识库增加 `wiki_compile` 异步 Job，输入固定为激活的 `AiKnowledgeIndexVersion`，禁止直接修改原始上传文件。
2. 定义服务端拥有的输出契约，例如 `WikiPageDraft { type, title, slug, sourceChunkIds, body, links }`；让 LLM 输出 JSON，而不是自由文件路径。由 .NET 校验后生成 Markdown、frontmatter、`index.md` 和 append-only `log.md`。
3. 页面正文每个关键段落保留 `documentId/pageNo/chunkId`，使 Wiki 摘要可回溯到现有 citation，而不是形成“引用摘要自身”的闭环。
4. 以 `contentHash + compilerVersion + promptVersion + modelSnapshot + indexVersionId` 为缓存键；写入使用临时文件 + 原子替换。

验收：同一索引版本重复编译不产生无意义变更；删掉派生目录可完整重建；任一页面可跳回原始证据。

### P1：两阶段编译与人工 Review

1. 阶段 A 产出结构化分析，阶段 B 产出页面草稿；两者分别落审计记录，重试不重复提交。
2. 先只允许 `source/concept/entity/synthesis` 四类页面；目录和链接由后端确定性生成。
3. 把矛盾、低置信度、缺引用和新主题放入 Review，不让 LLM 自动覆盖已发布的人工作品。
4. SSE 推送 `queued/parsing/analyzing/generating/validating/committing/completed/failed`，复用现有知识任务进度基础设施。

### P2：融合检索

1. 保留现有 `rag_search` 与 `read_page_range`，新增 `wiki_keyword_search` 和一跳 `wiki_graph_expand`。
2. 先用 RRF 融合排名，避免直接相加不可比的 LlamaIndex score、关键词 score 和图分数；答案上下文同时允许原始 chunk 和 Wiki 页面，但最终 citation 指向原始证据。
3. 建立离线评测集，至少覆盖语义问答、页码范围、跨文档综合、矛盾发现和中文查询；再决定是否需要独立 Wiki embedding。

### P3：复杂解析与增量维护

1. 接入现有/规划中的 MinerU Worker 作为可选 parser adapter；保留轻量 parser fallback、超时、容量限制和许可证复核。
2. 文档变更时按 source ownership 标记受影响 Wiki 页，生成差异草稿而非全库重写。
3. 再增加 orphan、失效引用、冲突、无来源断言和过期页面的定期 lint；发布仍需权限检查和审计。

## 不建议照搬的部分

- 不在 Next.js 浏览器端直接访问 LLM/Embedding API 或持有供应商密钥；统一由 .NET provider client 执行。
- 不把 LanceDB 作为第二套权威索引；短期沿用 LlamaIndex 与现有版本化存储，除非评测证明独立 Wiki 向量表有净收益。
- 不让模型输出任意相对/绝对路径；模型只产出领域 DTO，后端负责 slug、目录、权限与原子提交。
- 不把 Markdown 文件状态直接等同业务状态；服务端数据库保存租户、版本、Job、Review、发布状态与审计，Markdown 只作可导出、可浏览的派生物。
- 不以 README 的单次 benchmark 决定上线；使用 AiAgent 自己的中英文、长文、页码和跨文档集合做回归。
