# DeepSeek Harness 部署与工作区写入配置

本说明对应 AiAgent 新增的 `deepseek-harness` 本地代理。它通过 DSH SDK JSON-RPC stdio 运行时访问**已登记的代码项目工作区**；浏览器不提供 CLI、配置文件路径或工作区路径。

## 1. 启用 AiAgent Provider

在服务器的私有配置（例如 `appsettings.dev.json`，不要提交）中合并以下 `Dsh` 节点。示例不包含任何供应商密钥。

```json
{
  "Dsh": {
    "Enabled": true,
    "Command": "dsh-jsonrpc-agent",
    "ConfigPath": "E:\\项目\\know-why\\deepseek-harness\\examples\\jsonrpc-agent\\aiagent.cordis.yml",
    "SessionRoot": "E:\\AiAgent\\data\\dsh-sessions",
    "Provider": "deepseek-official",
    "Model": "deepseek-chat",
    "ApiKey": "在此填写 DeepSeek API Key",
    "BaseUrl": "https://api.deepseek.com",
    "PermissionMode": "workspace-write",
    "AllowDangerFullAccess": false,
    "RuntimeLeaseSeconds": 90,
    "MaxSessionsPerUser": 2
  }
}
```

AiAgent 后端会可选加载其发布目录中的 `appsettings.dev.json`，因此本地开发与部署均可将该私有文件作为覆盖配置；保存后需要重启后端。默认部署模板位于 `E:\项目\know-why\deepseek-harness\examples\jsonrpc-agent\aiagent.cordis.yml`，它必须保留在 Harness checkout 内，以便通用 `dsh-jsonrpc-agent` 解析所需插件包；其内容不含供应商凭据，并使用 Windows 的 PowerShell 工作区沙箱。

字段含义：

| 字段 | 含义 |
| --- | --- |
| `Enabled` | 只有为 `true` 时前端才将 DSH 视为可聊天 Provider。 |
| `Command` | 服务账号可执行的 SDK JSON-RPC bin；固定为受控路径或命令名，不来自浏览器。 |
| `ConfigPath` | 管理员维护的完整 DSH `cordis.yml`，后端以 `DSH_CORDIS_CONFIG` 传入。 |
| `SessionRoot` | DSH JSONL/索引等持久化数据目录；应由服务账号可写、禁止浏览器直接访问。 |
| `Provider`、`Model` | 后端发送给 DSH `initialize` 的固定路由；不要让前端任意覆盖。 |
| `ApiKey`、`BaseUrl` | 可选地将 DeepSeek 凭据和 API 地址明文放在**不提交的** `appsettings.dev.json` 中；后端仅将它们注入 DSH 子进程环境，不写入日志、聊天或 API 响应。 |
| `PermissionMode` | `read-only`、`workspace-write` 或 `danger-full-access`；生产中建议仅使用前两者。 |
| `AllowDangerFullAccess` | 只有明确设为 `true` 才允许第三种模式；这是防误配开关。 |

可使用环境变量替代命令、配置路径和凭据：`AIAGENT_DSH_COMMAND`、`AIAGENT_DSH_CONFIG_PATH`、`DEEPSEEK_API_KEY`、`DEEPSEEK_BASE_URL`。当私有文件中的 `ApiKey` / `BaseUrl` 非空时，它们优先于同名环境变量；两者都不会发送到浏览器或记录到日志。

### Windows 中显示“未安装”的处理

AiAgent 现在会依次查找配置的 `Command`、服务账号的 `%LOCALAPPDATA%\pnpm\dsh-jsonrpc-agent.cmd`、`%APPDATA%\npm\dsh-jsonrpc-agent.cmd` 和系统 `PATH`。因此，pnpm 全局安装后可以保留 `"Command": "dsh-jsonrpc-agent"`；修改后必须重启 AiAgent 后端，再刷新“第三方代理”页面。

若 AiAgent 是 Windows 服务、IIS 应用池或计划任务，它常以**不同于安装 DSH 的用户**运行。这时应在该服务账号下安装 DSH，或将 `Dsh:Command` / `AIAGENT_DSH_COMMAND` 设为该账号可读取的 `dsh-jsonrpc-agent.cmd` 绝对路径；不要依赖交互式终端的 `PATH`。

## 2. 允许改代码与 Git commit

将 `PermissionMode` 设为 `workspace-write` 即可让 DSH 受控 shell/文件工具在当前已登记项目根目录及其临时目录中写文件；`git commit` 对工作区和 `.git` 的写入也在此范围内。不要把它设为 `danger-full-access` 来解决权限问题。

DSH 的 `aiagent.cordis.yml` 必须同时满足以下条件：

