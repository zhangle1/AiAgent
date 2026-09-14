# AiAgent 公司级知识中心规划

> 文档状态：规划稿  
> 更新时间：2026-09-14  
> 适用范围：AiAgent `Knowledge / RAG / CodeRepository / Chat` 领域

## 1. 结论

建议不要另起一套“LLM Wiki”，而是在现有知识库之上建设一个独立的 **Knowledge Center（知识中心）领域层**：用统一资源目录管理公司、项目、人员、技能和代码知识，用现有 RAG 能力负责解析与召回，再通过权限网关向内部 Agent 和外部 API 提供同一套可审计的检索服务。

推荐的目标形态是：

```text
数据源 -> 采集/快照 -> 解析 -> 自动提炼 -> 审核发布 -> 混合检索 -> 引用式回答/API
            |                         |
            +---- 版本与血缘 ----------+

统一资源地址：guokun://{tenant}/{scope}/{owner}/{collection}/{path}
```

首期应聚焦“目录、权限、上传、提炼、检索、引用、API Key”七件事，不要一开始就投入知识图谱、自学习 Agent 或跨组织联邦检索。

## 2. 当前代码库能力盘点

当前实现已经具备可复用底座：

| 已有能力 | 代码位置 | 可复用方式 |
| --- | --- | --- |
| 知识库增删查、文档上传、重建索引、搜索 | `backed/Services/Knowledge/KnowledgeAppService.cs` | 保留为兼容 API，逐步转调新领域服务 |
| 文档、Chunk、索引版本、后台 Job | `backed/Entities/Knowledge/` | 作为旧数据模型与索引 Adapter |
| 原文件安全落盘与 SHA-256 | `KnowledgePathService.cs` | 继续作为受控文件存储实现 |
| LlamaIndex / 本地索引构建 | `KnowledgeTaskRunner`、`KnowledgeIndexMaterializer` | 接到新的处理流水线 |
| 前端知识库管理与检索 | `front/components/knowledge/`、`front/lib/knowledge-*` | 升级为知识中心入口 |
| 用户—代码项目授权关系 | `AiUserCodeProject` | 作为项目知识权限的首个事实来源 |
| Agent RAG 与按页读取工具 | `Services/Chat/Agentic` | 改接统一检索门面 |

当前主要缺口：

- `AiKnowledgeBase` 没有公司、项目、人员等归属字段，也没有成员级授权模型。
- 现有路由以知识库名称为中心，缺少稳定资源 URI、目录浏览和跨库统一搜索。
- 上传后以“切片 + 索引”为主，缺少摘要、实体、技能卡、项目说明等自动提炼产物及人工审核。
- 外部调用尚未产品化：缺少 API Client、Scope、密钥轮换、限流、配额、审计和版本契约。
- 知识库 API 需要确认并统一接入当前认证上下文；不能只依靠路径中的 `kbName` 判断访问权。

## 3. GitHub 同类项目调研与取舍

### 3.1 OpenViking：借鉴资源 URI 与分层上下文

OpenViking 用 `viking://{scope}/{path}` 统一资源、用户、Agent 和会话上下文，并在目录中提供 abstract、overview 和原文等不同信息密度。它还提供类似文件系统的 list/read/find 接口。

适合借鉴：

- 稳定 URI，而不是把数据库 ID、真实文件路径暴露给 Agent。
- “目录摘要 -> 文档摘要 -> 原文片段”的渐进式读取。
- 资源可以被浏览、精读和检索，不把向量搜索当作唯一入口。

不建议首期照搬：

- 不替换现有 SQL Server、SqlSugar、文件存储和 LlamaIndex 链路。
- 不把虚拟 URI 映射成可由调用方直接访问的服务器路径。
- 若未来嵌入或分发其实现，需单独评估许可证和运维成本。

### 3.2 RAGFlow：借鉴解析可见性和检索测试

RAGFlow 将数据集、文件解析、分块模板、Chunk 预览和检索测试做成可运营功能，强调不同文档类型应采用不同解析方式。

适合借鉴：

- 文件级解析策略与启停开关。
- 可查看、修正、禁用 Chunk，并保留修改记录。
- 发布前用问题集进行检索测试，而不是“索引成功即上线”。
- PDF、Office、Markdown、表格、图片采用不同解析器。

### 3.3 Dify / AnythingLLM：借鉴成员权限与服务 API

