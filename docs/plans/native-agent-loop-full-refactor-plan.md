# 自有 Agent Loop 完全重构规划

> 状态：规划稿，尚未实施  
> 编制日期：2026-09-07  
> 目标仓库：AiAgent 后端（`.NET 9`）  
> 参考项目：`APS 张乐 AI` 中的 Codex Runtime 实现  
> 参考快照：`12ed76b09bf311d8d9a6f25be2f9a8eb37daf879`

## 1. 结论

本次重构不继续扩展现有 `FINISH / TOOL / THINK` 文本标签协议，而是在现有 `Services/AgentRuntime` Phase 0 边界内，重建一个以 **Thread、Turn、Step、Item、ToolCall、Event** 为核心对象的原生 Agent Runtime。

目标链路如下：

```text
Chat HTTP / WebSocket / SSE
        │
        ▼
RunCoordinator ── Run ledger / cancellation / status
        │
        ▼
NativeAgentRuntime
        │
        ▼
TurnRunner
  ├─ capture immutable StepContext
  ├─ build model request with native tool schemas
  ├─ consume structured response items
  ├─ dispatch tool calls through ToolRouter
  ├─ append tool outputs to Thread history
  ├─ compact / retry / steer when required
  └─ finish on assistant terminal output
        │
        ▼
RuntimeEvent stream ── compatibility projector ── current frontend events
```

重构完成后，`AgentLoop`、`LabeledStepRunner` 和文本标签修复提示将退出生产主路径；`NativeAgentRuntime` 不再是 legacy adapter，而是真正拥有模型—工具迭代的执行引擎。

## 2. 范围与边界

### 2.1 本次包含

- 自有模型 Agent Loop 的运行时领域模型、状态机与执行流程。
- 原生 function/tool calling 请求与流式响应解析。
- 工具注册、可见性计划、路由、执行、并发、超时、取消和审批边界。
- Thread 历史、Turn/Step 快照、上下文压缩与用户中途追加输入（steer）的演进接口。
- 统一、可恢复、可追踪的 Runtime Event 协议与运行账本。
- 现有知识库、代码库、看板工具迁移。
- 兼容现有聊天 API、WebSocket/SSE 和前端事件消费。
- 单元、集成、协议、故障恢复与安全测试。

### 2.2 本次不包含

- 重写 Codex、DeepSeek Harness 等第三方 Runtime；它们继续作为独立 `IRuntimeEngine`。
- 第一阶段直接实现完整多 Agent 编排、分布式队列或跨节点热迁移。
- 让模型或浏览器提供真实文件路径、命令行参数、权限策略或密钥。
- 为迁就旧实现永久保留文本标签协议。
- 在 Runtime 核心中耦合具体前端展示文案。

## 3. 现状基线与主要问题

当前生产链路的关键实现位于：

- `Services/Chat/Agentic/AgentLoop.cs`
- `Services/Chat/Agentic/LabeledStep.cs`
- `Services/Chat/Agentic/ToolProtocol.cs`
- `Services/Chat/Agentic/ToolDispatch.cs`
- `Services/Chat/Agentic/AgentContext.cs`
- `Services/AgentRuntime/NativeAgentRuntime.cs`
- `Services/AgentRuntime/RunCoordinator.cs`
- `Services/AgentRuntime/RuntimeContracts.cs`

Phase 0 已经提供了正确的外层方向：`IRuntimeEngine`、`RunCoordinator`、运行状态、追加式运行事件、取消入口和持久化账本。但内部仍由 legacy loop 执行，存在以下结构性问题：

1. **协议脆弱**：工具调用依赖模型输出首行标签和 JSON 文本，必须用 repair prompt 修复格式；内部推理或协议文本可能泄漏到答案。
2. **状态过粗**：`AgentContext` 同时承担请求 DTO、运行状态、能力配置和临时字典，缺少 Thread/Turn/Step 生命周期边界。
3. **历史不可精确回放**：模型消息、工具调用、工具输出没有统一的强类型 Item 序列，恢复与诊断依赖聚合结果。
4. **工具系统耦合**：定义、权限判断、执行、结果聚合和引用处理边界不清；无法可靠表达 call id、逐调用状态和结构化错误。
5. **并发策略缺失**：多个工具调用缺少按工具能力声明的并发/串行门控。
6. **暂停语义未闭环**：状态枚举已有 `WaitingApproval`、`WaitingInput`，但 Coordinator 状态机不能恢复到 Running，Runtime 也没有 durable continuation。
7. **事件语义不完整**：当前事件主要由 legacy 文本流投影而来，Item 和 ToolCall 生命周期不是事实源。
8. **预算与容错分散**：固定 5/8 轮、token 估算、模型重试、工具超时、总时限和上下文压缩尚未形成统一策略。
9. **能力快照不充分**：能力选择有快照雏形，但工具可见性、权限、模型能力和工作区状态没有在每个 Step 固化。
10. **测试难以覆盖状态组合**：Loop 是大方法，模型流、解析、工具与终止条件难以分别替换和验证。

