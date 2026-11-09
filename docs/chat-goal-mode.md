# 聊天目标模式首版

## 使用

选择项目及 Codex app-server 模型，在输入区上方勾选「目标模式」，填写目标与验收标准，并发送本轮消息。目标最多 4000 字符，预算留空表示没有用户设定的 Token 上限，不偷偷填入默认预算。

卡片显示原生状态、累计 Token、预算及耗时。暂停使用控制接口；现有停止按钮和断线也会暂停当前执行。继续时勾选目标模式，发送“继续”或补充要求，恢复原 thread 与累计用量。模型、推理强度、权限或项目切换后拒绝复用不匹配绑定，需恢复原配置或结束旧目标。

不勾选时仍是普通聊天。已有目标不会因进入页面、刷新或普通消息而自动执行。exec Profile 和其他引擎不支持目标请求。

## 执行边界

- `CodexChatService.Goals.cs` 是独立分支，只有 `request.Goal != null` 才进入。普通聊天保留 `ephemeral: true`、单轮读取及原有重试规则。
- 目标使用 `ephemeral: false`，在服务器保存 thread 绑定；恢复失败不新建替代 thread。浏览器不能提交 thread ID。
- 原生 `thread/goal/set/get/updated` 提供状态与累计用量。**续跑只由 Codex 原生调度**，AiAgent 读取跨轮事件，不发起第二套续跑循环。目标 `complete/blocked/paused/budgetLimited/usageLimited` 与本轮 `completed` 分开判断；目标终态先于最后输出时继续读取活动轮。
- 暂停先禁用当前请求的后续处理，尝试 `turn/interrupt`，再保存原生 paused；各最多等待 3 秒，无响应时杀掉本 worker 的进程树。目标 worker 结束后丢弃，不把旧目标订阅放回普通聊天。
- 断线或异常中断时，已收到的文本保存为带 `goal_id`、`goal_status` 和 `interrupted` 的 assistant 历史消息。创建目标的请求快照随用户消息元数据保留。没有完整输出时不会宣称完成。
- 目标请求的通用自动重试、通用手动重试均关闭。用户通过最新状态版本显式继续。start 使用预期版本（首次为 0），版本单调递增，重复／迟到启动不会重新执行已完成目标。
- 平台聊天用量继续使用原有估算记录；目标卡片使用原生累计数，两者不相加。中断目标的用量统计以卡片原生快照为准，平台统计尚未逐自动轮对账。

当前仅支持**单 API 实例、浏览器在线执行**。进程内会话锁与目标取消表阻止并发执行；多 API 实例需要另加分布式租约。后端重启后，遗留 active 状态在查询时转为 paused，只有显式继续才恢复。保留现有执行总时限和无事件时限；原生没有发起续跑时超时暂停，不自行制造模型轮次。

## 存储及部署

新增 `AiChatSession.GoalJson`，显式 `[SugarColumn(IsNullable = true)]`。首版使用会话独立 JSON 列存当前目标、原生 thread 及配置绑定，避免改写偏好 JSON。没有独立目标／运行／事件表；完整历史目标的查询、后台事件补发和预算调整界面留待后续。

`ModelSchemaInitializer.EnsureChatSessionColumns` 在聊天兼容阶段显式添加 `NVARCHAR(MAX) NULL`，原表、补列、索引与种子顺序保持不变。同步部署前后端；若部署禁用 CodeFirst，须先由部署流程执行这项可空补列。此次开发没有连接或迁移真实数据库。

可设置 `Codex:GoalModeEnabled=false` 拒绝新的目标执行，普通聊天不受此开关控制。现有目标仍可查看和结束。CLI 不支持原生目标接口时明确失败，不降级为普通聊天。

## 接口

| 入口 | 契约 |
| --- | --- |
| 原聊天 HTTP / WebSocket 请求 | 可选 `goal: { action: "start", objective, token_budget?, expected_version: 0 }` |
| 原聊天 HTTP / WebSocket 请求 | 继续使用 `goal: { action: "resume", expected_version }`；不重设 objective／预算以保留累计用量 |
| `GET /api/v1/chat/sessions/{sessionId}/goal` | `{ goal: null }` 或脱敏卡片快照，不返回 thread／工作区绑定 |
| `POST .../goal/pause` | `{ expected_version }`，取消当前执行，轮询等待实际暂停 |
| `POST .../goal/end` | `{ expected_version }`，执行中需先暂停；保留现有消息历史 |

所有控制接口按当前登录用户限定会话，状态版本冲突返回 409。目标快照使用 camelCase；请求中的预算与版本使用 snake_case。流新增 `goal_updated`，普通文本和工具事件继续使用现有协议。

Furion 路由注册须为根 GET 使用显式空模板 `[HttpGet("")]`，并把父路径中的 sessionId 显式标为 `[FromRoute]`，避免自动追加同名参数导致整个端点注册失败。控制路径参数使用 `{goalAction}`，避开 MVC 保留名 `action`；浏览器仍调用上述 `/goal/pause` 和 `/goal/end` 地址。Swagger 回归同时用合成身份和目标服务通过真实 HTTP 验证 GET、暂停、结束及 expected_version 绑定。

原生协议参考：[OpenAI app-server 文档](https://learn.chatgpt.com/docs/app-server)。已用本机 CLI 0.164.0 导出 schema，并在隔离 `CODEX_HOME` / 工作区运行真实 `initialize → thread/start → goal/set(paused) → goal/get → goal/clear`，返回 paused 和指定预算；没有调用真实模型。

## 验证

- 后端：临时 SQLite 与模拟 app-server，验证普通临时 thread、原生跨轮、终态早于最后输出、暂停与部分结果、同 thread 恢复、预算／阻塞终态、越权访问、配置绑定、版本与重复启动，以及旧 Codex 诊断、图片 Profile、技能、启动索引回归。
- 前端：TypeScript 检查；流生命周期与自动重试测试新增目标请求不重发；Playwright 在独立组件中验证手机宽度、目标勾选、必填目标、预算省略／校验、会话切换、版本化继续与结束。
- 本机默认 dotnet 为 SDK 6；验证使用 `C:/Users/Administrator/.dotnet/dotnet.exe` 的 SDK 10。缺少 x64 .NET 9 运行时，测试以 `DOTNET_ROLL_FORWARD=Major` 在 .NET 10.0.13 运行。
- 浏览器测试工具独立安装在仓库外，执行时设置 `GOAL_TEST_TOOLS` 指向含 playwright/esbuild 的目录，再运行 `node front/tests/chat-goal.browser.mjs`。

尚未执行真实模型的长任务端到端验收，也未操作现网数据库。部署后的首次验收应检查实际服务账号的 CLI 原生续跑行为、暂停恢复及最终产物。
