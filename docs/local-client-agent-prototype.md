# 本地 Client Agent 原型设计

## 1. 目的与边界

这个原型用于理解「像 Codex CLI 或 Pi 一样工作的本地 Agent」：用户在桌面聊天界面提出目标，Agent 规划步骤、调用受控本地工具、持续展示过程，并把结果沉淀到会话中。

一期把它当成**独立 Client 项目**，不接入当前 AiAgent 后端，也不复用其旧的聊天、权限、知识库或服务器工作区逻辑。

### 一期包含

- 桌面端聊天窗口与会话列表。
- 对接多个模型提供商的统一适配层。
- 本地工作区内的文件读取、搜索、修改及命令执行。
- 工具调用过程、确认提示、流式输出、会话恢复。
- 一个可观察、可中断、可审计的 Agent loop。

### 一期不包含

- 用户登录、团队协作、云端同步和服务端任务队列。
- RAG、企业知识库、远程代码库托管。
- 多 Agent 编排、自动发布、无人值守的高权限操作。
- 试图兼容任意 CLI 的私有协议；CLI 只作为可选 Provider/Runtime，而非内核依赖。

## 2. 体验原型

布局借鉴 Codex 桌面端的工作流，而不是照搬其实现：

```text
┌────────────┬──────────────────────────────────────────┬───────────────┐
│ 会话与项目 │                 对话主区                 │ 运行详情      │
│            │  用户问题 / Agent 回复 / 流式状态        │ 计划、工具、  │
│ + 新对话   │                                          │ diff、终端    │
│ ● 重构...  │  [输入框] 模型  工作区  权限  发送       │               │
└────────────┴──────────────────────────────────────────┴───────────────┘
```

- 左栏：本地保存的项目与会话；项目绑定一个明确的本地工作区根目录。
- 中栏：自然语言对话。回答中的计划、工具卡片和 diff 都是消息的一部分，用户不必切换页面理解 Agent 正在做什么。
- 右栏：当前任务的实时详情，可收起。显示计划状态、工具输入/输出摘要、文件变更和终端日志。
- 输入区：始终可见模型、工作区与权限模式。高风险工具不能被隐藏在模型回复里。

首个可用流程：选择工作区 → 选择模型 → 提问「解释这个项目并修复一个小问题」→ Agent 先给计划 → 读取/搜索文件 → 遇到写入或执行命令时请求确认 → 展示 diff 与最终总结。

## 3. 核心架构

建议采用 Electron + React + TypeScript 做桌面壳；也可用 Tauri 替换 Electron，以下边界不变。渲染进程只负责 UI，任何文件、终端和密钥访问都由受限的本地主进程完成。

```text
React 聊天 UI
      │ IPC（白名单命令与结构化事件）
Desktop Main Process
      ├── Session Store（SQLite / 本地文件）
      ├── Agent Runtime
      │     ├── Model Gateway
      │     ├── Context Builder
      │     ├── Tool Registry
      │     └── Approval Gate
      └── Workspace Sandbox
            ├── File tools
            ├── Search tools
            └── Process tool
```

关键原则：模型只提出 `tool_call`；Runtime 验证参数、检查权限、等待用户确认，再执行工具。模型不拥有桌面进程或操作系统权限。

## 4. Agent loop：最小但完整的学习闭环

```text
用户消息
  → 构建上下文（会话摘要、当前工作区、工具描述、权限）
  → 调用模型并流式显示
  → 若模型返回工具调用：校验 → 必要时确认 → 执行 → 记录结果
  → 将工具结果回送模型
  → 直到模型产出最终回复、用户取消或达到预算
```

每个任务具有 `taskId` 和以下状态：`idle → planning → running → waiting_approval → completed | failed | cancelled`。状态机是原型的重点：它让 UI、取消操作、恢复会话和日志都有明确归属，而不是把 Agent 写成不可控的 while 循环。

应设置三类预算：最大回合数、最大工具调用数、最大上下文/费用。超过预算时暂停并向用户说明，而不是静默继续。