Dify 的数据集权限区分仅本人、全团队和部分成员，并支持外部知识源；AnythingLLM 的 OpenAPI 也显式描述 Workspace 与用户授权。

适合借鉴：

- 知识集合拥有者与可见成员分开建模。
- 外部 API 和 Web 用户共用授权内核，但凭证、配额和审计分开。
- API 的检索结果返回来源、分数、资源地址和版本，而不只返回生成答案。

## 4. 产品信息架构

建议把前端 `/knowledge` 升级为五个一级模块：

| 模块 | 主要用户 | 核心功能 |
| --- | --- | --- |
| 知识门户 | 全体员工 | 搜索、问答、收藏、最近更新、按项目/人员浏览 |
| 知识空间 | 项目负责人、知识管理员 | 空间、集合、目录、成员、标签、生命周期 |
| 数据接入 | 内容维护者 | 文件上传、代码库同步、URL/API/数据库连接、同步计划 |
| 加工与发布 | 内容维护者、审核者 | 解析预览、自动提炼、人工修订、质量检查、版本发布 |
| 开放平台 | 管理员、开发者 | API Client、Scope、配额、调用日志、Webhook、接口文档 |

角色建议：

- `platform_admin`：组织级策略、模型、配额和审计。
- `knowledge_admin`：管理全部知识空间，不自动拥有敏感原文导出权限。
- `space_owner`：管理某空间、成员、数据源和发布。
- `editor`：上传、同步、编辑提炼结果。
- `reviewer`：批准或驳回发布版本。
- `viewer`：浏览和检索已发布知识。
- `api_client`：仅按授予的 Scope 机器访问。

## 5. `guokun://` 命名空间设计

用户给出的 `guokun:/{project}/{code}` 与 `guokun:/{user}/{skill}` 可以升级为带租户和明确作用域的规范：

```text
guokun://acme/org/handbook/hr/leave-policy.md
guokun://acme/project/aiagent/code/backed/Services/Knowledge/README.md
guokun://acme/project/aiagent/wiki/architecture/knowledge-center.md
guokun://acme/user/u-1024/skill/dotnet-api-review.md
guokun://acme/team/platform/runbook/release.md
```

URI 规则：

```text
guokun://{tenant_slug}/{scope}/{owner_slug}/{collection}/{logical_path}

scope      = org | project | team | user
collection = wiki | code | document | skill | runbook | decision | dataset
```

关键约束：

- URI 是逻辑定位符，不是物理文件路径，也不替代数据库主键。
- `tenant + canonical_uri` 必须唯一；重命名通过 alias/redirect 保留旧引用。
- 目录以 `/` 结尾，资源 URI 不以 `/` 结尾。
- URI 只表达位置，不表达权限；每次 list/read/search 都必须进行服务端鉴权。
- 用户展示名、项目名变化不直接改变 URI，使用不可变 slug 或内部稳定编号。
- 搜索结果必须带 `canonical_uri`、`resource_version`、标题、片段位置和可访问引用链接。

## 6. 领域模型

### 6.1 核心对象

```text
KnowledgeSpace
  └─ KnowledgeCollection
       └─ KnowledgeResource
            ├─ ResourceVersion
            │    ├─ SemanticNode
            │    └─ KnowledgeChunk
            ├─ ResourceRelation
            └─ AccessControlEntry

KnowledgeSource -> IngestionRun -> ProcessingStage -> Publication
ApiClient -> ApiGrant -> ApiUsageLog
```

| 对象 | 作用 | 关键字段 |
| --- | --- | --- |
| `KnowledgeSpace` | 公司/项目/团队/个人知识边界 | tenant、scope、owner、visibility、status |
| `KnowledgeCollection` | 空间内业务集合 | type、slug、schema、default_pipeline |
| `KnowledgeResource` | 稳定逻辑资源 | canonical_uri、source_type、current_version、classification |
| `ResourceVersion` | 不可变内容快照 | content_hash、source_revision、parser_version、status |
| `SemanticNode` | 目录/文档/章节的 L0/L1 摘要 | level、summary、parent_id、token_count |
| `KnowledgeChunk` | 可召回原文片段 | text、page/line、embedding_ref、metadata |
| `KnowledgeArtifact` | 自动提炼产物 | artifact_type、content、confidence、review_status |
| `KnowledgeSource` | 上传或同步来源 | upload/git/url/api/database、schedule、cursor |
| `ResourceRelation` | 资源关系 | depends_on、authored_by、belongs_to、supersedes |
| `AccessControlEntry` | 主体对资源的授权 | subject_type/id、resource_prefix、actions、effect |
| `Publication` | 一次可回滚发布 | release_no、index_version、approved_by、published_at |