## 4. 从参考项目吸收的设计

参考项目不是逐行移植对象；本项目采用其运行时结构，并按 C#、Furion、SqlSugar 和现有业务能力重新实现。

| 参考设计 | 本项目采用方式 | 不直接照搬的内容 |
| --- | --- | --- |
| `run_turn` 持续采样，直到模型不再要求 follow-up | `TurnRunner` 驱动 Step 循环，由结构化响应决定继续或完成 | Rust async 类型和具体 Codex provider 实现 |
| 每次采样前捕获 `StepContext` | 固化模型、工具计划、权限、工作区 revision、上下文预算 | 参考项目全部环境/插件功能 |
| 模型看到的工具规格与实际可执行 Runtime 来自同一 `ToolRouter` | 由 `ToolPlanBuilder` 一次生成并校验，避免“声明了但不可执行” | 初期不做动态插件推荐 |
| 原生 response item：message、function call、function output | 建立强类型 `RuntimeItem`，废弃文本 `TOOL` JSON | 不绑定单一供应商 wire format |
| 工具按能力声明并发；非并行工具使用独占门 | `ToolConcurrencyPolicy` + turn-scoped gate | 初期不做跨进程并发调度 |
| 审批、沙箱选择、执行与重试集中编排 | `ToolExecutionPipeline` 统一前置策略与生命周期 | 不复制本机 shell 沙箱实现；沿用本项目受控 Service |
| append-only conversation / rollout | Thread Item 与 Run Event 分开持久化，支持回放和审计 | 不保存敏感 prompt、真实路径或凭据 |
| turn-scoped model client session 与流重试 | Provider session 在 Turn 内复用，重试策略集中管理 | 不保证所有供应商支持 WebSocket sticky session |
| cancellation token 贯穿模型与工具 | Coordinator → Turn → Step → ToolCall 全链路取消 | 不把取消误判为普通工具失败 |
| compaction、pending input、hooks 是独立阶段 | 预留 `IContextCompactor`、`ITurnInputQueue`、生命周期拦截器 | 第一交付阶段只实现必要最小集 |

## 5. 目标领域模型

### 5.1 标识与层级

```text
Thread
└─ Turn (一次用户目标，可被暂停/继续)
   ├─ Step 1 (一次模型采样及其产生的 items)
   │  ├─ AssistantMessageItem
   │  ├─ ToolCallItem
   │  └─ ToolResultItem
   ├─ Step 2
   └─ FinalAssistantItem

Run
└─ 某个 Runtime 对某个 Turn 的一次执行记录
```

- `ThreadId`：长期会话历史边界。
- `TurnId`：用户一次目标的语义边界。
- `RunId`：执行、重试、取消与审计边界。
- `StepId`：一次模型采样边界。
- `ItemId`：消息、工具调用、工具结果等可流式更新对象。
- `CallId`：模型 tool call 与 tool result 的严格关联键。

现有请求将 `ThreadId` 与 `RunId` 传入 Runtime，但缺少独立 `TurnId`，需在协议 v2 中补齐。

### 5.2 不可变快照与可变状态

`TurnContext` 在 Turn 开始时生成，保存：

- 用户、Thread、Turn、Run 标识。
- 模型配置快照与 provider 能力。
- 审批和权限策略快照。
- 知识库、代码库、看板等能力选择。
- 附件的不透明 ID 和服务端校验后的安全描述。
- 总时限、Step、ToolCall、token、输出大小等预算。

`StepContext` 在每次模型调用前重新捕获，保存：

- 本次输入历史版本和上下文预算。
- 本次模型可见的工具规格及其可执行路由快照。
- 工作区 revision、知识索引 revision 等外部世界状态。
- 本次采样的模型参数、输出 schema 和 provider session。

