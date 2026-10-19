# Codex / 第三方 Profile 运行诊断

## 在哪里查看

更新并重启前后端后，重新发送一次失败请求。Codex 基础诊断默认开启，不需要打开聊天页的 Debug 开关；失败消息会显示阶段、分类错误码与服务端生成的诊断编号。展开“失败详情”，点击“复制诊断信息”即可提供排查信息。

消息下方“查看诊断日志”读取当前用户、当前会话的历史诊断，沿用已有存储，默认保留 7 天；可通过 `ChatDebugTrace:RetentionDays` 设置 1–30 天。历史请求不会补写新日志；数据库保存失败时仍以服务器日志为准。普通非 Codex 请求保持原来的 Debug 开关行为。

`Run-AiAgent.ps1` 启动的部署包将后端控制台输出写入部署目录的 `runtime/backend.out.log` 和 `runtime/backend.err.log`。在部署目录执行以下命令，用页面复制的编号定位同一次请求：

```powershell
Select-String -Path '.\runtime\backend.out.log','.\runtime\backend.err.log' -SimpleMatch '粘贴诊断编号'
```

其他启动方式从后端标准输出或已配置的日志提供程序查找 `[CodexDiagnostic]`。本改动不新增独立日志文件，也不修改服务器日志轮转策略；需要保留 Information 级日志才能看到完整阶段。

## 如何定位

| 阶段 / 字段 | 含义 |
| --- | --- |
| `codex_request_context` | 进入模型前的身份、附件、项目引用或上下文处理失败 |
| `codex_model_resolution` | 管理端模型策略及推理等级校验 |
| `codex_workspace` | 服务器上的项目工作目录 |
| `codex_session_acquire` | 当前用户会话并发控制 |
| `codex_session_resume` / `resume_selected` | 是否从数据库选择了旧 CLI 会话 |
| `codex_cli_resolve` | 定位实际 CLI；完整路径仅在服务器日志 |
| `codex_profile_config` | 记录配置目录来源及文件是否存在，未读取或验证 TOML 内容 |
| `codex_process_start` / `pid` / `native_error` | 子进程是否启动，以及 Windows 原生错误码 |
| `codex_stdin` | 请求文本写入 CLI 标准输入 |
| `codex_cli_output` | 等待、收到 CLI 标准输出 |
| `codex_cli_exit` / `exit_code` | 子进程退出码与协议终态校验 |
| `codex_session_save` | 保存 CLI 会话标识 |
| `codex_app_server_start` / `codex_app_server_turn` | 内置模型 app-server 准备及回合执行 |
| `codex_completed` | Codex 服务调用完成；后续消息入库等仍由外层时间线记录 |

服务器结构化日志还记录运行账号、机器名、有效 CODEX_HOME、项目目录、CLI 路径及异常方法名。这些字段不发送到浏览器、不保存到会话诊断 JSON。比较服务器的运行账号和配置目录与手动执行成功的终端，能发现服务账号或配置位置差异。`profile_config_exists=False` 也可能使用了主配置内的旧式 Profile，不能单独证明配置错误。

主要错误分类：

- `codex_cli_missing` / `codex_process_start`：CLI 不存在或启动失败。
- `codex_profile_missing` / `codex_config_invalid` / `codex_cli_arguments`：Profile、TOML 或 CLI 参数不兼容。
- `codex_authentication` / `codex_access_denied`：鉴权或访问权限失败。
- `codex_resume_missing`：选中的 CLI 会话在当前机器不存在；需要结合运行账号与部署位置确认。
- `codex_model_unavailable` / `codex_context_limit` / `codex_rate_limit`：模型可用性、上下文或限流。
- `codex_network` / `codex_timeout`：网络或超时。
- `codex_protocol_incomplete`：进程退出但没有 `turn.completed`，不能报告成功。
- `codex_cli_failed` / `codex_runtime_failed`：无法归入已知类别，结合阶段、退出码、系统错误码与服务器方法名继续调查。

分类基于有限的 CLI 错误特征，属于排查提示，不能替代供应商故障结论。`Model metadata ... fallback metadata` 只记录为 `codex_model_metadata` 警告；退出码为 0 且收到 `turn.completed` 的回复仍正常完成。取消保留取消语义，启动后的异常会回收该请求的子进程。

## 数据与验证边界

诊断只输出固定提示和允许的元数据，不写入原始 stdout/stderr、提示词、聊天正文、图片路径、配置内容、API Key、连接串或原始异常消息。stderr 流采用有界缓冲分类，避免无限累积。未知错误不会把原文直接返回前端；模型本身生成的聊天答案和工具事件继续沿用原有聊天协议。

WebSocket 和 SSE 的失败提示、持久诊断及非流式调用的异常编号保持一致。请求内诊断编号由服务器生成，浏览器 JSON 不能覆盖。历史记录沿用当前用户的会话归属校验，不新增数据库列。不改变 CLI 命令参数、自动重试、Profile 配置、旧会话选择策略或模型调用权限。

验证使用内存 SQLite、临时假 CLI、模拟 HTTP/WebSocket，未调用真实模型或用户数据库。后端专项覆盖成功警告、启动与协议失败、错误分类、脱敏、取消、历史归属和原图能力回归；前端验证默认失败详情、历史记录、复制内容与桌面/移动端布局。后端项目排除 `PythonWorkers/**/.venv/**` 内容复制，避免将本地 Python 虚拟环境复制到测试或交付输出；实际 Python 环境按原部署流程准备。