所有新增后端实体字段都应遵守项目约定，显式标注 `[SugarColumn(IsNullable = true)]`；需要非空语义时先由 Service 校验，完成历史数据迁移与回填验收后再收紧数据库约束。

### 6.2 自动提炼产物

上传或同步后的原文不能被 LLM 静默覆盖。建议将 AI 结果保存为独立 Artifact：

- `abstract`：约 100 tokens 的一句话/短摘要。
- `overview`：约 1,000–2,000 tokens 的结构化概览。
- `outline`：章节、页面或代码模块目录。
- `faq`：可审核的问题—答案对。
- `entities`：人员、系统、项目、接口、术语。
- `decision`：决策、背景、结论、责任人、日期。
- `skill_card`：个人技能、证据来源、熟练度和有效期。
- `code_map`：模块、入口、依赖、所有者、关键符号。

每个 Artifact 必须保留 `prompt_version`、模型、输入版本、生成时间、置信度、审核状态和来源引用。人员技能属于敏感派生数据，默认仅本人和获授权管理者可见，并应支持本人纠错、撤回和设置有效期。

## 7. 处理流水线

```text
Discover
  -> Snapshot
  -> Virus/type/size validation
  -> Parse/OCR
  -> Normalize
  -> Segment
  -> Extract metadata
  -> Generate abstract/overview/artifacts
  -> Embed + keyword index
  -> Quality gates
  -> Human review (按策略)
  -> Atomic publish
```

建议状态：`draft -> queued -> processing -> review_required -> ready -> published -> archived`，失败进入 `failed`，可从失败阶段幂等重试。

必须满足：

- 使用 `source_version + pipeline_config_hash` 保证幂等。
- 新版本校验通过后再原子切换，失败不影响当前已发布版本。
- 删除或权限收回后，检索层先逻辑隔离，物理向量清理由异步任务完成。
- 单文件失败不阻塞同批其他文件；阶段错误可观察、可重试。
- Git 数据源保存 commit SHA；数据库/API 数据源保存游标或水位线。
- 原文、AI 派生产物、人工修订内容分别存储，不互相覆盖。

## 8. 检索与回答架构

```text
Caller
  -> Identity/API-key authentication
  -> ScopeResolver（可见 URI 前缀）
  -> QueryPlanner（搜索、目录浏览、精读、结构问答）
  -> Hybrid retrieval（BM25 + vector + metadata）
  -> Global rerank/deduplicate
  -> Policy filter（再次鉴权）
  -> Token budget
  -> CitationAssembler
  -> 可选 AnswerGenerator
```

API 要把“检索”和“生成回答”拆开：搜索接口返回确定性的候选和引用；回答接口在其上增加模型生成。这样外部系统可以只消费证据，不被迫消费生成文本。

首期排序可以使用可解释组合分：

```text
final_score = semantic * 0.45
            + keyword  * 0.25
            + title    * 0.10
            + freshness* 0.05
            + authority* 0.15
```

权重不是固定真理，应通过离线问题集和真实点击/引用反馈校准。权限过滤需在候选召回前后各执行一次，禁止先取回越权原文再交给模型过滤。

## 9. 外部 API 设计

