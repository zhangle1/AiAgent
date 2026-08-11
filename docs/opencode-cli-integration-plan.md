# OpenCode CLI 接入 AiAgent 规划

> 状态：调研与方案阶段，**本文件不代表已接入或已开放 OpenCode**。  
> 调研日期：2026-08-10。资料仅采用 OpenCode 官方文档、官方 GitHub 仓库与官方 npm 包说明。

## 1. 结论

**可以接入，且建议采用 OpenCode `serve` 的本地 HTTP/OpenAPI 方式，不建议把 `opencode run --format json` 作为主聊天协议。**

原因如下：

- OpenCode 官方提供无 TUI 的 `opencode serve`，默认仅监听 `127.0.0.1:4096`，并公开 OpenAPI 3.1、会话、消息、权限、差异和 SSE 事件接口。
- 它能天然对应 AiAgent 现有的“创建会话 → 发送消息 → 实时展示工具/文本 → 中止 → 展示文件差异”的交互，而不是解析一套未承诺稳定结构的 CLI 标准输出。
- `opencode run` 确实支持非交互调用与 `--format json` 原始 JSON 事件，适合本机诊断或一次性降级执行；但官方 CLI 页没有为该 JSON 事件给出稳定的公开 schema。生产接入应以 `/doc` 暴露的 OpenAPI 规范为契约。

总体工作量评估：**中等，约 5–8 个开发人日完成最小可用版；另需 3–5 个开发人日完成权限审核、故障恢复和试点验证。** 对比已有 Codex 接入不需要复用 app-server JSONL 解析器，而是新增一个独立的 HTTP/SSE Provider Adapter。

## 2. 官方能力核实

| 能力 | 官方结论 | 对 AiAgent 的意义 |
| --- | --- | --- |
| 安装 / Windows | 官方 README 给出 `npm i -g opencode-ai@latest`；Windows 也给出 `scoop install opencode`、`choco install opencode`。 | 在部署机安装 CLI，后端服务账户需能执行 `opencode`。建议试点优先采用 Scoop/Chocolatey 或发布页 Windows 二进制，并记录版本。 |
| 基本启动 | 不带参数的 `opencode` 启动 TUI；`opencode run [message..]` 可程序化运行。 | 本地人工验收与后端健康探测可直接使用。 |
| 非交互 | `opencode run` 是官方的非交互命令，支持 `--session`、`--continue`、`--agent`、`--model`、`--file`、`--auto`。 | 可作为短任务或 HTTP 服务不可用时的降级通道；不作为主会话承载。 |
| JSON / JSONL | `opencode run --format json` 输出 raw JSON events；`opencode acp` 使用 stdin/stdout 的 nd-JSON。 | 可用于调试/兼容层，但不将其事件字段写死为线上协议。ACP 仅在需要接 ACP 客户端时再评估。 |
| HTTP / API | `opencode serve` 为 Headless HTTP 服务，公开 `/doc` OpenAPI 3.1。 | 适合作为 AiAgent 后端与 OpenCode 之间的稳定边界。 |
| 会话与消息 | 可创建、查询、终止、fork 会话；可同步发消息或异步 `prompt_async`。 | 将 AiAgent chat session 与 OpenCode session ID 建立映射。 |
| 流式与工具事件 | `/event` 与 `/global/event` 是 SSE；消息、工具调用、权限、文件变更均通过事件流/消息 parts 获取。 | 后端订阅 SSE 后转换为现有 WebSocket `content` / `tool` / `file-change` 事件。 |
| 权限 | 全局或 Agent 级 `permission` 支持 `ask` / `allow` / `deny`，并可对 `bash` 和文件路径使用匹配规则；权限请求有专用 HTTP 响应接口。 | 必须与 AiAgent 的角色/项目权限联动，不应因 Headless 模式直接启用全权限。 |
| 鉴权 | `OPENCODE_SERVER_PASSWORD` 可启用 HTTP Basic Auth，用户名默认为 `opencode`，也可用 `OPENCODE_SERVER_USERNAME` 覆盖。 | OpenCode 仅绑定本机回环；AiAgent 后端以 Basic Auth 调用，密码放服务器密钥/环境变量而非前端。 |
| 模型 / Provider | 可通过 JSON/JSONC 配置模型、Provider 和 Agent；Provider 参数可从环境变量或文件注入。 | OpenCode Provider 配置属于部署机运行时配置，不把第三方 API Key 回传或存入 AiAgent 数据库。 |

## 3. 与现有 AiAgent 适配的区别

### 3.1 Codex app-server（现有）

当前 `CodexChatService` 启动 `codex app-server`，由后端通过 stdin/stdout JSONL 自行完成协议初始化、请求 ID 关联、通知解析、会话保活及事件归一化。运行时按浏览器客户端与工作区持有 app-server 租约。

