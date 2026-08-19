# AiAgent Local Agent Bridge 设计方案

## 1. 目标

让开发者在自己的电脑安装 AiAgent Local Agent，登录平台账号并授权一个或多个本地工作区；AiAgent 服务器继续负责运行 Codex CLI、模型调用、会话编排和额度消耗，本地 Agent 只负责在明确授权的工作区内执行文件、Git、终端和测试操作。

核心体验：

1. 开发者安装并登录 Local Agent。
2. Local Agent 通过浏览器完成设备绑定。
3. 开发者在本机选择允许访问的项目目录，并配置权限。
4. 平台创建 Codex 会话，服务器将受控工具请求转发到该设备。
5. 本地 Agent 校验路径和权限后执行，流式返回结果。
6. 开发者可随时暂停会话、撤销工作区或断开设备。

## 2. 非目标与产品边界

- 不把开发者电脑暴露为公网 SSH 主机。
- 不让服务器直接挂载或扫描开发者整台电脑。
- 不把服务器的 Codex Token、模型密钥或平台服务凭据下发到本机。
- 不允许 Codex 文本自行扩大工作区、权限或审批范围。
- 不在第一阶段提供任意端口转发、桌面控制、管理员提权和跨工作区命令。
- Local Agent 不是另一个 Codex CLI；它是经过认证、受策略约束的远程工具执行器。

## 3. 总体架构

```text
┌──────────────────────── AiAgent Server ────────────────────────┐
│ Web UI ─ API/Auth ─ Session Orchestrator ─ Codex CLI/App Server│
│                                │ tool call                     │
│                         Bridge Gateway                         │
│                                │ WSS/mTLS                      │
└────────────────────────────────┼───────────────────────────────┘
                                 │ 本地主动建立出站连接
┌──────────────────── Developer Workstation ─────────────────────┐
│ Local Agent                                                    │
│  ├─ Device identity / secure credential store                  │
│  ├─ Policy & approval engine                                   │
│  ├─ Workspace path jail                                        │
│  └─ File / search / patch / git / process adapters             │
│                 │                                               │
│          Authorized workspace only                              │
└─────────────────────────────────────────────────────────────────┘
```

服务器侧 Codex 不应假装本地路径存在。应给 Codex 会话注册一组桥接工具，例如 `workspace.read_file`、`workspace.search`、`workspace.apply_patch`、`workspace.run_process`。Bridge Gateway 把结构化调用投递给对应设备；Local Agent 返回结构化结果。这样可以复用现有 Agent loop，同时把路径与执行边界留在开发者电脑上。

## 4. 信任与安全模型

### 4.1 三层授权

| 层级 | 授权对象 | 有效期 | 撤销方式 |
| --- | --- | --- | --- |
| 设备绑定 | 某台 Local Agent 与平台账号 | 长期，凭据轮换 | 设备页解绑、本地退出 |
| 工作区授权 | 规范化后的本地根目录 | 长期或指定期限 | 本地或平台撤销 |
| 会话授权 | 某次 Codex 会话的工具能力 | 会话级 | 暂停/结束会话 |

任一层失效，后续调用都必须拒绝。平台只保存工作区不透明 ID、展示名和能力摘要；真实绝对路径默认仅保存在本机。

### 4.2 路径边界

- 工作区授权时解析符号链接并保存规范化根路径与稳定目录标识。
- 每次文件操作都重新规范化目标路径，验证其仍位于授权根目录中。
- 拒绝 `..` 逃逸、符号链接逃逸、Windows junction/reparse point 逃逸、UNC 路径切换和大小写混淆。
- 文件写入采用临时文件加原子替换；补丁应用前校验文件版本或内容哈希。
- 默认拒绝访问 `.env*`、私钥、系统凭据目录等敏感项，并允许项目级追加忽略规则。

### 4.3 命令边界

命令必须是结构化请求：可执行文件、参数数组、工作目录、超时、环境变量白名单分别传输，不拼接 shell 字符串。初始策略：