统一新增 `/api/v1/knowledge-center`，旧 `/api/v1/knowledge` 暂时兼容：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/spaces` | 列出有权访问的空间 |
| `POST` | `/spaces` | 创建知识空间 |
| `GET` | `/resources?parent_uri=...` | 浏览目录 |
| `POST` | `/resources/uploads` | 上传并返回不透明资源 ID |
| `GET` | `/resources/{id}` | 读取元数据与当前版本 |
| `GET` | `/resources/{id}/content` | 读取授权后的正文 |
| `POST` | `/search` | 按 URI、标签、类型和项目范围检索 |
| `POST` | `/answers` | 基于检索证据生成带引用回答 |
| `POST` | `/sources` | 创建 Git/API/数据库同步源 |
| `POST` | `/sources/{id}/sync` | 发起同步 |
| `GET` | `/runs/{id}` | 查询处理流水线进度 |
| `POST` | `/publications/{id}/approve` | 审核发布 |

搜索请求示例：

```json
{
  "query": "知识中心的权限校验在哪里实现？",
  "target_uris": ["guokun://acme/project/aiagent/"],
  "resource_types": ["code", "wiki"],
  "top_k": 8,
  "include": ["abstract", "chunks"],
  "filters": { "status": "published" }
}
```

响应示例：

```json
{
  "request_id": "req_01...",
  "results": [
    {
      "resource_id": "res_01...",
      "uri": "guokun://acme/project/aiagent/code/backed/Services/Knowledge/KnowledgeAppService.cs",
      "version": "git:8ac2...",
      "title": "KnowledgeAppService",
      "score": 0.86,
      "snippet": "...",
      "location": { "start_line": 14, "end_line": 52 },
      "citation_url": "/knowledge/resources/res_01...?version=git%3A8ac2..."
    }
  ],
  "next_cursor": null
}
```

开放平台安全要求：

- API Key 仅展示一次，数据库只保存强哈希和可识别前缀。
- Scope 至少细分 `knowledge:list/read/search/answer/write/admin`，并限制 URI 前缀。
- 支持过期、轮换、吊销、IP allowlist、每分钟限流、日配额和最大 `top_k`。
- 所有读取和搜索记录 client、subject、URI 范围、命中资源、耗时和状态，但日志不保存完整敏感原文。
- 外部响应永不返回 `StoragePath`、向量路径、服务器绝对路径或内部异常堆栈。
- 使用 cursor 分页、`Idempotency-Key`、标准错误码和 API 版本；后续可发布 OpenAPI 3.1 文档。

## 10. 后端模块落位

建议新增：

```text
backed/Services/KnowledgeCenter/
├─ Catalog/          # Space、Collection、Resource、URI
├─ Sources/          # Upload、Git、URL、API、Database adapters
├─ Processing/       # DAG、parser、extractor、artifact、publication
├─ Retrieval/        # scope、planner、hybrid、rank、citation
├─ Permissions/      # RBAC + URI-prefix ACL
├─ OpenApi/          # API client、grant、quota、audit
└─ KnowledgeCenterAppService.cs

backed/Entities/KnowledgeCenter/
backed/Dtos/KnowledgeCenter/
front/lib/knowledge-center-{api,types}.ts
front/components/knowledge-center/
```

Controller/Dynamic API 只做 HTTP 参数、身份和响应映射；URI 校验、权限、文件操作、同步和外部进程均放在 Service。`KnowledgeAppService` 保留旧契约，通过 Adapter 调用新检索服务，避免一次性改坏 Chat。

## 11. 前端页面建议

```text
/knowledge                         知识门户
/knowledge/spaces                  空间列表
/knowledge/spaces/{id}             空间概览、成员、集合
/knowledge/resources/{id}          原文、摘要、版本、血缘、权限
/knowledge/processing              任务、失败重试、质量门禁
/knowledge/retrieval-lab            检索测试与版本对比
/knowledge/open-api                Client、Scope、配额、日志
```

资源详情采用四栏/标签式信息：内容预览、AI 提炼、来源与版本、权限与审计。检索实验室至少展示关键词分、向量分、最终分、命中 Chunk、过滤原因和索引版本。

## 12. 分阶段实施

### Phase 0：基线与安全补强（1–2 周）

- 给现有知识 API 增加统一身份解析和服务端授权检查。
- 建立 30–50 个真实问题的离线评测集，记录 Recall@K、MRR、引用正确率和越权用例。
- 为现有搜索记录 trace、耗时、索引版本和引用。
- 确认上传类型/大小/签名、路径根校验和审计满足现有安全边界。

验收：任一知识操作都能回答“谁、何时、以什么权限、访问了哪个逻辑资源”；越权测试为 0 泄漏。

### Phase 1：空间、资源目录与 URI（2–3 周）

- 新增 Space、Collection、Resource、Version、Alias、ACE 表和 Service。
- 将旧知识库映射到 `org` 或 `project` 空间，不迁移/删除旧索引。
- 实现 list/read 和 URI 前缀授权。
- 新前端完成按公司、项目、人员浏览。

验收：可通过 `guokun://` 稳定定位资源；重命名后旧 URI 可跳转；项目成员权限沿用 `AiUserCodeProject`。

### Phase 2：自动提炼与发布（3–4 周）

- 把 Job 拆为可重试阶段，生成 abstract、overview、outline、FAQ。
- 增加 Artifact 审核、人工修订、版本比较和原子发布。
- 建设解析/Chunk 预览和检索实验室。

