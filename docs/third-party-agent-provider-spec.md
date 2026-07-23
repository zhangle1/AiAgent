# 第三方代理与本地 CLI 检测规范

## 目标

将设置首页的“伙伴与代理”调整为“第三方代理”，提供本机 CLI 环境检测，并允许聊天窗口选择可用的本地编码代理。

首期内置两个提供方：

| 提供方 | CLI | 协议 | 聊天接管 |
| --- | --- | --- | --- |
| Codex | `codex app-server --stdio` | JSONL app-server | 支持 |
| CodeBuddy Code | `codebuddy`（官方安装包 `@tencent-ai/codebuddy-code`） | 官方公开页面仅说明交互式 CLI，未说明 Codex app-server JSONL 协议 | 暂不支持 |

## 官方兼容性结论

CodeBuddy 官方 CLI 页面说明其安装命令为 `npm install -g @tencent-ai/codebuddy-code`，依赖 Node.js 22+ 和 Git，支持 Windows。页面未公开 `app-server`、`--stdio` 或与 Codex 相同的线程/回合 JSONL 协议。

因此不得将 `codebuddy` 直接替换为 `codex app-server --stdio` 调用。首期将它作为“已检测、待协议适配”的第三方代理显示；只有确认官方提供非交互式/结构化协议后，才增加 `CodeBuddyChatService` 适配器。

来源：<https://www.codebuddy.cn/cli/>

## 后端

1. 新增 `IAgentProviderEnvironmentService`，只执行固定候选命令的 `--version` 探测，带超时且不使用 shell。
2. `GET /api/v1/agent-providers/environments` 返回名称、CLI、版本、安装状态、协议和 `chat_supported`。
3. Codex 候选路径复用现有 `Codex:Command` / `AIAGENT_CODEX_COMMAND` 与标准 npm 路径；CodeBuddy 使用 `CodeBuddy:Command` / `AIAGENT_CODEBUDDY_COMMAND` 与标准 npm 路径。
4. 聊天编排器仅接受检测为 `chat_supported` 的代理。当前 `codex` 可接管；`codebuddy` 返回明确的“已检测但协议未适配”错误。

## 前端

1. 设置首页卡片更名为“第三方代理”，跳转到 `/settings/agents`。
2. 新页面展示探测结果、命令、版本、协议、刷新动作和 CodeBuddy 安装/兼容性提示，并保存“默认聊天代理”首选项。
3. 聊天输入区从固定的“Codex 接管”复选框改为“本地代理”选择器，初始值读取首选项；检测不可用时自动回退。不可接管的 CodeBuddy 显示为禁用项，不会被当作 Codex 调用。

## 安全与验证

- 探测命令来自受控配置或固定路径；不拼接用户输入，不经过 shell。
- 前端不读取本机环境；仅调用后端检测 API。
- 不保存任何 CLI 登录令牌或环境变量值。
- 静态检查 API 类型、路由与 UI 状态；不在本规范实现中运行构建或安装第三方 CLI。
