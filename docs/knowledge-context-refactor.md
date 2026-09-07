# 知识与上下文重构方案

状态：提案（不改变现有线上行为）  
范围：AiAgent 知识库、项目 Markdown、会话文件、长期记忆与 Agent 检索上下文  
参考：`E:\项目\know-why\OpenViking` 的分层检索、异步语义处理与上下文寻址设计

## 1. 背景与问题

AiAgent 当前知识库主链路为：上传文件、生成文档记录、通过 LlamaIndex worker 构建索引、激活一个索引版本、由 Agent 通过 RAG 工具检索片段。

该链路已具备可用的基础设施：知识库版本、后台任务、chunk 落库和引用信息。但用户在会话中实际使用的上下文还包括项目 Markdown、上传附件、代码库资料和长期记忆；这些对象分别由不同服务管理，检索规则和权限范围没有统一模型。

当前主要问题：

- 多知识库检索逐库执行，再直接拼接结果，缺少全局排序、去重和统一 token 预算。
- 索引以平铺 chunk 为主要召回单位，无法先定位“哪个项目、文件夹、文件或章节”再精读。
- 文件索引、会话附件、项目资料和长期记忆之间没有可追溯的关联。
- 索引流程是一个后台任务，缺少解析、分块、摘要、向量化等阶段的独立状态、重试与恢复能力。
- 版本只覆盖知识库索引；来源快照、解析器版本、Embedding 配置与检索策略无法形成可比较的完整上下文版本。

## 2. 重构目标

1. 将所有可供 Agent 使用的资料收敛为统一的“上下文资源”，但不替换既有项目、会话、用户和权限体系。
2. 提供统一检索入口，能跨多个知识库、项目资料、附件和记忆进行全局排序、去重和引用输出。
3. 支持分层检索：先以资源摘要定位，再下钻到片段，减少无关 chunk 对上下文窗口的挤占。
4. 将索引处理拆成可观察、可重试、可回滚的异步流水线。
5. 保持现有 LlamaIndex worker 可用，避免一次性迁移全部历史知识库。

## 3. 非目标

- 第一阶段不更换现有 SQL Server、SqlSugar、项目权限或会话审计。
- 第一阶段不要求部署 OpenViking、VikingDB、AGFS 或其完整 Python 服务端。
- 不把“知识库重构”等同于“自动抽取和永久保存所有聊天内容”；长期记忆必须受用户、项目和策略控制。
- 不直接复制 OpenViking 源代码。其主项目采用 AGPLv3，若未来考虑嵌入或分发，应先进行许可证评估。

## 4. 现状边界

| 现有模块 | 职责 | 保留方式 |
| --- | --- | --- |
| `KnowledgeBaseManager` | 知识库、文档、版本、任务元数据 | 作为知识库领域 Adapter 保留 |
| `KnowledgeTaskRunner` | 后台发起索引任务并激活版本 | 改为调用新的索引流水线 |
| `LlamaIndexPipeline` | Python worker 的索引与查询实现 | 作为第一个向量/检索 Adapter 保留 |
| `KnowledgeRetrievalService` | Agent 的知识库查询和页码读取 | 改为统一检索模块的兼容门面 |
| `MemoryService` | 会话记忆候选与长期记忆 | 作为一种上下文资源接入，不直接混入文档索引 |
| 项目 Markdown / 附件服务 | 项目和会话范围内的文件上下文 | 作为资源来源接入 |

## 5. 目标模型

### 5.1 统一资源模型

新增一个深模块：`IContextCatalog`。调用方只需要声明资源来源和作用域，不需要了解文件路径、chunk、向量库或摘要的存储细节。

```text
ContextResource
├─ knowledge_document       知识库文件
├─ project_markdown         项目 Markdown
├─ chat_attachment          会话附件
├─ memory_item              经确认的长期记忆
├─ skill_document           Skill / 职责说明
└─ code_document            可选：已批准纳入检索的代码文档
```

每个资源至少有以下稳定字段：

```text
ResourceId          全局 ID
ResourceType        资源类型
OwnerScope          user / project / team / system
OwnerId             对应用户或项目 ID
SourceUri           稳定逻辑地址，而非临时物理路径
SourceVersion       来源内容版本或哈希
Title / MimeType    展示及解析信息
LifecycleStatus     draft / processing / ready / failed / archived
AccessPolicyRef     指向既有 AiAgent 权限判断结果
```

`SourceUri` 可采用内部命名规范，例如：

```text
aiagent://project/42/knowledge/product-manual/files/123
aiagent://project/42/markdown/repository/docs/api.md
aiagent://user/18/memory/preferences/ui-style
aiagent://session/abc/attachment/xyz
```

