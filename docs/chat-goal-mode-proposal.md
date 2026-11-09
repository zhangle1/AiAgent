# AiAgent 聊天目标模式改动清单

日期：2026-11-09。状态：设计建议，尚未实现。范围：AiAgent 网页聊天 `front/` 与服务端 `backed/` 的 Codex app-server 链路；KunBuddy 桌面端暂不包含。

## 1. 建议结论

可以增加目标模式。界面改动较集中，主要工作在 Codex thread 持久化、跨轮执行、停止控制和状态恢复，整体属于中等规模改造；若要求关闭浏览器后继续运行，则还需独立后台执行与事件恢复能力，规模明显增加。

建议首版定位：用户在现有聊天中设定目标，AI 在同一个 Codex thread 内持续推进，直到完成、用户暂停、预算耗尽或阻塞。首版仅开放给 Codex app-server，不把其他聊天引擎一起改造。

## 2. 当前代码与差距

以下为当前仓库代码核实结果，不能直接套用之前“已有目标接口即可完成”的判断。

| 位置 | 当前行为 | 目标模式需要补充 |
| --- | --- | --- |
| `backed/Services/Chat/Codex/CodexChatService.cs` → `CodexAppServerWorker.RunAsync` | 每次调用 `thread/start`，且设置 `ephemeral = true` | 目标会话创建持久 thread，后续恢复并复用 |
| 同文件 → `ReadTurnAsync`、`HandleNotificationAsync` | 收到当前轮 `turn/completed` 后结束读取 | 区分本轮完成与整个目标完成，处理轮间续跑 |
| 同文件 → `BuildPromptText` | 拼装当前消息、服务端引用与附件上下文 | 首轮初始化与后续增量输入分开，避免重复灌入历史参考内容 |
| 同文件 → runtime lease、pool、worker | 以浏览器 runtime 管理进程租约，worker 可回收；取消或失败会使 worker 不可用 | thread 绑定独立于浏览器 runtime，活动目标不能被当作空闲资源回收 |
| 同文件 → exec 分支 | 已有按会话和 profile 保存 CLI exec session 的逻辑 | 与 app-server thread 分开管理，不能误认为 app-server 已支持会话续接 |
| `backed/Services/Chat/ChatWebSocketHandler.cs` | 请求取消绑定 `RequestAborted`，监听客户端关闭；请求结束后关闭 socket | 明确断线策略，保存目标状态并支持重新查询 |
| `backed/Services/Chat/ChatOrchestrator.cs` | 按 agent 分派单次聊天执行 | 增加目标运行入口或委托独立目标服务 |
| `backed/Entities/Chat/AiChatSession.cs` | 会话保存项目与偏好，没有专用目标运行记录 | 持久保存目标、运行和 thread 绑定 |
| `front/components/chat/ChatStreamProvider.tsx` | 管理单次流，结束即 done；错误有定时重试；停止通过 abort | 目标生命周期独立于消息流；暂停走显式接口；重试前查询运行状态 |
| `front/components/chat/KnowledgeChatHome.tsx` | 当前聊天主界面 | 增加目标入口、配置、状态卡片和恢复操作 |

## 3. Codex 原生能力与前置验证