- `read-only`：读取、搜索、Git status/diff/log。
- `workspace-write`：增加补丁和工作区文件写入；测试/构建按策略审批。
- `full-access`：仍只限授权工作区；危险命令、联网、提权和工作区外访问必须单次审批。名称表示能力档位，不代表绕过本机安全边界。

高风险操作包括删除、覆盖大量文件、Git push、包发布、联网脚本、安装系统依赖、修改 Git 历史。Local Agent 必须展示实际命令和影响范围，由本机用户确认，不能只依赖网页确认。

### 4.4 身份与传输

- 使用设备码/OAuth PKCE 完成绑定，避免把账号密码交给 Local Agent。
- 首次绑定生成设备密钥，私钥进入 Windows Credential Manager、macOS Keychain 或 Linux Secret Service。
- Local Agent 主动建立 `wss://` 连接；生产环境建议叠加设备证书或请求签名、短期 access token 与刷新凭据轮换。
- 每条任务包含不可重放的 `requestId`、递增序列、期限、会话 ID、工作区 ID 和策略快照哈希。
- 服务端与本地端都记录审计事件，但对命令输出和文件内容做敏感信息截断与保留期控制。

## 5. 核心协议

### 5.1 能力协商

Local Agent 连接后上报版本、操作系统和支持能力，不上报整个文件系统：

```json
{
  "type": "agent.hello",
  "protocolVersion": "1.0",
  "deviceId": "dev_7f2a",
  "capabilities": ["fs.read", "fs.patch", "search.rg", "git", "process"],
  "agentVersion": "0.1.0"
}
```

### 5.2 工具调用

```json
{
  "type": "tool.request",
  "requestId": "req_01J...",
  "sessionId": "ses_01J...",
  "workspaceId": "ws_01J...",
  "tool": "process.run",
  "deadline": "2026-08-19T08:05:00Z",
  "policyHash": "sha256:...",
  "input": {
    "executable": "npm",
    "args": ["test"],
    "cwd": ".",
    "timeoutMs": 120000
  }
}
```

响应通过 `tool.accepted`、多个 `tool.output` 和最终 `tool.completed` / `tool.failed` 流式返回。断线重连时按 `requestId` 查询状态，不能盲目重执行有副作用的操作。

## 6. 服务端模块建议

```text
backed/Services/LocalAgent/
├─ DeviceAppService.cs          # 绑定、查询、解绑
├─ WorkspaceGrantAppService.cs  # 授权元数据与撤销
├─ BridgeGateway.cs             # WSS 连接与消息路由
├─ ToolDispatchService.cs       # Codex tool call -> bridge request
├─ SessionPolicyService.cs      # 会话策略快照
└─ AuditService.cs              # 安全审计
```

建议实体：

- `LocalAgentDevice`：用户、设备公钥、名称、平台、版本、状态、最后在线时间、撤销时间。
- `WorkspaceGrant`：设备、展示名、不透明本地引用、能力集、忽略策略、有效期、撤销时间。
- `RemoteExecutionSession`：聊天会话、设备、工作区、权限档位、状态、策略快照。
- `RemoteToolRequest`：请求 ID、工具、风险级别、审批状态、结果摘要、时间戳。

所有新增字段遵循现有 SqlSugar 可空字段和 DTO JSON 命名约定。Bridge Gateway 与业务服务解耦，Controller/Dynamic API 只负责 HTTP 协议。

## 7. Local Agent 进程建议

首版可使用 .NET Worker，便于与现有后端共享 DTO 和签名逻辑，并打包为 Windows 单文件程序；后续补齐 macOS/Linux。进程拆分：

- `ConnectionWorker`：登录、WSS、心跳、重连和版本升级提示。
- `WorkspaceRegistry`：由原生目录选择器创建授权，不接受服务器传入真实根路径。
- `PolicyEngine`：本机最终裁决，服务端策略只能收紧、不能放宽。
- `ExecutionBroker`：限并发、超时、取消、输出限流与子进程树回收。
- `ApprovalUI`：托盘窗口/本地 WebView，显示风险、命令和影响范围。
- `Adapters`：文件、ripgrep、Git 和进程操作的结构化实现。