OpenCode **不应直接套用**该解析器：其推荐接入面是 HTTP + OpenAPI + SSE，且服务原生拥有 Session 和 Permission 请求接口。因此应抽象出统一的 `ICodeAgentRuntime`（或等价 Provider 接口），保留上层聊天、项目授权、审计和 WebSocket 输出，替换下层协议客户端。

### 3.2 CodeBuddy CLI（现有探测）

现有环境探测已将 CodeBuddy 标为“已检测到 CLI，但官方公开资料未提供与 Codex app-server 相同的可接管 JSONL 协议”。

OpenCode 与 CodeBuddy 的关键差异是：**OpenCode 官方明确提供 Headless HTTP Server、OpenAPI 3.1、SSE、Session、Message、Diff、Permission 等完整接口**。因此它可做可控的服务端集成；CodeBuddy 在取得官方、稳定的自动化协议前仍只适合环境检测，不应通过模拟终端输出来接入。

## 4. 推荐架构

```text
浏览器
  └─ AiAgent Frontend WebSocket
       └─ AiAgent Backend
            ├─ 鉴权、项目/目录授权、消息与审计入库
            ├─ OpenCodeRuntimeAdapter
            │    ├─ HTTP: POST /session、/session/{id}/prompt_async
            │    ├─ SSE: GET /event（转换为前端流式事件）
            │    ├─ HTTP: GET /session/{id}/diff、POST /abort
            │    └─ HTTP: POST /session/{id}/permissions/{permissionID}
            └─ 本机 OpenCode serve (127.0.0.1:<专用端口>)
                 └─ 已授权项目工作区
```

### 会话流程

1. 用户在 AiAgent 选择“OpenCode 本地”与已授权代码库。
2. 后端确认当前用户具有项目访问权限、服务账户对该工作区具有所需文件权限。
3. 若该 AiAgent 会话尚无 OpenCode session，后端创建 `/session`，并保存 `opencode_session_id`、工作区和 Provider 配置版本。
4. 后端向 `/session/{id}/prompt_async` 发送标准化 Prompt 和文本/附件 parts。
5. 后端订阅 `/event`，只转发属于该 session 的文本、工具、权限、文件变化、完成或失败事件。
6. 用户点击“停止”时，后端调用 `/session/{id}/abort`；完成后读取 `/session/{id}/diff`，沿用现有代码变更确认页面。
7. 若收到权限事件，后端显示“允许一次 / 记住 / 拒绝”。只有权限响应接口返回成功后才继续；所有决定均入审计日志。

## 5. 最小可用范围（MVP）

### 必做

- 新增 `opencode` Agent Provider：安装检测、版本读取、服务健康检查。
- 启停独立 OpenCode Server：仅 `127.0.0.1` 监听，端口由 AiAgent 配置（建议 `4097`，避免与默认 `4096` 或其他用户进程冲突）。
- 新增 HTTP/SSE Adapter：创建/续用会话、异步发消息、SSE 解析、中止、差异读取。
- 将 OpenCode session ID 映射到现有 AiAgent Chat Session；禁止跨用户、跨项目复用。
- 在“第三方代理/管理配置”中配置命令、端口、Basic Auth 密钥引用、可用模型/Agent 白名单与默认 Agent。
- 配置并验证 Windows 服务账户对 `代码库目录`、`.opencode` 数据目录及 AiAgent 上传临时目录的最小写权限。
- 所有写文件、命令执行、`external_directory` 权限都进入现有审核/审计链路。

### 先不做

- 不开放 OpenCode 的 `/session/:id/shell`、`/pty` 等直连能力给浏览器。
- 不将 `--auto` 设为生产默认，也不设置全局 `"*": "allow"`。
- 不将 OpenCode Web UI 或 mDNS 暴露到内网。
- 不承诺兼容任意社区插件；插件先按白名单安装并在隔离测试项目验证。
- 不迁移历史 Codex 会话到 OpenCode。

## 6. 运行与安全配置建议

### 6.1 服务启动（概念示例）

```powershell
$env:OPENCODE_SERVER_PASSWORD = '<从服务器密钥读取>'
$env:OPENCODE_SERVER_USERNAME = 'aiagent'
opencode serve --hostname 127.0.0.1 --port 4097
```

AiAgent 后端仅访问 `http://127.0.0.1:4097`，并发送 Basic Auth。不要使用 `0.0.0.0`，不要开启 `--mdns`；需要跨主机部署时，优先使用 AiAgent 后端侧的受控反向代理/私网通道，而非直接暴露 OpenCode Server。

