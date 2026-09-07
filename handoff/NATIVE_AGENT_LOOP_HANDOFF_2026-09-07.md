# AiAgent 原生 Agent Loop 交接（2026-09-07）

## 交接结论

AiAgent 的原生运行时已从“仅把旧 `AgentLoop` 包在 Runtime 接口外”的 Phase B，推进到具备 OpenAI-compatible 原生 function calling 的 **Phase C 灰度实现**。

当前生产默认行为没有改变：仅当下列两个配置都为 `true` 时，普通聊天才进入 V2；否则仍走旧的 `FINISH / TOOL / THINK` 标签循环。

```json
{
  "AgentRuntime": {
    "NativeEnabled": true,
    "NativeV2Enabled": true,
    "NativeV2ToolTimeoutSeconds": 90
  }
}
```

先在确认支持 OpenAI-compatible `tools` / `tool_calls` 的模型配置上灰度验证，再扩大范围。不要把 `NativeV2Enabled` 直接作为所有模型的默认值。

## 本次提交范围

### 运行时与审计

- `RuntimeTurnRequest` 新增 `TurnId`，旧调用未传时回退到 `RunId`。
- 新增 `RuntimeTurnContext`、`RuntimeStepContext`、`RuntimeExecutionCheckpoint` 和运行状态机；Step 记录模型、工具快照哈希、上下文规模和工作区 revision 是否存在等脱敏事实。
- `RunCoordinator` 既持久化状态变更，也持久化运行时事件；V2 会发出 Turn、Step、Provider、Tool、Usage、ContextCompacted、Completed/Failed/Cancelled 事件。
- `AgentRunStore` 只允许白名单元数据进入账本，避免 prompt、附件正文、工具原始结果和密钥进入运行记录。

### 原生模型与工具循环

- `ILlmChatClient` / `LlmChatClient` 已支持 OpenAI-compatible `tools` 请求、流式 `delta.tool_calls` 解析，以及 assistant tool call 与 `role=tool` result 回传。
- `NativeTurnRunner` 以结构化 tool call 驱动模型—工具迭代，不解析 `FINISH / TOOL / THINK` 文本协议。
- `NativeToolRouter` 使用同一份 `NativeToolPlan` 同时约束模型可见 schema 与执行许可；不在快照中的工具不会进入旧分发器。
- 只读检索/索引工具可并行；文件、看板、校验、写入和未知工具严格串行。
- 工具有独立超时（默认 90 秒）；用户取消会原样取消，工具自身超时返回结构化失败结果。
- 对幂等只读工具，HTTP/超时类瞬态异常会用新超时窗口重试一次；写操作不自动重试。
- 同一 Turn 同时防 provider `CallId` 重复和“工具名 + 排序参数”语义重复，避免模型换 ID 原地重复检索或重复写入。

### 上下文、引用和模型协议保护

- 会话历史只在 owner 校验成功后读取 `ai_chat_message` 的 user/assistant 文本；不读取 thought、附件路径、metadata，也会排除刚写入的当前用户消息。
- 按模型 `context_window` 为用户输入、项目/文档/记忆引用、历史和工具循环分配预算；工具输出截断后再放入下一轮。
- 压缩只移除完整 assistant-tool-call/tool-result 配对；请求发给模型之前再做一次非破坏性消息规范化，确保不会发送孤立 `role=tool` 或残缺工具调用组。
- token 账本按每一次 provider 请求累计上下文和 tools schema，并将 function arguments 计入估算 completion；每个工具轮后都会发 `UsageUpdated`。
- 最终回答的 citations 按文档 chunk 或代码文件/行号去重；每个工具事件仍保留其原始引用，方便追溯。

## 主要文件

| 位置 | 作用 |
| --- | --- |
| `backed/Services/AgentRuntime/NativeAgentRuntime.cs` | V2 灰度入口；默认保留 legacy bridge。 |
| `backed/Services/AgentRuntime/NativeTurnRunner.cs` | 原生模型—工具迭代、预算、事件、引用和防重。 |
| `backed/Services/AgentRuntime/NativeToolRouter.cs` | 工具快照授权、超时、只读重试。 |
| `backed/Services/AgentRuntime/NativeThreadHistoryStore.cs` | owner 校验后的安全会话历史。 |
| `backed/Services/AgentRuntime/RuntimeExecutionContexts.cs` | Turn/Step/checkpoint 状态事实。 |
| `backed/Services/Chat/Llm/LlmChatClient.cs` | OpenAI-compatible tools wire protocol 与流式解析。 |
| `backed.tests/AgentRuntime/NativeTurnRunnerTests.cs` | 工具、超时、只读重试、写操作不重试、语义防重、引用去重覆盖。 |
| `backed.tests/Chat/LlmChatClientTests.cs` | tool-call SSE 解析与完整工具消息组规范化覆盖。 |
| `docs/plans/native-agent-loop-full-refactor-plan.md` | 全量计划、阶段记录和未完成边界。 |

## 服务器继续开发的建议顺序

1. 在独立测试模型上打开两个 Native flag，验证流式 tool call 格式与当前 provider 的真实兼容性；观察 `ai_agent_run` / `ai_agent_run_event` 的 Step、Tool、Usage 记录。
2. 给模型目录/管理 UI 增加明确的“支持原生工具调用”能力字段和按模型灰度，而不是仅用全局 `NativeV2Enabled`。
3. 引入真正的持久化 checkpoint：当前 `RuntimeExecutionCheckpoint` 是进程内状态，账本可审计但**不能宣称进程重启后恢复执行**。恢复实现必须先持久化工具 claim，且在恢复时不重放任何已 claim 的副作用操作。
4. 将 `NativeToolRouter` 的 legacy `IToolDispatcher` adapter 按领域工具逐步替换为独立原生工具实现，同时保留同一安全快照和事件协议。
5. 等 V2 覆盖常用模型、工具、取消和异常测试后，再让 legacy 标签协议退出普通聊天主路径。

## 验证状态

- 已执行：`git diff --check`，无空白错误。
- 已新增单元测试源码，但本地未运行编译、测试或打包；本工作区有旧项目/中文内容，需要按现有编码保护约束在服务器 CI 或明确指定的构建环境验证。
- 本次提交不得包含以下非 Agent Loop 改动：`front/next-env.d.ts`、`knowledge-lab-mvp/` 删除、`knowledge-base-learning-lab/` 未跟踪目录。

## 参考来源

- 本地参考仓库：`E:\项目\apsai\guokun-aps2.0-api`
- 核心参考：`AgentTurnPipeline.cs`、`AgentExecutionCheckpoint.cs`、`AgentSessionStateStore.cs`、`OpenAiCompatibleModelGateway.cs`
- AiAgent 全量设计：`docs/plans/native-agent-loop-full-refactor-plan.md`