Local Agent 应默认以前台托盘应用运行。若以后提供系统服务模式，也要把高风险审批交给当前登录用户会话，不能静默执行。

## 8. 页面与交互

### 8.1 设备页

展示在线状态、系统、Agent 版本、最近心跳和授权工作区。支持复制安装命令、设备码绑定、重命名、暂停和解绑。

### 8.2 工作区授权向导

目录必须通过本机选择器选择。开发者配置展示名、权限档位、命令策略、敏感文件忽略和授权期限。提交前展示“平台能做什么/不能做什么”。

### 8.3 会话连接器

新建聊天时选择“服务器工作区”或“本地设备”，再选择在线设备与已授权工作区。会话顶部持续显示执行位置、权限档位和紧急断开入口。

### 8.4 执行中心

每个调用展示来源会话、工具、目标文件/命令、风险级别、审批状态、耗时和结果。支持取消当前任务，但不能把“取消超时”误报为进程已终止。

## 9. API 草案

```text
POST   /api/local-agent/device-code
POST   /api/local-agent/device-code/confirm
GET    /api/local-agent/devices
DELETE /api/local-agent/devices/{deviceId}
GET    /api/local-agent/devices/{deviceId}/workspaces
POST   /api/local-agent/workspaces/{workspaceId}/revoke
POST   /api/local-agent/sessions
POST   /api/local-agent/sessions/{sessionId}/pause
GET    /api/local-agent/sessions/{sessionId}/requests
WS     /api/local-agent/bridge
```

目录选择与真实路径注册只能通过本机 IPC/UI 完成，不提供“网页传绝对路径”接口。

## 10. 异常场景

- 设备离线：会话进入等待状态，不自动换到另一台设备。
- Agent 版本不兼容：拒绝新任务，允许查看并引导升级。
- 会话执行中断线：只恢复幂等读取；写入/进程请求先查询本地执行账本。
- 文件被开发者同时修改：哈希冲突，返回最新摘要并要求 Codex 重新读取。
- 本地审批无人响应：到期拒绝；不能在网页端替代本机高风险确认。
- 输出过大：分块限流，服务端保留截断摘要，原始日志按本地策略处理。
- Agent 被退出或设备解绑：立刻取消排队请求并撤销后续令牌。

## 11. 分阶段实施

### Phase 0：协议验证

- 单设备、单工作区、内存队列。
- 只读工具：列目录、读文件、搜索、Git status/diff。
- 验证 Codex 工具适配与断线行为。

### Phase 1：可用 MVP

- 设备码绑定、WSS 网关、持久化设备与工作区授权。
- 补丁写入、结构化测试命令、会话暂停和审计。
- Windows Local Agent 安装包与托盘审批 UI。

### Phase 2：团队可用

- 多设备、组织策略、Agent 自动升级、macOS/Linux。
- 策略模板、管理员可见但不可扩大用户授权的管控。
- 完整可观测性、限流、任务恢复与安全测试。

### Phase 3：增强能力

- 开发容器适配、端口预览的显式授权、团队共享开发机。
- 基于仓库策略的命令白名单和细粒度数据保留。

## 12. MVP 验收标准

- 未授权目录的读取、符号链接逃逸和工作目录逃逸全部被本机拒绝。
- 解绑设备或撤销工作区后，已有连接无法继续执行新请求。
- Codex 凭据始终留在服务器，本机日志中不存在模型 Token。
- 两个会话不能越权访问对方未授权的设备或工作区。
- 文件并发修改可检测，不发生静默覆盖。
- 命令参数无 shell 拼接，超时后能确认整个子进程树的最终状态。
- 用户可从平台和本机两端查看、暂停并撤销授权。
- 审计记录能回答谁、何时、通过哪个会话、对哪个工作区执行了什么能力。

## 13. 原型说明

`local-agent-bridge-prototype.html` 聚焦管理与执行中心，不代表最终视觉稿。可交互内容包括：

- 切换设备在线/暂停状态。
- 选择不同授权工作区。
- 调整会话权限档位。
- 模拟新增工作区授权。
- 模拟普通执行、高风险审批和紧急断开。

所有数据仅存在于当前页面内存，刷新即重置。