URI 的作用是稳定引用和检索范围表达，不应替代数据库主键，也不应绕开既有权限校验。

### 5.2 内容与语义层

一个资源可以拥有多个内容版本；一个内容版本可以产生多种检索材料：

```text
ContextResource
  └─ ContextSourceVersion
       ├─ ContextSegment        原始或解析后的片段
       ├─ ContextSemanticNode   文件/章节/目录摘要
       └─ ContextIndexVersion   某次 Embedding 与检索配置快照
```

`ContextSemanticNode` 是本次重构的关键新增对象。它可代表：项目、知识库、目录、文件、章节或记忆主题；其摘要中保留子节点引用。检索时先命中语义节点，再选择对应片段展开。

## 6. 核心接口

外部调用面保持小而稳定，复杂度收敛在实现内：

```csharp
public interface IContextRetrieval
{
    Task<ContextSearchResult> SearchAsync(
        ContextSearchRequest request,
        CancellationToken cancellationToken);

    Task<ContextReadResult> ReadAsync(
        ContextReadRequest request,
        CancellationToken cancellationToken);
}
```

`ContextSearchRequest` 只包含：查询文本、调用用户、允许的项目/知识库/会话范围、资源类型过滤、token 预算和检索意图。它不暴露向量库目录、chunk 大小或 provider 细节。

```text
Agent / Chat / WorkCanvas
        │
        ▼
IContextRetrieval
        │
        ├─ ScopeResolver          根据用户、项目、会话过滤可见资源
        ├─ RetrievalPlanner       判断是否需要目录摘要、关键词、向量或精读
        ├─ CandidateRetriever     多来源并行召回
        ├─ RankAndDeduplicate     全局排序、同源去重、预算裁剪
        └─ CitationAssembler      生成稳定 URI、标题、页码和版本引用
```

现有 `KnowledgeRetrievalService` 在第一阶段改为调用 `IContextRetrieval`；原来的 `ReadPageRangeAsync` 仍保留，作为 `ContextReadRequest` 的兼容 Adapter。

## 7. 索引流水线

借鉴 OpenViking 的语义 DAG 思路，但实现为 AiAgent 可控的后台作业：

```text
source snapshot
  → parser
  → normalize
  → segment
  → semantic summary
  → embedding
  → validation
  → activate
```

每一阶段应记录：输入版本、开始/结束时间、状态、可重试次数、错误摘要、产物位置、处理器版本和配置摘要。

必须满足的业务不变量：

- 新索引在所有验证通过前不能替换当前 `ActiveVersionId`。
- 同一 `SourceUri + SourceVersion + PipelineConfigHash` 的重复提交应幂等。
- 单文件解析失败不得导致其他文件索引全部不可用；失败资源必须可定位和单独重试。
- 删除或权限收回后，资源在检索侧必须立刻不可见，即使异步向量物理清理尚未完成。
- 索引版本激活与数据库状态更新应在同一事务边界中完成；向量产物使用“先写新版本、再原子切换”的策略。

## 8. 检索策略

### 8.1 分层检索

1. 先由 `ScopeResolver` 确定用户可见的资源范围。
2. 对知识库、目录、文件、记忆主题等语义节点执行轻量召回。
3. 从排名靠前的节点下钻至 `ContextSegment`，执行向量或关键词混合检索。
4. 对全部来源进行全局重排、去重和 token 预算裁剪。
5. 输出可读引用和可回溯 URI；Agent 需要更多信息时再按 URI 精读。

### 8.2 多知识库改造

现有多知识库查询会依次调用每个库。改造后应并行召回候选，并将结果归一为统一分数与来源权重，再进行全局 Top-K。

建议先定义显式的评分记录，而不要混合不同向量引擎的原始分数：

```text
FinalScore = SemanticScore × SourceWeight × FreshnessWeight × ScopeWeight
```

第一期可先只记录各项分数，不立刻启用复杂重排模型；这样可以用真实会话日志校准权重。

### 8.3 记忆处理

长期记忆与文档的区别在于其生命周期和权限，而不是是否可以检索。记忆资源应至少带有：

- 作用域：用户、项目、团队、会话。
- 类型：偏好、事实、决策、经验、待办。
- 有效期与失效方式。
- 来源会话和人工确认记录。

默认策略：会话级记忆只在当前会话检索；项目级记忆需项目权限；用户偏好不应泄露给其他用户或被写入项目文档索引。

## 9. 分期实施

### Phase 0：观测与基线