官方提供 `thread/goal/set/get/clear` 和 `thread/goal/updated` 通知，目标包含 objective、status、tokenBudget、tokensUsed、timeUsedSeconds。支持 `thread/resume` 和 `turn/interrupt`。目标文本非空且最多 4,000 字符；替换目标会重置用量，更新当前未终结目标可保留用量。[OpenAI 官方 app-server 文档](https://learn.chatgpt.com/docs/app-server)

这不等于已经证明当前部署的 CLI 会自动连续执行。编码前先做最小协议验证：

1. 记录服务端实际运行的 CLI 版本、执行路径、profile 和模型；检查其协议是否支持目标接口与所需状态。
2. 创建持久 thread，设置目标，运行一轮，观察目标通知、用量及轮结束后的行为。
3. 验证 app-server 是否自行开始下一轮；若支持则仅订阅，若不支持再由 AiAgent 控制下一轮，避免两套机制重复驱动。
4. 验证暂停、interrupt、resume、进程重建后的 thread 恢复；确认同一服务账号和 Codex 存储环境下可续接。
5. 核对原生状态枚举、预算计量和限制生效时机，以实际协议为准；不直接把产品状态字符串传入原生接口。

## 4. 前端改动

### 4.1 目标入口与目标卡片

- 在聊天输入区域增加“目标模式”，限定已选择可用 Codex app-server 的会话。
- 配置目标说明、验收标准和可选 Token 预算；用户未填写预算时不自行填入一个隐含预算。
- 输入框上方显示当前目标、执行状态、当前步骤、累计用量、耗时和停止原因。
- 提供暂停、继续、调整预算、结束目标；替换目标需明确提示其用量归属将改变。
- 目标期间的普通消息默认补充当前目标；替换目标通过明确操作完成。
- 首版运行中补充消息可排队到轮间处理；即时干预可后续接入 `turn/steer`。

### 4.2 类型与流状态

- `front/lib/chat-api.ts`：保持现有聊天协议兼容，补充与目标相关的事件识别。
- 建议新增 `front/lib/chat-goal-types.ts`、`chat-goal-api.ts`，集中维护目标状态与接口。
- `ChatStreamProvider.tsx`：消息流结束后依据服务端目标状态更新卡片，不能直接宣告目标完成。
- 进入会话、切换会话和刷新时查询目标快照；仅恢复展示，不因前端重新挂载而重复启动执行。
- 禁用目标运行的现有盲目定时重发，改为先查询 run，再决定恢复订阅或由用户重试。

## 5. 服务端改动

### 5.1 持久 thread 与协议读取

核心修改文件：`backed/Services/Chat/Codex/CodexChatService.cs`。

- 为目标会话使用持久 thread；绑定维度至少包括用户、网页 session、项目、profile/provider 和工作区身份。
- 绑定由服务端生成并校验，不接受前端任意指定他人的 threadId。
- 进程存活时复用已加载 thread；进程重建或迁移到新 worker 时按需 `thread/resume`。
- 项目、provider、工作区或权限切换时停止续跑并校验绑定；不可静默复用不匹配的上下文。
- 保存活动 turnId，暂停优先使用 `turn/interrupt`，等待 interrupted 或实际终态；强杀作为超时兜底。
- 由一个读取循环读取 stdout，按 requestId、threadId、turnId 分派响应和通知，避免暂停、查询与执行各自读取同一流造成串包。
- 将当前 `clientUserMessageId = request.SessionId` 改为消息级或提交级唯一标识，服务端另外保证启动请求幂等。

### 5.2 目标控制服务

建议新增 `ChatGoalService` 处理业务，`ChatGoalAppService` 只处理 HTTP 协议。Codex 协议适配继续放在 Codex 服务层。

- 创建目标：校验身份、会话、项目、引擎能力和目标内容，生成配置快照，绑定 thread 后设置原生目标。
- 持续执行：每轮开始前检查目标状态、预算、用户暂停和运行锁；轮结束后核对完成证据及是否允许继续。
- 完成判断：以验收标准、执行结果和测试等证据记录完成；普通 final 回复或单次 completed 不代表整个目标完成。
- 阻塞判断：记录具体缺失条件并停止空转，等待用户补充；规则应明确，不能每次失败都立即判为阻塞。
- 暂停与继续：先禁止新轮，再中断当前轮；继续时保留原目标和累计用量。
- 多标签页及并发请求：同一目标只允许一个执行者；操作带预期版本，迟到的事件不能覆盖暂停或新目标。
- 首版可采用进程内运行锁；若部署多个 API 实例，需要数据库条件更新或分布式租约。

### 5.3 建议 API

以下为 AiAgent 产品接口建议，不是 Codex 原生协议路径。

| 接口 | 用途 |
| --- | --- |
| `GET /api/v1/chat/sessions/{sessionId}/goal` | 获取目标、运行快照和可用操作 |
| `POST /api/v1/chat/sessions/{sessionId}/goal` | 创建目标，接受幂等键 |
| `POST /api/v1/chat/sessions/{sessionId}/goal/pause` | 禁止新轮并中断当前轮 |
| `POST /api/v1/chat/sessions/{sessionId}/goal/resume` | 校验状态后恢复 |
| `PATCH /api/v1/chat/sessions/{sessionId}/goal` | 调整预算或更新目标配置，带预期版本 |
| `POST /api/v1/chat/sessions/{sessionId}/goal/end` | 结束目标并保留历史 |

最终路由按项目 Dynamic API 命名方式落地。同步补充 `backed/Dtos/Chat/`、前端类型与 API 客户端；HTTP 和 WebSocket 入口复用同一个服务端校验过程。

## 6. 数据与事件

建议独立建表保存目标及执行记录，避免把持续变化的运行状态全部塞入 `PreferencesJson`。

| 记录 | 建议字段 |
| --- | --- |
| Goal | Id、UserId、SessionId、ProjectId、Objective、AcceptanceCriteria、Status、TokenBudget、TokensUsed、TimeUsedSeconds、StopReason、Version、创建/更新时间 |
| Run / thread 绑定 | RunId、GoalId、CodexThreadId、ActiveTurnId、Profile/Provider、WorkspaceIdentity、执行配置快照、执行者租约、最近事件时间、错误与恢复信息 |
| 事件或轮记录 | EventId/Sequence、GoalId、RunId、TurnId、Type、消息/产物引用、用量快照、时间 |

字段名为建议。数据库表纳入项目现有初始化机制。技能、模型、权限和引用范围按启动时快照记录，恢复时重新检查当前授权。

产品状态可设计为：待启动、执行中、暂停中、已暂停、阻塞、已完成、预算耗尽、失败、已结束；单独保存原生 goal status 并建立映射。传输连接状态与业务状态分开。

流事件建议增加 `goal_updated`、`goal_progress`、`goal_terminal`；共用 goalId、runId、sequence，支持客户端去重和快照对齐。流式文字继续复用已有 content/tool 事件，按 turn 分组持久保存；自动续跑不伪造成用户消息。

用量优先以原生目标累计值为依据，累计快照不能当作增量重复相加。平台用量统计仍按每轮真实调用记录，避免同时汇总 goal 和 turn 导致重复计费。服务端另设明确的运行时长与资源保护上限，和用户目标预算分别展示。

## 7. 首版与后台运行的边界

### 首版：在线持续执行，断线后暂停

- 浏览器在线时跨轮推进；关闭或连接丢失后停止发起新轮，中断当前轮并保存结果及暂停原因。
- 重连查询实际状态；只有确认没有活动执行者后才允许继续，不自动重发启动请求。
- 后端重启后标记运行待核对，查询原生 thread 状态并对账；不默认从头执行或直接宣布完成。
- 保留现有租约池，但活动目标需持续持有租约；暂停后可释放 worker，保留持久 thread 绑定。

### 后续：关闭浏览器后继续运行

- 新增独立后台 worker/队列，执行取消不再绑定 HTTP 或 WebSocket 请求生命周期。
- 浏览器只订阅事件；关闭浏览器只取消订阅，暂停必须调用独立控制接口。
- 增加持久事件序列、断点补发、执行者租约、重启恢复，以及活动目标的资源配额。
- 替换浏览器心跳对执行存活的控制关系，防止目标因页面关闭被回收，也防止失去控制后无限运行。

这两种方案需要明确选择。建议先交付首版，再根据长任务需求升级为后台运行。

## 8. 实施顺序与验收

1. **协议验证**：确认部署 CLI 的目标接口、自动续跑行为、预算及恢复语义；输出协议样例。
2. **执行基础**：持久 thread、统一读取循环、活动 turn 记录、interrupt 与恢复、幂等控制。
3. **业务与数据**：目标服务、记录表、状态转换、接口、配置快照和用量对账。
4. **聊天交互**：入口、目标卡片、暂停/继续、跨会话与刷新恢复、禁用重复重发。
5. **联调验收**：通过以下场景后再扩展后台执行。

- 同一目标多轮使用相同 thread，其他用户、项目或会话无法复用。
- 一轮回答结束且目标未完成时可继续；已完成、暂停、阻塞或预算耗尽后不再开启新轮。
- 连续点击启动、双标签页操作、网络重试只产生一个执行者。
- 暂停发生在工具执行中或轮间时，都不会被迟到事件重新激活。
- 刷新、断线、worker 回收、进程退出后状态可解释，恢复不重复执行上一轮写操作。
- 自动续跑仍执行原有技能显式启用、附件归属、项目授权和沙箱策略。
- 用量累计、预算调整和目标替换符合实际协议；失败或中断的部分结果仍可查看。
- 普通聊天、Codex exec 和其他引擎保持现有行为。

建议补充后端假 app-server 协议测试，重点覆盖多轮、通知乱序、暂停竞态和恢复；前端扩展 `chat-stream-lifecycle`、`chat-auto-retry` 场景并增加目标交互验证。本文仅为方案文档，本次未修改实现或运行功能测试。
