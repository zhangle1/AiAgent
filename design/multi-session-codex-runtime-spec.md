# AiAgent 多会话与 Codex 运行时规格

> 状态：当前实现基线  
> 范围：聊天多会话流式显示、完成一致性、Codex CLI 常驻租约、并发限制与页面状态提示。

## 1. 目标

1. 用户切换会话时，不中断其他会话正在进行的流式请求。
2. 每条消息仍由原有 WebSocket 优先、SSE 降级的聊天协议传输；不在浏览器直接连接 Codex。
3. Codex 请求必须真实启动和使用 `codex app-server --stdio`，不以“短问题”绕过 Codex。
4. 避免每条消息重复初始化 CLI、MCP 和 app-server，降低后续请求首字节等待时间。
5. 同一用户最多保留并运行 3 个 Codex 会话，避免一个账号无限创建常驻进程。

## 2. 核心模型

```text
认证用户 ID + 浏览器页签运行时 ID
        │
        ▼
CodexRuntimeLease（最多一个 CLI）
        │  心跳每 25 秒续租
        ▼
codex app-server --stdio
        │
        ├── initialize（仅首次）
        ├── thread/start（每条消息）
        └── turn/start → turn/completed（每条消息）

会话页 ──创建流──► ChatStreamProvider（应用根）──► WS / SSE ──► 后端聊天编排
  ▲                         │                                   │
  └────按 session_id 派生───┘                                   └──落盘后发送 completed
```

### 2.1 浏览器页签运行时 ID

- 前端将随机不透明 ID 写入 `sessionStorage`，字段名为 `client_runtime_id`。
- 它代表浏览器当前页签运行时，不读取设备硬件、Canvas、UA 或其他隐私指纹。
- 服务端只将此 ID 与已认证用户 ID 组合为租约键；浏览器不能指定用户 ID。
- 关闭页签或浏览器后心跳停止，租约会自然过期。

### 2.2 Codex 租约

- 同一 `用户 + 页签运行时 ID` 只有一个 `codex app-server` 进程。
- 页面选中 Codex 和代码项目时立即调用心跳接口预热；之后根层 Provider 每 25 秒续租一次。
- 默认无心跳 90 秒且 CLI 空闲后回收；配置项 `Codex:RuntimeLeaseSeconds` 限制为 30–600 秒。
- 同一 CLI 的 stdout 只允许一个 turn 消费，因此同一页签的 Codex turn 串行执行，不交错读取 JSONL。
- 请求失败、取消或 app-server 已退出时，当前 worker 丢弃；下一次心跳或请求会创建新的 CLI。
- 保持当前执行模式：`approvalPolicy=never`、`dangerFullAccess` 与 MCP 配置不变。

## 3. 多会话并发与限制

| 规则 | 前端 | 后端 |
| --- | --- | --- |
| 同一会话 | 同一时刻仅允许一条流 | 同一 `session_id` 已运行时拒绝重复 Codex 请求 |
| 同一用户 | 最多 3 条运行中的会话流 | 最多 3 个 Codex 运行时租约，最多 3 个活动 Codex 会话 |
| 不同会话切换 | 只切换渲染目标，不 abort 其他流 | 不依赖浏览器当前页面，会继续处理已建立的请求 |
| 超出限制 | 显示“最多 3 个会话”错误 | 返回限制错误，不能由前端绕过 |

`Codex:MaxSessionsPerUser` 默认值和上限均为 `3`。用户确需并行执行时，应在不同页签中运行；每个页签拥有各自的 CLI 租约，但仍受用户总上限约束。

## 4. 前端流状态

应用根的 `ChatStreamProvider` 是本次浏览器生命周期内唯一的流状态拥有者：

```text
stream_id -> {
  session_id,
  status: streaming | done | stopped | error,
  events[],
  started_at,
  unread
}
```