## 5. 模型适配层

前端和 Agent Runtime 只面对统一接口，Provider 差异留在适配器内。

```ts
interface ModelAdapter {
  id: string;
  stream(request: AgentModelRequest, signal: AbortSignal): AsyncIterable<ModelEvent>;
}

type ModelEvent =
  | { type: 'text_delta'; text: string }
  | { type: 'tool_call'; id: string; name: string; arguments: unknown }
  | { type: 'usage'; inputTokens?: number; outputTokens?: number }
  | { type: 'done' }
  | { type: 'error'; message: string };
```

第一批适配器可按价值依次实现：OpenAI Responses API、Anthropic Messages API、兼容 OpenAI 的自定义网关/本地模型。每个适配器只负责协议转换；工具循环、确认和文件安全必须保持在 Runtime 中统一实现。

API Key 只存入操作系统凭据库（Keychain/Credential Manager/libsecret），会话数据库只保存 Provider 名称、模型 ID 与密钥引用，不保存明文密钥。

## 6. 工具与权限模型

工具从只读开始，逐步开放能力：

| 工具 | 一期权限 | 执行边界 |
| --- | --- | --- |
| `list_files` / `read_file` / `search_text` | 默认允许 | 仅工作区根目录及其子目录 |
| `write_file` / `apply_patch` | 每次确认 | 原子写入；显示 diff；拒绝越界路径 |
| `run_command` | 每次确认 | 指定 cwd 在工作区内；不经 shell 拼接参数；限制时长与输出 |
| `git_diff` / `git_status` | 默认允许 | 仅读取当前工作区 Git 状态 |

权限模式可设为：`read-only`、`ask-before-write`、`ask-before-all-tools`。不要在原型早期提供“永久完全访问”；这会模糊 Agent 能力与操作系统权限之间的边界。

所有工具调用都记录为结构化事件：工具名、脱敏参数摘要、开始/结束时间、退出状态、产生的文件 diff 与用户确认结果。终端完整输出只保存在本地，并支持按会话清除。

## 7. 本地数据模型

最小持久化对象：

```text
Project(id, name, workspaceRoot, createdAt)
Conversation(id, projectId, title, modelId, createdAt, updatedAt)
Message(id, conversationId, role, blocks, createdAt)
Task(id, conversationId, status, budget, startedAt, endedAt)
ToolRun(id, taskId, callId, toolName, inputSummary, resultSummary, approval)
```

`Message.blocks` 使用 JSON 数组保存文本、思考/计划摘要、工具卡片、diff 引用和错误块。UI 根据 block 类型渲染，避免把运行过程塞进一长段 Markdown。

## 8. 迭代路径与验收

1. **聊天壳**：本地会话、模型选择、模拟流式回复；验证窗口布局和消息数据模型。
2. **单模型与只读工具**：接入一个真实模型，完成读取、搜索、工具事件展示与取消。
3. **确认与写入**：加入审批状态、`apply_patch`、diff 审阅、工作区路径校验。
4. **命令与恢复**：受限 `run_command`、任务日志、异常恢复、预算控制。
5. **多模型适配**：再接入第二个 Provider，验证统一事件协议没有泄漏供应商细节。

完成一期的判定标准：用户能在一个本地工作区内完成一次可中断的“理解代码并修改单个文件”任务；全过程可看见模型输出、工具调用、确认决定与最终 diff；模型替换不需要修改聊天 UI 或工具实现。

## 9. 接下来最值得亲手实现的三个点

1. 先实现 `AgentRuntime.run()`，即使模型和工具都是 mock，也要把状态机、事件流和取消做通。
2. 再实现 `read_file` 与 `search_text`，专门练习工作区路径校验与结构化工具结果。
3. 最后接一个真实模型，把其流式输出转换为统一 `ModelEvent`；此时最容易看清“模型 API”和“Agent Runtime”是两层不同的东西。

这样做出的不是现有系统的简化版，而是一个可以逐层拆解、验证和替换的 Client Agent 实验台。