验收：原文与 AI 结果有完整血缘；失败版本不影响线上；审核者可回滚到上一发布。

### Phase 3：项目代码与个人技能（3–5 周）

- 代码仓按 commit 增量同步，提炼模块图、符号摘要和文档关系。
- 个人技能只从授权资料生成证据化 Skill Card，默认私有、可纠错、可撤回。
- Chat/Agent 工具改接统一 Search/Read，支持目录摘要后下钻。

验收：代码答案可定位到 commit + 文件 + 行号；人员技能每条结论都有来源和可见性说明。

### Phase 4：开放 API（2–3 周）

- API Client、Grant、Key 轮换、配额、限流、审计。
- 发布 Search、Read、Answer API 和 OpenAPI 文档。
- 增加 webhook：资源发布、同步失败、权限变化。

验收：不同 API Client 只能访问授予的 URI 前缀；密钥吊销即时生效；调用可追踪但不泄漏原文。

### Phase 5：高级能力（按数据决定）

- 连接器市场、数据库 CDC、知识关系图谱、反馈学习、跨语言索引。
- 只有在评测证明混合检索无法解决关系型问题时，再引入 GraphRAG。

## 13. 建议的 MVP 范围

八周 MVP 建议只交付：

1. `org / project / user` 三种 Space。
2. `document / wiki / code / skill` 四种 Collection。
3. 文件上传与单个 Git 仓库按 commit 同步。
4. abstract、overview、outline 三种 Artifact。
5. viewer/editor/owner + 项目成员继承权限。
6. list/read/search 三个统一能力及带引用结果。
7. API Key 的 read/search Scope、限流和审计。
8. 旧知识库与 Chat 的兼容 Adapter。

明确延期：数据库直连、全量知识图谱、自动改写原文、跨租户共享、用户行为自动生成公开技能、由 Agent 自动批准发布。

## 14. 成功指标

| 维度 | MVP 建议门槛 |
| --- | --- |
| 安全 | 越权检索/精读测试 0 泄漏；吊销后立即不可见 |
| 质量 | 评测集 Recall@5 ≥ 0.80；引用可定位率 ≥ 95% |
| 时效 | 单文件上传 95% 在 5 分钟内可检索；Git 增量同步可观察 |
| 稳定性 | 新版处理失败不影响当前发布版；任务可幂等重试 |
| 可解释 | 100% 回答可返回资源 URI、版本与片段位置 |
| API | P95 搜索延迟建立基线后持续监控；所有请求有 request_id |
| 运营 | 过期资源、无人负责资源、失败任务均可在后台发现 |

## 15. 近期应做的决策

在编码前只需确认以下四点：

1. `guokun` 是正式产品协议名，还是内部代号；若可能改名，应把 scheme 做成配置但持久化 canonical scheme。
2. 第一租户是否固定为单公司；即使单租户，数据模型也建议预留 `TenantId`。
3. 个人 Skill Card 的可见性和合规审批人是谁。
4. 首个外部 API 消费者是谁、只需检索证据还是需要生成回答；这决定 MVP 是否包含 `/answers`。

若暂时没有答案，建议默认：单租户建模、多租户字段预留；个人技能默认私有；外部 API 首期只开放 list/read/search。

## 16. 参考资料

- [OpenViking：Viking URI](https://github.com/volcengine/OpenViking/blob/main/docs/en/concepts/04-viking-uri.md)
- [OpenViking：文件系统 API](https://github.com/volcengine/OpenViking/blob/main/docs/en/api/03-filesystem.md)
- [OpenViking 仓库](https://github.com/volcengine/OpenViking)
- [RAGFlow：配置知识库与解析](https://github.com/infiniflow/ragflow/blob/main/docs/guides/dataset/configure_knowledge_base.md)
- [RAGFlow：RAG 基础](https://github.com/infiniflow/ragflow/blob/main/docs/basics/rag.md)
- [Dify：Dataset Service API 与权限模型](https://github.com/langgenius/dify/blob/main/api/controllers/service_api/dataset/dataset.py)
- [Dify：Dataset Service 权限过滤](https://github.com/langgenius/dify/blob/main/api/services/dataset_service.py)
- [AnythingLLM OpenAPI](https://github.com/Mintplex-Labs/anything-llm/blob/master/server/swagger/openapi.json)

