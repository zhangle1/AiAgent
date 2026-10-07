# AiAgent 知识库重构方案：Viking Context Workspace

> 状态：第一阶段设计与基础实现计划
>
> 日期：2026-10-07
>
> 目标：参考 OpenViking 的资源树、分层上下文和可观察任务模型，重构 AiAgent 的知识库；保留现有 raw、Markdown 知识页、Wiki 检索和可选 RAG 的兼容能力。

## 1. 设计结论

知识库统一成为一个可浏览的上下文空间。用户上传的文件首先进入 L2 原始资源；系统按需生成目录级 L0 摘要、L1 概览和文件级 Markdown 表示层；Agent 先定位目录，再读取概览，最后按引用需要读取原文或知识页。

每个资源都有稳定的 `viking://` URI。URI 是用户和 Agent 看到的定位方式，数据库自增 Id 只作为内部关联键。第一阶段使用以下命名空间：

```text
viking://resources/{knowledge-base}/        共享项目资料
viking://resources/{knowledge-base}/{path}  文件和目录
viking://user/{user-id}/resources/{path}    用户私有资料（预留）
viking://user/{user-id}/memories/{path}     会话记忆（预留）
viking://agent/skills/{path}                Agent 技能（预留）
```

现有 API 路由继续工作，新的 URI、层级、处理状态和引用信息通过 DTO 增量返回，避免一次迁移破坏聊天和已有知识库。

## 2. OpenViking 可借鉴的部分

| OpenViking 机制 | AiAgent 对应实现 | 采用方式 |
| --- | --- | --- |
| `viking://{scope}/{path}` | `AiKnowledgeDocument`、`AiKnowledgeArtifact` | 增加规范化 URI 和路径校验；保留数据库 Id |
| L0 abstract | 知识库或目录的一句话摘要 | 目录级 Markdown sidecar，限制 256 字符 |
| L1 overview | 目录结构、主题和导航 | 目录级 Markdown sidecar，限制 4,000 字符 |
| L2 detail | raw 文件、解析文本、主题知识页 | 按需读取，不把完整原文放入检索上下文 |
| bottom-up semantic processing | 解析 → 文件摘要 → 目录 L1/L0 → 向量化 | 使用独立有界队列和任务状态 |
| `ls/tree/find/read` | 目录浏览、语义检索、页面读取、证据引用 | Wiki 检索模型只调用宿主提供的只读动作 |
| task center | `AiKnowledgeJob` 和任务中心 | 统一 ingestion、semantic、embedding、reindex 状态，保留任务类型 |
| retrieval trajectory | 检索步骤和引用轨迹 | 保存查询、访问 URI、读取层级和最终引用的脱敏记录 |