`TurnState` 只保存执行中可变数据：当前 Step、累计用量、最后 assistant message、待处理输入、终止原因和取消状态。禁止继续使用开放式 `Dictionary<string, object?>` 作为核心控制状态。

### 5.3 统一 Item 模型

建议首版类型：

- `UserMessageItem`
- `AssistantMessageItem`
- `ReasoningSummaryItem`（仅保存允许展示/审计的摘要，不保存隐式思维链）
- `ToolCallItem`
- `ToolResultItem`
- `ApprovalRequestItem`
- `SystemContextItem`
- `CompactionItem`
- `ErrorItem`

所有 Item 具备 `ItemId`、`TurnId`、`StepId`、序号、状态、时间戳和安全元数据。工具输入/输出采用结构化 JSON 或受控内容块；日志层只记录脱敏摘要。

## 6. 目标组件与目录

建议在 `backed/Services/AgentRuntime` 下逐步形成以下结构：

```text
AgentRuntime/
├─ Contracts/
│  ├─ RuntimeContracts.cs
│  ├─ RuntimeItems.cs
│  ├─ RuntimeEvents.cs
│  └─ RuntimeErrors.cs
├─ Coordination/
│  ├─ RunCoordinator.cs
│  ├─ RunStateMachine.cs
│  └─ RunLeaseService.cs
├─ Execution/
│  ├─ NativeAgentRuntime.cs
│  ├─ TurnRunner.cs
│  ├─ StepRunner.cs
│  ├─ TurnContext.cs
│  ├─ StepContext.cs
│  ├─ TurnBudget.cs
│  └─ TerminationPolicy.cs
├─ Models/
│  ├─ IAgentModelClient.cs
│  ├─ ModelRequest.cs
│  ├─ ModelResponseItem.cs
│  ├─ ModelStreamAssembler.cs
│  └─ ProviderAdapters/
├─ Tools/
│  ├─ IAgentTool.cs
│  ├─ ToolRegistry.cs
│  ├─ ToolPlanBuilder.cs
│  ├─ ToolRouter.cs
│  ├─ ToolExecutionPipeline.cs
│  ├─ ToolConcurrencyPolicy.cs
│  ├─ ToolApprovalService.cs
│  └─ Builtins/
├─ History/
│  ├─ IThreadStore.cs
│  ├─ ThreadHistory.cs
│  ├─ ContextWindowBuilder.cs
│  └─ ContextCompactor.cs
├─ Events/
│  ├─ RuntimeEventSink.cs
│  ├─ RuntimeEventProjector.cs
│  └─ LegacyChatEventAdapter.cs
└─ Persistence/
   ├─ AgentRunStore.cs
   ├─ AgentThreadStore.cs
   └─ RedactionPolicy.cs
```

HTTP 协议仍由现有 AppService/WebSocket handler 承载；路径校验、文件写入、检索和外部进程继续位于各领域 Service 或工具实现中。

## 7. 核心执行流程

### 7.1 Turn 主循环

```text
1. Coordinator 创建 Run，并获取 owner-scoped cancellation lease
2. Runtime 构造不可变 TurnContext
3. 校验并追加本轮 UserMessageItem
4. 需要时进行 pre-sampling compaction
5. 捕获 StepContext（模型 + 工具 + 权限 + 外部状态快照）
6. 从 Thread history 构造 provider-neutral ModelRequest
7. Model adapter 转换并流式调用供应商
8. ModelStreamAssembler 产出结构化 items
9. 逐个追加 items、发事件并更新 usage
10. 若存在 tool calls：
    a. ToolRouter 校验 call id、名称和参数
    b. ToolExecutionPipeline 执行审批/超时/取消/领域调用
    c. 按工具能力并行或串行
    d. 逐项追加 ToolResultItem
    e. 回到步骤 4/5，继续采样
11. 若收到 terminal assistant output 且无待执行 tool call：完成 Turn
12. 持久化结果与终态，发 TurnCompleted
```

终止不再依赖 `FINISH` 字样，而由结构化响应状态决定。若供应商不支持原生工具调用，由 provider adapter 内部实现兼容解析；兼容细节不得泄漏到 Runtime 核心。

### 7.2 工具批次规则