- 为现有检索记录查询、知识库、索引版本、候选数、耗时、命中 chunk、最终引用和失败原因。
- 准备一组真实问题的离线评测集，涵盖单库、多库、页码读取、项目 Markdown、附件和记忆。
- 不修改 UI 和现有检索结果。

验收：可以回答“某次回答用了哪些来源、耗时在哪个阶段、没有命中是因为没有资源还是没有召回”。

### Phase 1：统一检索门面

- 新增 `IContextRetrieval`、范围解析和统一引用 DTO。
- 仅将知识库接入；`LlamaIndexPipeline` 仍是唯一检索 Adapter。
- 将多知识库串行检索改为并行候选召回与全局 Top-K。
- 保留旧 `KnowledgeRetrievalService`，用 Feature Flag 在旧链路与新门面之间切换。

验收：旧会话功能不回归；多库查询结果含来源名称和稳定 URI；关闭开关即可回退旧链路。

### Phase 2：资源目录与语义节点

- 为知识库文件和项目 Markdown 建立 `ContextResource`、版本与语义节点。
- 在索引流水线中加入文件摘要、章节摘要和目录摘要。
- 实现“摘要定位 → 片段下钻 → 按 URI 精读”。

验收：用户可从回答引用跳转到文件/章节；目录级问题不再只返回随机 chunk。

### Phase 3：附件与记忆接入

- 会话附件通过短生命周期资源接入；过期时从检索范围移除。
- 将已确认的 `AiMemoryItem` 映射为 `memory_item` 资源，不迁移未确认候选。
- 实现资源—记忆关系和人工撤销。

验收：不同用户、项目与会话之间不存在越权召回；删除/撤销后检索立即不可见。

### Phase 4：索引 DAG 与运营能力

- 将索引任务拆分为可重试阶段，提供阶段状态、失败文件清单和单资源重试。
- 引入评测、召回质量监控、成本/耗时指标和灰度策略。

验收：单文件失败不阻塞其他资源；新索引失败不影响当前激活版本；可比较两版索引质量。

## 10. 迁移与回滚

迁移采用“双写可选、双读灰度、单向切换”的方式：

1. 历史 `AiKnowledgeBase` / `AiKnowledgeDocument` 保持不变。
2. 新资源表先从已有文档和活动索引版本回填，使用文件哈希识别重复项。
3. 新检索门面先只读新资源目录、底层仍调用既有 LlamaIndex 索引。
4. 按项目或管理员开关启用新结果；记录旧/新检索候选差异。
5. 验证稳定后才让新索引流水线成为默认写入路径。

任何阶段均应可以通过 Feature Flag 回退到原 `KnowledgeRetrievalService + LlamaIndexPipeline` 组合；不允许删除历史索引版本作为迁移前提。

## 11. 初始数据表建议

第一期只新增，避免修改历史表：

```text
ai_context_resource
ai_context_source_version
ai_context_segment
ai_context_semantic_node
ai_context_index_run
ai_context_index_stage
ai_context_resource_link
```

其中权限不复制到资源表；只保留 `OwnerScope / OwnerId / AccessPolicyRef`。每次检索仍由 AiAgent 现有认证、项目成员关系和会话授权决定可见范围。

## 12. 风险与决策点

| 风险 | 处理方式 |
| --- | --- |
| 一次替换检索导致历史回答质量回退 | 双读灰度、评测集、Feature Flag 回滚 |
| 摘要生成成本和延迟增加 | 仅对文件/目录版本变化时生成；限并发、缓存和按需下钻 |
| 多来源权限泄露 | 检索前先做范围过滤，向量召回不作为授权依据 |
| 不同引擎分数不可比 | 归一化后再排序，并保留原始分数用于观测 |
| 记忆污染知识库 | 记忆必须有来源、确认状态、类型、作用域和撤销能力 |
| 直接复用 OpenViking 代码的许可证风险 | 只借鉴架构；任何代码/部署复用先完成许可证评估 |

## 13. 建议的首个开发切片

先做 Phase 1，不创建新向量库：

1. 定义 `IContextRetrieval` 与统一 `ContextCitationDto`。
2. 用现有知识库作为唯一 Adapter 实现该接口。
3. 并行执行多知识库查询，统一去重、排序和 token 预算。
4. 在 Agent 结果中同时记录新旧链路的候选统计，暂不改变默认答案。
5. 增加覆盖单库、多库、权限范围和回退开关的测试。

这个切片的价值是先建立清晰的接口与可观测性；之后接入项目 Markdown、附件和长期记忆时，调用方不需要再次了解底层索引细节。
