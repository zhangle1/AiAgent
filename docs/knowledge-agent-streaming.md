# 知识工作区 KnowAgent 流式对话（2026-10-10）

## 界面与模型选择

右侧面板由纯文本终端改为 KnowAgent 聊天：欢迎区、用户/助手消息、可展开工具卡片、模型思考、多行输入和停止按钮。输入区固定展示当前目录 URI 前缀；同目录文件预览不清空聊天，切换目录会取消旧流并建立新的对话上下文。历史保留在本页面内，不写入一般聊天会话或跨用户缓存。

模型列表只投影已配置 LLM 的 ID、名称、配置档名称、窗口和工具能力；不向前端返回密钥、端点、额外请求头。模型 ID 不存在时明确拒绝，不能静默回退到另一个默认模型。

已在模型配置启用 `supports_native_tool_calling` 的 API 使用 OpenAI-compatible 原生工具流，拼接分块的函数名和 JSON 参数，在收到完整调用后执行并将配对的 `role=tool` 结果送回下一步。未启用该能力的模型使用普通文字流及宿主提供的当前资料，明确提示能力边界；用户仍可执行显式目录命令。

## HTTP 与事件

| 接口 | 用途 |
| --- | --- |
| `GET /api/v1/knowledge-agent/options` | 安全模型元数据、已配置 CLI 模型策略、默认编译参数 |
| `POST /api/v1/knowledge-agent/stream` | `uri/model_id/message/history` → SSE 增量、工具、上下文、结束或错误事件 |
| `POST /api/v1/knowledge-agent/command` | 显式 ls/read/search/status 命令及分页参数 |
| `POST /api/v1/knowledge-agent/compile` | 原始资源 URI 列表及本批编译参数 → 任务列表与未入队原因 |

聊天执行参考 `Services/AgentRuntime/NativeTurnRunner` 的有限步骤与配对工具协议，复用 `RuntimeEventProjector`、`ToolDefinition`、`ILlmChatClient` 及其原生 HTTP 序列化。工具执行独立于通用代码仓库工具，不赋予 shell、任意本地文件或代码修改能力。

流式端点认证后执行，设置 `text/event-stream`、禁缓存与代理缓冲，每个事件立即 flush。前端支持跨块 UTF-8、CRLF 和多个 SSE 事件；EOF 没有 done 视为失败，保留已收到内容。LLM 客户端区分供应商 `[DONE]` 标记与普通 EOF，知识 Agent 在供应商缺少结束标记/结束原因时也拒绝成功。取消传播至 HTTP、模型与工具；失败和停止不会标记为完成，也不会覆盖旧轮次错误。

## 工具范围

- 每轮将所选 URI 解析为实际资源树节点，人员总根别名归一到本人根目录。每次执行重新读取可见树，资源删除或访问范围改变会报错。
- 工具的目标必须为选中节点或目录后代；文件范围不能访问父目录或同级文件。逻辑 URI 不解释为磁盘路径。
- ls 返回真实直接子节点与 next_offset，不用模型猜测列表。read 返回最多 6000 字符的正文页和偏移，默认 3000，保护 Unicode 页边界；内容为空时不伪造解析结果。
- search 每页扫描最多 40 个可见文件，返回命中片段、偏移及扫描限制；不把有限扫描声明为完整检索。status 返回本人最新正文/语义任务信息。
- 同一轮最多 12 个模型步骤、24 次工具调用，模型调用与压缩共用 16 次上限。非法/重复调用 ID、工具名、参数、输出过大及非完整响应均显式终止或返回工具错误。

## 上下文指标与压缩

上下文使用 UTF-8 字节数作 token 的保守上界估算，并包含消息序列和工具 schema；非供应商 tokenizer 或实际计费用量。输入上限 = 配置模型窗口 − 输出预留 − 1024 协议预留。界面展示输入用量/上限、窗口、压缩比例和次数，并可查看预留和压缩前后数值。

超限时分批压缩历史及已完成观察，保留文件 URI、偏移、决定与不确定性；原始资料不改写，证据需通过 read 重读。压缩整组调用与结果，避免留下孤立 role=tool 消息；保留当前问题。压缩不收敛、调用耗尽或仍超限均报错，不伪造压缩率。显示比例来自实际估算输入的前后差值。

## URI 前缀与编译配置

点击输入区 URI 前缀或 `/ls` 打开目录命令弹窗；输入 `/` 显示命令菜单。`ls/compiler/compile/parse` 也可作为快捷命令输入，编译别名统一打开 compiler 表单，不提交给模型猜测执行。

编译弹窗从当前目录选择最多 32 个原始文件，排除本人 summaries/wiki 生成产物；选择 LLM API 或服务器配置的 Codex/CLI Profile、模型、推理等级、调用预算与超时。只调用既有受控编译通道，不接受任意 shell 字符串。

配置由入队时复制并保存到 `AiKnowledgeJob.ConfigurationJson`（新增可空列，随现有实体初始化）；不修改全局 knowledge_compiler。重试读取快照。已有不同配置的活动任务时拒绝本次入队并返回原因，避免界面选择新模型但实际上沿用旧任务。

任务沿用 `wiki_compile`，执行 L2 解析后生成文件摘要、目录 L1 `.overview.md` 与 L0 `.abstract.md`，发布到本人工作区。沿用原子版本、缓存指纹和用户归属；正文已保存但语义失败时仍可阅读正文。队列、失败原因、取消和重试继续在文件处理弹窗及任务中心展示。

## 本轮验证

- 后端 Knowledge、LlmChatClient、NativeTurnRunner 专项测试 **81 项通过**，含新增原生工具分块/配对、真实 LlmChatClient 的模拟 HTTP 协议、第二配置档选择、未知模型拒绝、供应商异常 EOF、压缩测量、取消、实际目录与越界拦截、人员根别名，以及配置冻结/不同配置去重拦截/重试快照。
- 浏览器 `knowledge-agent.browser.mjs` 通过：运行中的前端 + 模拟模型目录/命令/编译 API + 独立本地分块 SSE 服务，验证首段先显示、指定 API、工具卡片、上下文指标、同目录预览不丢会话、命令/CLI 表单、断流、停止与窄屏弹窗。
- `knowledge-layout.browser.mjs` 回归通过：面板拖动/键盘/宽度保存、单预览滚动区、文件状态与刷新、弹窗焦点、窄屏布局。
- 前端所改文件静态类型/语法检查通过；git 空白检查通过。未运行前端生产构建。

后端测试经用户明确授权，在临时独立 artifacts 目录编译与执行，避免运行中的服务锁定普通输出目录。测试只用临时数据和替身模型，未调用真实在线 LLM 或真实 CLI，未重启用户服务、提交、推送或部署。上线需同步更新前后端，重启后端初始化可空字段；真实供应商及 CLI 的连通性仍依赖已配置环境。

```powershell
dotnet test backed.tests/AiAgent.Backend.Tests.csproj --artifacts-path <临时验证目录> --filter "FullyQualifiedName~Knowledge|FullyQualifiedName~LlmChatClientTests|FullyQualifiedName~NativeTurnRunnerTests"
# 指向已有 playwright 依赖目录；前端需已运行
$env:KNOWLEDGE_TEST_TOOLS = "<已有工具依赖目录>"
node front/tests/knowledge-agent.browser.mjs
node front/tests/knowledge-layout.browser.mjs
```