- 每个调用必须有唯一 `CallId`，结果必须引用同一 `CallId`。
- 一个模型响应可包含 message 和多个 tool calls；只有所有必需工具结果写回后才进入下一 Step。
- 工具显式声明 `SupportsParallelExecution`、`IsReadOnly`、默认超时、输出上限和审批类别。
- 只有全部调用都允许并行时才并发；任何独占工具进入写锁，防止看板修改、Git 或会话状态工具互相踩踏。
- 单个非致命工具失败转换成结构化 `ToolResultItem(status=failed)` 供模型处理；协议破坏、权限绕过、账本写入失败属于 fatal error。
- 工具输出在进入模型上下文前执行大小限制、敏感信息清理和内容类型归一化。

### 7.3 审批与暂停

审批是 Runtime 状态，不是工具返回字符串：

```text
Running → WaitingApproval → Running
Running → WaitingInput    → Running
```

需补齐：

- `RunStateMachine` 合法恢复路径。
- `ApprovalId` / `RequestId` 与 owner 校验。
- 审批结果持久化和幂等提交。
- 等待期间释放执行资源，但保留可恢复 continuation。
- 第一阶段若暂不实现 durable continuation，则明确限制为单实例、进程内等待，不能假装已支持跨实例恢复。

## 8. 模型适配层

新接口 `IAgentModelClient` 只暴露 provider-neutral 能力：

- `StreamAsync(ModelRequest, ModelSession, CancellationToken)`
- 输入：强类型历史 Item、原生工具 schema、输出 schema、模型参数和安全元数据。
- 输出：`TextDelta`、`ReasoningSummaryDelta`、`ToolCallStarted`、`ToolArgumentDelta`、`ToolCallCompleted`、`Usage`、`Completed`、`ProviderError`。

供应商适配器负责：

- OpenAI-compatible `tools` / `tool_calls` 映射。
- 流式 arguments 拼装与 JSON 完整性校验。
- provider finish reason 归一化。
- usage、rate limit 和 retry-after 提取。
- 可重试网络错误与不可重试协议错误分类。
- Turn 内复用连接/session（供应商支持时）。

原 `LlmChatClient` 可先通过 adapter 接入，但 Runtime 不再依赖 `LabeledStepResult`。

## 9. 工具系统迁移

### 9.1 工具契约

每个 `IAgentTool` 至少提供：

- 稳定名称、版本、说明和 JSON Schema。
- 能力/租户/资源可见性判断。
- 参数反序列化与语义校验。
- 并发、只读、超时、审批和副作用声明。
- `ExecuteAsync(ToolExecutionContext, JsonElement, CancellationToken)`。
- 结构化结果、用户可见摘要、模型可见内容、引用和安全元数据。

### 9.2 首批迁移顺序

1. 纯读知识工具：RAG、页范围读取、代码检索。
2. 看板读取与校验工具。
3. 看板补丁等受 revision 保护的写工具。
4. Git、外部进程及后续需要审批/沙箱的工具。

旧 `IToolDispatcher` 在过渡期由 adapter 包装，但新工具不得继续向旧聚合模型扩展。

## 10. 事件协议 v2

`RuntimeEvent` 必须成为事实源，不再从旧聊天字符串反推。建议增加：

- `ThreadStarted` / `TurnStarted`
- `StepStarted` / `StepCompleted`
- `ItemStarted` / `ItemDelta` / `ItemCompleted`
- `ToolCallStarted` / `ToolCallCompleted`
- `ApprovalRequested` / `ApprovalResolved`
- `UsageUpdated`
- `ContextCompacted`
- `TurnCompleted` / `TurnFailed` / `TurnCancelled`
- `RunStatusChanged`

协议要求：

- 每个 Run 的 `Sequence` 严格单调递增。
- `EventId` 全局唯一，事件追加幂等。
- delta 必须引用稳定 `ItemId`。
- 完成事件带终止原因、模型快照、usage 和脱敏统计。
- 持久化成功后再向传输层发布，避免 UI 看见无法恢复的“幽灵事件”。
- SSE/WS 断线重连可按 `after_sequence` 补发。

过渡期由 `LegacyChatEventAdapter` 把 v2 事件投影为当前 `content/thinking/tool/tool_result/done`；前端迁移完成后删除 adapter。

## 11. 持久化与安全

### 11.1 数据职责分离

- Run ledger：状态、版本、用量、错误分类、时间和事件序号。
- Thread store：允许回放给模型的结构化 Items。
- 业务数据：知识库、看板、代码库仍由对应领域持有，Runtime 只保存不透明引用和快照标识。

如新增实体字段，必须显式 `[SugarColumn(IsNullable = true)]`，并先完成兼容迁移。