1. 使用 `@deepseek-ai/dsh-sandbox-local` 与 `@deepseek-ai/dsh-sandbox-policy`，并令 `mode` 读取 `process.env.DSH_PERMISSION_MODE`。
2. Windows 使用 `@deepseek-ai/dsh-pwsh-sandbox` + `@deepseek-ai/dsh-tool-pwsh`；Linux/macOS 使用 `@deepseek-ai/dsh-bash-sandbox` + `@deepseek-ai/dsh-tool-bash`。不要用无沙箱的 `dsh-bash-local` 作为生产写入执行器。
3. 文件工具使用 `@deepseek-ai/dsh-fs-sandbox`，不要用无约束的 `dsh-fs-local`。
4. 组装 `@deepseek-ai/dsh-sdk-jsonrpc-server`、选定的 LLM adapter、Agent spine、会话持久化和所需工具；stdout 不得配置 console logger 或 TUI。
5. DSH 进程的工作目录由 AiAgent 设置为已验证的 `AiCodeProject.RootPath`，配置中不得用聊天输入、用户 patch 或任意外部目录覆盖它。

核心配置片段如下；它应合并进已验证的完整 DSH 组合，而不是替代完整组合。

```yaml
- id: sdk-jsonrpc-server
  name: '@deepseek-ai/dsh-sdk-jsonrpc-server'

- id: sandbox
  name: '@deepseek-ai/dsh-sandbox-local'

- id: sandbox-policy
  name: '@deepseek-ai/dsh-sandbox-policy'
  config:
    mode: !!js process.env.DSH_PERMISSION_MODE ?? 'read-only'
    workspaceRoot: !!js process.env.DSH_CWD ?? process.cwd()

- id: pwsh-sandbox
  name: '@deepseek-ai/dsh-pwsh-sandbox'
  disabled: !!js process.platform !== 'win32'

- id: tool-pwsh
  name: '@deepseek-ai/dsh-tool-pwsh'
  disabled: !!js process.platform !== 'win32'

- id: bash-sandbox
  name: '@deepseek-ai/dsh-bash-sandbox'
  disabled: !!js process.platform === 'win32'

- id: tool-bash
  name: '@deepseek-ai/dsh-tool-bash'
  disabled: !!js process.platform === 'win32'

- id: fs-sandbox
  name: '@deepseek-ai/dsh-fs-sandbox'
  config:
    cwd: !!js process.env.DSH_CWD ?? process.cwd()
```

`approval: ask` 在当前 SDK wire 中没有 AiAgent 回答通道；无人回答时 DSH 会拒绝升级，这是 fail-closed。正常的 `workspace-write` 内文件修改和 `git commit` 不应请求更宽权限；模型若请求 `danger-full-access`，应被拒绝。后续若需要在 UI 中逐次批准升级，必须先实现文档中定义的 approval bridge，不能用 `danger-full-access` 代替。

首版会流式显示模型输出和工具活动，但 DSH SDK 事件尚未规定统一的“已修改文件列表”字段；因此 AiAgent 不会把缺少该字段误报为“未修改文件”。需要精确变更清单时，应在后续适配器中针对 DSH 文件工具事件补充受路径校验的解析。

## 3. Git 的服务账号前提

在部署 DSH 的服务账号下完成一次人工配置：

```powershell
git config --global user.name "AiAgent DSH"
git config --global user.email "aiagent-dsh@localhost"
git config --global --get user.name
git config --global --get user.email
```

确保该服务账号对每一个已登记工作区及其 `.git` 目录有写权限。`git commit` 应始终使用带 message 的非交互调用；若仓库有签名、hook、LFS、子模块或公司策略，需在测试工作区以同一服务账号验证。

当前 DSH 通用 shell 工具是“工作区写入”能力，不是“只允许 git commit”的命令白名单：若同时提供网络或远端凭据，模型理论上也能调用其他 shell 命令，例如 `git push`。若必须严格只允许 commit、不允许 push/网络，需要在 DSH 侧增加命令策略插件或以服务账号 ACL/网络出口策略强制限制；AiAgent 的 `PermissionMode` 本身不提供命令级 allowlist。

## 4. 上线检查顺序

1. 先保持 `Enabled: false`，执行 DSH bin 的 `--version` 并用 `--dump-config` 检查组合配置；不要把 stdout logger 加入 SDK runtime。
2. 使用无敏感测试仓库，以 `PermissionMode: read-only` 验证文本流、会话复用、停止和进程回收。
3. 改为 `workspace-write`，验证创建/编辑文件、`git status`、`git diff` 和带 message 的 `git commit`；同时验证对项目根外写入会失败。
4. 检查 AiAgent 的“第三方代理”页面显示 `SDK JSON-RPC stdio` 且聊天下拉框可选择 DeepSeek Harness。
5. 不要启用 `danger-full-access`；若确有不可替代需求，先在隔离测试机验证并同时设置 `AllowDangerFullAccess: true`。

`backed/appsettings.example.json` 和 `backed/appsettings.dev.example.json` 已给出不启用的节点模板。实际密钥和私有路径只放在服务器私有配置中。
