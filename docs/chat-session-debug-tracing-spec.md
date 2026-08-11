# 聊天会话级 Debug 开关与耗时链路诊断

## 目的

为单个浏览器标签页中的当前聊天会话提供按需、结构化的端到端耗时诊断。用户能在聊天页确认慢在浏览器、后端准备、模型请求、首个有效流式事件、持久化还是推送阶段，同时不记录或展示消息、提示词、附件、凭据及敏感绝对路径。

## 开关语义与存储范围

- 默认关闭；关闭时不创建详细阶段事件，不增加常规聊天日志的详细内容。
- 开关只保存在浏览器 `sessionStorage`，键包含会话 ID；仅当前标签页有效，关闭标签页即失效，不同步到服务端、不写入会话偏好或数据库。
- 开启后，每个聊天请求携带 `debug_trace: true` 和浏览器生成的 `trace_id`。服务端仅在该请求生命周期内保有诊断对象，并通过原有 WebSocket/SSE 流返回。
- 服务端仅信任布尔开关来决定是否发回诊断事件；`trace_id` 会进行格式规范化，不能作为权限或查询键使用。
- 不存在全局、账户级或永久 Debug 开关。

## Trace Schema

流事件类型为 `debug_trace`，字段为：

| 字段 | 说明 |
| --- | --- |
| `trace_id` | 浏览器产生的随机请求关联 ID，不包含用户标识 |
| `stage` | 固定阶段名（如下表） |
| `status` | `started`、`completed`、`failed` 或 `cancelled` |
| `elapsed_ms` | 从该请求的后端接收到当前事件的单调时钟耗时 |
| `duration_ms` | 已完成阶段自身耗时；进行中的阶段为空 |
| `provider` | `codex`、`openai_compatible` 或 `unknown` |
| `transport` | `codex_app_server`、`http_stream`、`websocket`、`sse` 或 `unknown` |
| `error_code` | 仅分类代码，如 `cancelled`、`request_failed`，不含异常原文 |

阶段名固定：`browser_submit`、`backend_received`、`auth_session_context`、`request_started`、`provider_request_started`、`first_stream_event`、`provider_completed`、`persistence`、`frontend_push`、`request_completed`。浏览器在发送前生成 `browser_submit`；其余由服务端发出。事件不携带正文、标题、文件名、提示词、请求头、Token、API Key、Authorization、真实路径或异常堆栈。

## 阶段矩阵

| 统一阶段 | DeepSeek / OpenAI 兼容 | 原生 Codex |
| --- | --- | --- |
| `auth_session_context` | 鉴权、会话/上下文构建 | 鉴权、会话/上下文构建 |
| `request_started` | 编排进入模型调用 | 编排进入本地代理调用 |
| `provider_request_started` | HTTP 流请求即将发出 | app-server 启动/请求已写入 |
| `first_stream_event` | 首个非空流式增量 | 首个有效 app-server 流事件 |
| `provider_completed` | HTTP 流完成或失败 | app-server run 完成或失败 |
| `persistence` | 聊天消息写库 | 聊天消息写库 |
| `frontend_push` | WebSocket/SSE 推送完成 | WebSocket/SSE 推送完成 |

事件始终保留 `provider` 与 `transport`，使同一时间线可区分提供商路径。取消产生 `cancelled` 终态；异常产生脱敏的 `failed` 终态。

## UI

- 聊天页右上角提供“Debug”切换按钮，默认关闭；激活态明显可见。
- 开启时，在当前会话的消息区域底部显示“诊断时间线”，按时间展示阶段、状态、相对耗时、提供商/传输；每条助手消息的操作区提供诊断图标，点击后在弹窗查看该会话的短期持久化记录。
- 时间线仅显示在当前页面内存状态中；发送下一请求前可保留，切换会话时按对应 sessionStorage 开关读取。关闭开关立即隐藏当前页面的诊断数据，后续请求不再产生诊断事件。
- UI 不展示错误原文，仅展示安全分类；不渲染任意后端传来的未定义字段。

## 接口与流事件

- `ChatCompleteRequest` 增加 `debug_trace?: boolean`、`trace_id?: string`，HTTP 完成、WebSocket、SSE 共享同一 DTO。
- WebSocket 与 SSE 复用 `AgentStreamEvent`，增加 `debug_trace` 事件及固定 schema 载荷；普通事件协议保持兼容。
- `GET /api/v1/sessions/{sessionId}/diagnostics` 返回当前登录用户在该会话下未过期的持久化记录；每次至多返回最近 30 条。
- 后端在接收请求后最早发送 `backend_received`，并在完成、取消、异常路径发送终态；写库与发送完成事件单独计时。

## 脱敏与留存

- 开启 Debug 的请求完成后，将固定 schema 的事件集合写入独立的 `ai_chat_debug_trace` 表；记录以用户和会话双重归属隔离，不写入聊天正文、会话偏好、应用日志或统计日志。
- 默认保留 7 天（可通过 `ChatDebugTrace:RetentionDays` 在 1–30 天内调整）；写入和读取时均清理过期记录。诊断接口仅返回当前登录用户所属会话的未过期记录。
- 仅允许固定枚举字段、非负整数耗时、随机关联 ID 与安全错误码；所有自由文本、文件/目录路径和凭据字段均不进入 schema。
- 前端仅在用户点击助手消息下方的诊断图标时请求并在弹窗中渲染记录；不会预加载或显示任意未定义字段。

## 性能开销

- 关闭时只做一个布尔分支，不创建 Stopwatch、事件列表或额外日志。
- 开启时采用单调 `Stopwatch` 和少量小型事件，沿既有流推送；终态额外进行一次小型脱敏记录写入，不增加模型请求。
- 时间线事件数量受固定阶段集合限制，避免高频 token 级事件。

## 错误与取消

- 浏览器主动取消、WebSocket 关闭和请求取消令牌触发时，若连接尚可写，发送 `cancelled` 终态；不可写时仅在内存结束。
- 认证、上下文、提供商、写库或推送失败均发送对应阶段 `failed`，且仅使用安全错误分类。
- 诊断逻辑绝不能掩盖或改变原有聊天异常、取消和连接关闭行为。

## 验收案例

1. 新打开聊天页 Debug 默认关闭；普通聊天不出现 `debug_trace` 事件或时间线。
2. 当前会话开启后，WebSocket 聊天可看到浏览器提交、后端接收、上下文、请求、首流、完成、写库和推送阶段；刷新同一标签会话开关仍在，关闭标签页后失效。
3. DeepSeek/OpenAI 兼容路径事件标记 `provider=openai_compatible`、`transport=http_stream`；Codex 路径标记 `provider=codex`、`transport=codex_app_server`。
4. 取消和模拟提供商错误均得到安全 `cancelled` / `failed` 终态，页面不显示消息正文、异常原文或路径。
5. SSE 回退可解析相同事件；聊天完成后点击助手消息下方的诊断图标可查看同会话的短期持久化记录，其他用户和过期记录不可访问。

## 非目标

- 不做跨设备、跨标签页同步，不做全局永久开关。
- 不做日志检索、数据库追踪查询、性能分析平台或 Token 计数诊断。
- 不改变模型提示词、模型选择、鉴权模型、附件访问控制和现有会话持久化策略。