### 11.2 禁止持久化/输出

- 密钥、Token、数据库连接串。
- 浏览器提供的本地路径或未经验证的服务器真实路径。
- 隐式思维链；只允许经过策略筛选的 reasoning summary。
- 未脱敏的工具输入、输出、命令环境和附件正文。
- 可从客户端篡改的权限、sandbox mode 或 capability snapshot。

图片和文件继续使用服务端签发的不透明附件 ID；真实签名、大小、数量、owner 和允许根目录校验必须发生在构造 Runtime Item 之前。

## 12. 预算、重试与错误分类

用 `TurnBudget` 取代固定 5/8 轮：

- 最大 Step 数。
- 最大模型调用数和 ToolCall 数。
- 输入/输出 token 预算。
- 单工具超时与 Turn 总时限。
- 单 Item、单工具输出和总上下文大小。
- 最大 compaction 次数和模型流重试次数。

标准终止原因：

- `completed`
- `cancelled`
- `budget_exceeded`
- `context_window_exceeded`
- `model_protocol_error`
- `provider_unavailable`
- `tool_fatal_error`
- `approval_rejected`
- `expired`

重试必须由错误分类驱动：网络断开、429/部分 5xx 可按 provider policy 重试；参数错误、权限拒绝和协议错误不盲目重试。已经产生副作用的工具不得自动重复执行，必须依赖幂等键或明确的执行记录。

## 13. 分阶段实施

### Phase A：冻结契约与测试基线

- 记录现有 native chat 的请求、事件、引用、附件、取消和错误行为。
- 为当前主路径建立 golden/contract tests。
- 定义 Runtime Protocol v2、Item schema、错误码与版本策略。
- 补充 feature flags：`AgentRuntime:NativeV2Enabled` 与按用户/会话灰度。

验收：关闭开关时行为零变化；协议文档和测试夹具可独立评审。

### Phase B：Thread / Turn / Step 与事件事实源

- 引入强类型标识、Item、TurnContext、StepContext、TurnBudget。
- 拆出 `RunStateMachine`，补齐暂停/恢复状态迁移。
- 建立事件 sink，保证先落账再发布、sequence 单调和重放。
- 保持 legacy loop 执行，仅用 shadow projector 验证 v2 事件。

验收：同一 Run 可从账本按序重建状态；敏感数据检查通过。

### Phase C：原生模型响应管线

- 建立 `IAgentModelClient` 和首个 OpenAI-compatible adapter。
- 支持 message、tool call、tool result、usage 的流式组装。
- 用 mock provider 覆盖参数分片、乱序/残缺 JSON、断流、重试和取消。

验收：不使用标签协议即可完成“回答—工具—回答”两步循环。

### Phase D：Tool Registry / Router / Execution Pipeline

- 一次生成模型可见 schema 与 executable route。
- 迁移只读工具，建立 call id、超时、取消、输出限制和结构化失败。
- 加入并发读锁/独占写锁策略与工具生命周期事件。

验收：未知工具、参数错误、并发混合、部分失败和取消均有确定结果。

### Phase E：写工具、审批与 revision 安全

- 迁移看板写工具及其他副作用工具。
- 将审批做成 Runtime pause/resume，而非提示文本。
- 强制 owner、工作区根目录、revision、幂等键和原子写入校验。

验收：审批拒绝不产生写入；重复回调不重复执行；revision 冲突明确失败。

### Phase F：历史、压缩、steer 与恢复

- 建立 Thread Item store 和 `ContextWindowBuilder`。
- 实现 token-aware compaction，保留工具调用—结果配对与引用。
- 支持运行中用户追加输入，并定义何时并入下一 Step。
- 评估 durable lease/queue，实现跨实例取消和恢复；未实现前保持能力声明诚实。

验收：长会话不会因固定拼接无限膨胀；重启后的可恢复范围有自动测试。

### Phase G：灰度切换与 legacy 删除

- shadow run 比较答案完成率、工具成功率、首 token、总耗时和错误率。
- 按用户/租户/会话逐步开启 V2，支持快速回退。
- 前端切换到事件协议 v2。
- 删除 `AgentLoop`、`LabeledStepRunner`、标签 prompt/parser、legacy projector 和无用 DTO。
- 更新 `README.md`、`AGENTS.md` 和运行手册。

验收：V2 默认开启并稳定运行一个观察周期；仓库中无生产路径引用文本标签协议。

