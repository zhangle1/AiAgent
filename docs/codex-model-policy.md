# Codex 模型策略与聊天选择

## 目标

AiAgent 将 Codex 本地代理使用的模型从本机 Codex CLI 默认值中解耦。管理员可以维护允许使用的 Codex 模型、默认模型，以及普通用户能否在聊天中切换模型；聊天请求只能使用该白名单中的模型。

首期内置模型：

- `gpt-5.6-sol`：Sol，适合复杂、开放式的编码与分析任务。
- `gpt-5.6-terra`：Terra，适合日常开发任务，兼顾速度与质量。

模型实际可用性仍取决于运行后端账户的 Codex 登录状态、套餐和当时容量，平台不承诺任一模型始终可用。

## 配置模型

配置保存为全局 `AiSettingSnapshot`，键为 `codex_model_policy`，不写入或修改运行账户的 `~/.codex/config.toml`。默认配置为 Sol、Terra 都启用，默认 Sol，并允许聊天切换。

```json
{
  "allowed_model_ids": ["gpt-5.6-sol", "gpt-5.6-terra"],
  "default_model_id": "gpt-5.6-sol",
  "allow_chat_model_override": true
}
```

管理员通过 `PUT /api/v1/agent-providers/codex-model-policy` 更新该配置。服务端只接受内置模型目录中的模型 ID；默认模型必须在启用列表中。普通用户只能读取 `GET /api/v1/agent-providers/codex-model-policy` 的安全视图，不能修改策略。

## 聊天行为

1. 聊天页选择 `Codex 本地` 时，原有自有 LLM 模型选择器替换为 Codex 模型选择器。
2. 初始值取管理员默认模型；管理员禁用聊天切换时，下拉框只读。
3. 前端传递独立字段 `codex_model_id`，不能复用自有 Agent 的 `model_id`。
4. 后端再次解析策略：空值使用默认模型；非白名单模型、禁用切换时的非默认模型均拒绝。
5. 后端在 Codex app-server `thread/start` 中传入 `model`。
6. 返回消息、历史会话和流量记录使用实际 Codex 模型 ID 与显示名，便于按模型审计消耗。

## 运行时隔离

Codex 运行租约的缓存键必须包含模型 ID。这样同一个浏览器标签在 Sol 与 Terra 间切换时，会创建对应模型的 app-server 租约，不会错误复用旧模型的运行上下文。

心跳不指定模型，使用当前默认模型仅用于预热；真正聊天请求始终以服务器解析后的模型为准。

## 容量满与失败处理

`Selected model is at capacity` 属于 Codex 服务端临时容量问题，不是 AiAgent 配置或本地 CLI 安装错误。

- 后端将该类错误归类为“模型繁忙”，返回可读说明及当前模型。
- 首期不静默自动切换模型，避免模型、质量与流量记录和用户预期不一致。
- 聊天用户可重试，或在管理员允许时明确切换到另一个可用模型（例如 Terra）。
- 后续如需自动降级，应新增管理员配置的回退顺序、开关和每次切换审计；不能只靠字符串匹配后静默重试。

## 验收

## 5.6 Luna、推理等级与第三方 Profile 扩展

- 内置模型扩展为 `gpt-5.6-sol`、`gpt-5.6-terra`、`gpt-5.6-luna`；管理员可分别启用并设置默认值。
- 管理员可以维护 `minimal`、`low`、`medium`、`high`、`xhigh` 推理等级、默认等级，以及是否允许聊天框覆盖默认值。AiAgent 将选中的等级通过 app-server 的 `modelReasoningEffort` 下发。
- 管理员可新增第三方 Codex profile：显示名、`profile_name`、可选的实际 `model_id`、说明，以及该 provider 是否支持 `modelReasoningEffort`。profile 名称仅允许字母、数字、连字符和下划线。
- profile 不写进 `thread/start`。内置模型仍走 app-server；第三方 profile 走 `codex exec --profile <profile_name> --json`，该 profile 对应后端运行账户 `~/.codex/<profile_name>.config.toml`。
- 第三方项未填写 `model_id` 时，AiAgent 不传 `model`，完整采用 profile 文件中的模型配置；填写后才覆盖为管理员指定的模型 ID。
- 运行时租约键同时包含工作目录、模型选择与 profile，避免项目切换或 DeepSeek 等不同 profile 复用到错误的 app-server 进程。

### 第三方 Profile 的 CLI 流式适配

配置了 `profile_name` 的第三方模型不依赖 app-server，而是由服务端执行 `codex exec --profile <profile_name> --json`。后端逐行解析 JSONL，将文本、工具和文件修改事件映射为现有 SSE；首次运行返回的 Codex session id 会保存到 AiAgent 会话偏好，后续消息自动使用 `codex exec resume <sessionId> --json`。

该模式不使用 `--ephemeral`，因此可保留 Codex CLI 上下文。浏览器停止生成时取消 HTTP 请求，后端会终止对应的 CLI 子进程树。第三方 provider 的模型、认证、审批和 sandbox 应在其 profile 文件中配置；只有管理员勾选“允许在聊天中选择推理等级”时，AiAgent 才通过 `--config model_reasoning_effort=<等级>` 覆盖 profile 默认值。

- 管理员可启用/停用 Sol、Terra，设置默认模型和聊天切换权限。
- 普通用户无法调用策略更新接口。
- Codex 请求未指定模型时使用管理员默认模型。
- 普通用户提交不允许的 `codex_model_id` 被后端拒绝。
- Codex 消息与流量记录显示实际使用的 Sol 或 Terra。
- 切换模型后不会复用先前模型的运行租约。
- Codex 容量满时显示“模型繁忙”而非笼统的内部错误。