OpenViking 的 L0/L1 是目录级 sidecar，不是每个普通文件强行生成两份摘要。目录摘要只聚合直接子项的摘要；原始文件和完整解析内容仍属于 L2。这个边界能降低 token 消耗，也让 Agent 可以逐层扩大上下文。[OpenViking Context Layers](https://github.com/volcengine/OpenViking/blob/main/docs/en/concepts/03-context-layers.md)

## 3. 目标数据模型

### 3.1 Context node

新增 `AiKnowledgeContextNode`，表示 URI 树中的目录、L0、L1、L2 资源和知识页映射。

```text
Id
KnowledgeBaseId
ParentId
Uri                       唯一规范化 viking:// URI
Name
NodeType                  namespace | directory | document | artifact | abstract | overview
Layer                     0 | 1 | 2
SourceDocumentId          可空
ArtifactId                可空
ContentPath               受控文件路径，可空
ContentHash
Status                    pending | processing | ready | stale | error
MetadataJson
CreatedAt / UpdatedAt
```

所有新增实体字段显式标记可空属性，沿用现有 SqlSugar Code First 规则。现有文档、解析文档、artifact 和 chunk 不删除；context node 负责把它们组织成 URI 树。

### 3.2 Semantic sidecar

L0/L1 内容使用受控 Markdown 文件或 artifact 存储，带最小元数据：

```yaml
---
directory: viking://resources/aiagent/docs/
generated_by:
  component: AiAgent.SemanticProcessor
  trigger: resource_ingest
freshness:
  total_entries: 12
  pending_child_changes: 0
---
```

前端预览显示正文，原始 sidecar 元数据只在诊断面板显示。用户编辑 sidecar 正文时保留受保护元数据。

### 3.3 Provider profile

知识库模型能力从通用模型目录中选择，保存快照到知识库配置：

```text
semantic_model_id       L0/L1 和知识页生成
vlm_model_id            PDF 页面、图片、扫描件的视觉理解，可空
embedding_profile_id    向量服务配置档
embedding_model_id      向量模型
embedding_dimension     实际探测维度
retrieval_mode          wiki | vector | hybrid
```

这样同一知识库可以使用 DeepSeek 进行文本推理、使用 VLM 解析图像或扫描页、使用 Ollama 或 SiliconFlow 生成向量，且索引版本能记录当时的配置签名。

## 4. 模型与向量能力

### 4.1 文本推理与 VLM

复用现有 OpenAI-compatible LLM 客户端，新增内容块协议：文本消息仍走 `content: string`；VLM 消息使用 `content: [{ type: "text" }, { type: "image_url" }]`。图片只允许来自后端受控临时目录或受控 data URL，不接受浏览器本地路径。

DeepSeek 的文本模型用于分析和知识页生成；VLM 使用配置目录中的视觉模型，模型不可用时该文档任务明确标记为 `vlm_unavailable`，不影响纯文本文件入库。对图片、扫描 PDF 和复杂表格，解析器先生成受限图像输入，再由 VLM 返回结构化 Markdown 和页码证据。

### 4.2 Ollama Embedding

增加原生 `ollama` embedding adapter：默认地址 `http://localhost:11434`，请求 `/api/embed`，发送 `model` 与 `input` 数组，支持批量返回向量。无 API Key 时不附加 Authorization；连接失败返回可读的 provider 错误和修复建议。

### 4.3 SiliconFlow Embedding

增加 `siliconflow` OpenAI-compatible embedding adapter：默认基础地址 `https://api.siliconflow.cn/v1`，请求 `/embeddings`，携带 `Authorization: Bearer <key>`，请求体使用 `model`、`input` 和 `encoding_format: "float"`。模型维度以接口响应为准并写入索引版本，不允许手工配置错误维度导致索引静默损坏。

SiliconFlow 官方接口支持字符串或字符串数组输入，并返回 `data[].embedding`；服务端应记录响应的 trace id（若有）到任务诊断元数据，不记录 API Key。[SiliconFlow Embeddings API](https://api-docs.siliconflow.cn/docs/api/embeddings-post)

### 4.4 配置页面

知识库设置拆成三个区块：

1. 语义处理：推理模型、VLM 模型、推理强度、最大输出和超时。
2. 向量服务：Ollama / SiliconFlow / 其他 OpenAI-compatible、模型、地址、密钥和维度探测。
3. 检索策略：Wiki 分层检索、向量检索或混合检索，候选倍数和重排策略。

“连接测试”只发送固定短文本或固定测试图片，不发送知识库原文；结果显示 provider、模型、维度、延迟和 trace id。

## 5. 处理链路

```text
上传源文件
  ↓
L2 raw 资源保存（URI、哈希、来源、权限）
  ↓ 异步任务：parse
解析文本 / 页码 / 图片区域 / 原始定位
  ↓ 异步任务：semantic
文件摘要 → 目录 L1 → 目录 L0
  ↓ 可选任务：compile
主题知识页 + 逐字证据 + 审核状态
  ↓ 可选任务：embed
L0/L1/知识页向量化 → 索引版本
  ↓ 查询
find / tree / read / cite 轨迹 → 结果和引用
```

任务状态必须可见：`queued → processing → completed | failed | cancelled`。每个任务记录阶段、进度、重试次数、模型快照、错误编码和可重试性。服务重启时将未完成任务标记为 `interrupted`，由用户明确重试。

## 6. 检索协议

默认使用分层 Wiki 检索：

1. 从知识库 URI 根目录读取 L0 目录摘要。
2. 对候选目录读取 L1 概览，决定是否继续深入。
3. 对候选页面或 L2 文件执行受限 `read`。
4. 引用动作只接受已读取内容中的逐字片段。
5. 返回 URI、层级、源文件、页码、知识页审核状态和引用文本。

当用户选择 vector/hybrid 时，向量候选只用于定位，最终仍读取 L1/L2 并执行证据校验。查询过程不得写入草稿、原文或索引；轨迹只保存必要的 URI 和统计信息。

## 7. 分阶段实施

### Phase 1：上下文模型与 provider 基础（本轮）

- 新增 URI、层级、节点状态和 provider 配置 DTO/实体。
- 新增 Ollama `/api/embed` 与 SiliconFlow `/v1/embeddings` adapter。
- 为语义处理增加可选 VLM 模型选择和内容块请求结构。
- 目录页面增加资源树、L0/L1/L2 标签和任务状态入口。
- 迁移现有 artifact 到 context node 时只建立映射，不复制 raw。

### Phase 2：异步语义处理

- 实现解析、文件摘要、目录 L1/L0 的 bottom-up worker。
- 任务中心展示阶段、重试、取消、日志和模型快照。
- 对图片和扫描页接入 VLM，保留页码和区域证据。

### Phase 3：分层检索与可观察性

- `tree`、`find`、`read`、`cite` 统一为服务端只读动作。
- Wiki、向量和混合检索共享 URI 结果结构。
- 查询面板展示检索轨迹、命中的层级、读取的 L2 和最终引用。

### Phase 4：兼容收口

- 旧 RAG API 映射到 vector/hybrid 模式。
- 旧知识页和索引版本继续可读。
- 删除旧页面依赖后再清理冗余字段和历史 provider 显示。

## 8. 本轮验收

- 设计稿和实现代码都不包含真实密钥、用户原文或运行时目录。
- Ollama 与 SiliconFlow 的连接测试能返回 provider、模型和维度；API Key 只从现有加密模型目录读取。
- 一份短文本能够生成 L0/L1 和知识页，且所有引用可在 L2 原文中逐字找到。
- 一份图片或扫描页在配置 VLM 后能生成带页码证据的 Markdown；未配置 VLM 时任务给出明确错误。
- 前端能看到 URI 树、层级标签和任务状态；旧 `/api/v1/knowledge` 路由保持兼容。

## 9. 参考来源

- [OpenViking Introduction](https://github.com/volcengine/OpenViking/blob/main/docs/en/getting-started/01-introduction.md)
- [OpenViking Context Layers](https://github.com/volcengine/OpenViking/blob/main/docs/en/concepts/03-context-layers.md)
- [OpenViking Viking URI](https://github.com/volcengine/OpenViking/blob/main/docs/en/concepts/04-viking-uri.md)
- [OpenViking Extraction and Semantic Queue](https://github.com/volcengine/OpenViking/blob/main/docs/en/concepts/06-extraction.md)
- [OpenViking Retrieval API](https://github.com/volcengine/OpenViking/blob/main/docs/en/api/06-retrieval.md)
- [SiliconFlow Embeddings API](https://api-docs.siliconflow.cn/docs/api/embeddings-post)
- [SiliconFlow Quickstart](https://docs.siliconflow.cn/docs/userguide/quickstart)