## 14. 测试矩阵

| 类别 | 必测场景 |
| --- | --- |
| 纯回答 | 单次采样完成、空输出、Markdown、usage |
| 工具循环 | 单工具、多工具、多 Step、未知工具、参数错误、部分失败 |
| 并发 | 全只读并行、读写混合串行、独占工具互斥、取消等待中的调用 |
| 流式 | text delta、arguments delta、断流、重复片段、完成事件缺失 |
| 状态机 | 正常完成、失败、取消、过期、等待审批、恢复、非法迁移 |
| 持久化 | sequence 单调、幂等追加、事件重放、账本失败时不发布 |
| 安全 | owner 越权、路径穿越、符号链接逃逸、附件伪造、敏感信息脱敏 |
| 副作用 | revision 冲突、原子替换、幂等重试、审批拒绝零写入 |
| 上下文 | token 超限、压缩、工具配对保留、引用保留、长会话 |
| 兼容 | SSE/WS 同义、旧前端 adapter、feature flag 回退、第三方 Runtime 不受影响 |

测试分层：

1. 无网络纯单元测试：状态机、assembler、router、预算与 redaction。
2. fake model + fake tools 的 TurnRunner 集成测试。
3. SQLite/测试库上的 store 与重放测试。
4. 现有 Chat API/WebSocket/SSE 端到端协议测试。
5. 少量真实 provider 冒烟测试，不把凭据写入仓库或测试输出。

## 15. 可观测性与上线指标

按 Run/Turn/Step/ToolCall 记录脱敏指标：

- 完成率、取消率、各类终止原因占比。
- 首 token 延迟、Step 耗时、Turn 总耗时。
- 模型调用数、ToolCall 数、重试数、compaction 次数。
- token usage、模型和工具错误率。
- 审批等待时长、工具排队与执行时长。
- 事件持久化和推送延迟、重放次数。
- legacy 与 V2 shadow 结果差异。

日志必须携带 `RunId / TurnId / StepId / ItemId / CallId`，但不得携带 prompt、真实路径、凭据或完整工具结果。

## 16. 迁移与回滚策略

- 保留现有 `IRuntimeEngine` 外层选择，V2 作为新的 native engine 实现注册。
- 不以原地大改方式一次删除 legacy；先双写事件/影子运行，再灰度切流。
- 数据表只做向前兼容新增，旧字段在稳定期结束后再清理。
- V2 事件带 `ProtocolVersion`，旧前端走 adapter。
- 回滚只切换 feature flag，不回滚已经写入的新表结构。
- 写工具 shadow 模式只做计划和校验，禁止双执行副作用。

## 17. 完成定义（Definition of Done）

全部满足才视为“完全重构完成”：

- 生产 native 路径不再调用 `IAgentLoop` 或解析 `FINISH / TOOL / THINK`。
- 模型工具调用和结果均为带 `CallId` 的强类型 Item。
- 工具声明、模型可见 schema 与可执行路由来自同一快照。
- 取消、超时、审批、错误与预算具有确定状态和终止原因。
- Run Event 可持久化、重放且顺序稳定；SSE/WS 共享同一事实源。
- Thread 历史可安全恢复，长上下文具备可验证的压缩策略。
- 所有副作用工具具备 owner、路径/revision、审批和幂等保护。
- 核心测试矩阵通过，第三方 Runtime 回归通过。
- V2 默认开启且可通过配置快速回退。
- legacy loop、标签 parser/prompt 和兼容代码在观察期后删除。
- `README.md`、`AGENTS.md`、架构文档和运维说明同步更新。

## 18. 建议的首个开发变更集

首个变更集只做“可评审的骨架”，不要同时迁移业务工具：

1. 新增 Protocol v2 的 `TurnId`、强类型 Item、Event 和 Error contracts。
2. 抽出并测试 `RunStateMachine`，修正 `WaitingApproval/WaitingInput → Running`。
3. 新增 `TurnContext`、`StepContext`、`TurnBudget` 和 `ITurnRunner` 接口。
4. 建立 fake model/fake tool 测试夹具。
5. 增加 V2 feature flag，默认关闭。
6. 让 legacy loop 的现有事件以 shadow 方式映射到 v2 账本，验证顺序与脱敏。

该变更集完成后，再进入原生模型 tool calling；这样可以先稳定协议和生命周期，避免模型适配、工具迁移、数据库迁移和前端改造在同一个提交中互相耦合。