- 会话页面不持有 WebSocket；只按当前 `session_id` 从 Provider 派生需要显示的流消息。
- 切换会话、侧栏刷新或会话组件卸载不会中断其他流。
- 仅点击停止时才 abort 对应流；Provider 卸载（整个应用关闭）时才取消全部流。
- 侧栏状态：运行中显示小型旋转圆环；后台完成但未查看显示提示点；错误显示错误提示点。
- 浏览器整页刷新会丢失内存中的进行中流；已完成结果仍由历史接口恢复。

## 5. 事件与完成一致性

| 事件 | 含义 | 前端动作 |
| --- | --- | --- |
| `content` / `thinking` / `tool` 等 | Codex 或 Agent 的中间增量 | 更新对应流消息 |
| `done` | Agent 已产生最终答复 | 保持流状态，等待持久化完成 |
| `completed` | 助手消息和使用量已落盘 | 结束流、重新读取当前会话历史、清理该会话终态内存流 |
| `error` | 流或 Agent 失败 | 显示失败状态和提示点 |

`done` 不是页面最终完成条件。后端完成会话及用量落盘后才发送 `completed`。这避免“前端已显示完成但刷新后没有记录”的问题。

完成后，当前会话页重新读取历史数据，并移除对应的已结束内存流。页面只保留落盘后的单条助手消息，避免同时出现 `Done` 与“Codex 已完成（未修改文件）”两张重复卡片。执行轨迹仅在流式运行中保留；完成历史默认折叠。

为兼容仍运行旧后端的浏览器，收到 `done` 后若 800ms 内未收到 `completed`，前端按旧协议结束该流；新协议始终以 `completed` 为准。

## 6. 后端接口与职责

| 路径/组件 | 责任 |
| --- | --- |
| `POST /api/v1/chat/codex/heartbeat` | 验证当前用户，续租并在给定项目下预热 CLI |
| `/api/v1/chat/ws` | 接收一条聊天请求，流式发送事件，落盘后发送 `completed` |
| `POST /api/v1/chat/complete/stream` | WebSocket 不可用时的 SSE 降级，终态语义相同 |
| `ChatStreamProvider` | 跨会话保存浏览器内存流、心跳和并发前置限制 |
| `CodexChatService` | 用户/页签租约、CLI 生命周期、单 stdout 串行和服务端会话上限 |
| `ChatWebSocketHandler` / `ChatAppService` | 鉴权、会话落盘、用量记录和 `completed` 通知 |

浏览器提交的 `client_runtime_id` 只作为运行时索引，不能传入 sandbox、执行路径、审批策略或用户身份。项目目录与 Codex 执行模式仍由服务端决定。

## 7. 配置

```json
"Codex": {
  "Command": "codex",
  "RuntimeLeaseSeconds": 90,
  "MaxSessionsPerUser": 3
}
```

- `Command`：后端运行账户可执行的 Codex CLI 命令或绝对路径。
- `RuntimeLeaseSeconds`：无心跳后保留空闲 CLI 的秒数。
- `MaxSessionsPerUser`：用户级保护阈值，服务端最大接受值为 3。

## 8. 可观测性与验收

建议日志至少包含：用户 ID 的脱敏标识、运行时 ID 的短哈希、租约创建/预热/回收、worker 重建、活动会话数、请求排队时间、CLI 初始化耗时、`done` 到 `completed` 的落盘耗时及终态错误。

验收场景：

1. 选择 Codex 与项目后，后台预热一个 CLI；首条消息仍走 Codex，后续同页签消息不重复 `initialize`。
2. 在两个会话中分别发送请求并切换页面，两个流都继续；返回任一会话可看到其当前事件。
3. 开启第 4 个 Codex 会话时，前端与后端均拒绝。
4. 收到 `done` 后不立即结束；收到 `completed` 后页面只保留一条已落盘消息。
5. 关闭页签或停止心跳，超过租约时间后空闲 CLI 被回收；下一次使用自动重建。