### 6.2 项目级权限基线（概念示例）

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "shell": "pwsh",
  "permission": {
    "read": "allow",
    "glob": "allow",
    "grep": "allow",
    "list": "allow",
    "edit": "ask",
    "bash": {
      "git status*": "allow",
      "git diff*": "allow",
      "git commit*": "ask",
      "git push*": "deny",
      "*": "ask"
    },
    "external_directory": "deny",
    "webfetch": "deny",
    "websearch": "deny"
  }
}
```

说明：此片段只表达权限设计，不可直接作为所有项目的最终配置。`edit: ask` 需要由 AiAgent 接收并响应 OpenCode Permission 事件；若实际协议/版本无法稳定透传该事件，则 MVP 应降级为 `edit: deny`（分析模式）或由受控审批规则明确放行，不能静默卡住。

### 6.3 Windows 特别检查

- 使用 `opencode --version`、`opencode serve --help` 作为安装后验收；系统服务账户与管理员交互账户可能不同，需分别验证 PATH、配置目录、Provider 凭证与代码目录 ACL。
- 官方文档说明 Windows 默认 shell 可为 `pwsh` 或 `cmd.exe`，也可在配置中指定 `"shell": "pwsh"`；若依赖 Git Bash，可设置 `OPENCODE_GIT_BASH_PATH`。
- OpenCode 目录、项目工作区与 AiAgent 的上传/Agent Markdown 目录必须采用可审计的最小权限 ACL；不要通过给磁盘根目录或 Everyone 开放写权限解决失败问题。

## 7. 开发拆分与验收

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| P0：探针 | CLI/版本、Server health、配置校验、端口占用、服务账户文件 ACL 诊断。 | 配置错误能在管理端解释到“命令 / 端口 / 凭证 / ACL”一级。 |
| P1：只读聊天 | 建立 Server、创建 Session、异步消息 + SSE 文本/工具流、停止与错误收敛。 | 已授权项目能多轮分析；前端无轮询卡死；网络/服务中断后可提示重试。 |
| P2：安全写入 | Permission 事件与审批 UI、Diff 显示、会话级取消、审计日志。 | 未审批的编辑/命令不能执行；允许后只影响所选工作区；可追溯用户、项目、会话、命令与文件。 |
| P3：模型与运营 | Agent/模型白名单、Provider 状态、使用量、重启恢复、部署脚本。 | 管理员可禁用模型/Agent；升级不会丢失配置或泄露密钥。 |

## 8. 风险与决策点

1. **版本锁定**：OpenCode 更新快，实施前应固定试点版本并将 `/doc` 导出的 OpenAPI 与 Adapter 合约测试固化；升级须在样板项目回归。
2. **SSE 事件兼容**：HTTP API 是官方公开面，但具体事件类型/parts 仍应以运行实例 `/doc` 和集成测试验证。Adapter 对未知事件只记录，不应使整轮失败。
3. **权限等待**：Headless 环境不可依赖终端人工确认。必须实现 Permission 响应桥接，或在 MVP 中采用严格、明确的自动策略；“默认全允许”不接受。
4. **Provider 凭证隔离**：OpenCode 自身 Provider 凭证与 AiAgent 登录账号是两层概念。密钥仅保存于服务器环境变量/机密文件，管理界面只显示状态与脱敏信息。
5. **网络暴露**：Server 默认回环监听和 Basic Auth 是最低要求；生产不开放 Web、mDNS、任意 CORS。 
6. **Windows 稳定性**：官方仓库持续记录 Windows/npm 安装与升级问题，试点需在目标 Windows Server 账户下做冷启动、重启、升级和断网测试，而不是只在开发机验证。

## 9. 建议的下一步

1. 在一台非生产服务器按固定版本安装 OpenCode，执行 P0 探针并保存 `/doc` 快照。
2. 选一个无敏感凭证的测试仓库，实现 P1 只读聊天，先验证本地 HTTP、会话关联和 SSE 流式转发。
3. 再实现 P2 权限审批；通过前不开放写文件、shell、Git 提交或项目根目录外访问。
4. 完成两类回归：已有 Codex app-server 聊天不能受影响；OpenCode 断连、权限拒绝、Server 重启、并发两个项目时都能正确清理会话。

## 10. 官方来源

- [OpenCode CLI 文档](https://opencode.ai/docs/cli/)：`run` 非交互、`--format json`、`serve`、`acp` nd-JSON、环境变量及 `OPENCODE_GIT_BASH_PATH`。
- [OpenCode Server 文档](https://opencode.ai/docs/server/)：`serve` 默认地址/端口、Basic Auth、OpenAPI `/doc`、Session/Message/Permission/Diff/SSE `/event` 接口。
- [OpenCode Config 文档](https://opencode.ai/docs/config/)：JSON/JSONC、配置优先级、Server、Shell、Provider、权限、环境变量/文件变量。
- [OpenCode Agents 文档](https://opencode.ai/docs/agents/)：Agent 级模型与 `ask` / `allow` / `deny` 权限、可控的 bash / 目录策略。
- [OpenCode 官方 GitHub README](https://github.com/anomalyco/opencode/blob/dev/README.md)：官方安装命令，含 Windows Scoop/Chocolatey 与 npm 包名。
- [OpenCode 官方 npm 包](https://www.npmjs.com/package/opencode-ai)：官方 npm 分发包 `opencode-ai`。
- [OpenCode GitHub Releases](https://github.com/anomalyco/opencode/releases)：Windows 二进制与版本发布记录。
