# 模型发现与坤伴订阅验证记录

日期：2026-10-21。用户授权继续执行此前待验证的后端专项测试、共享包／客户端构建和隔离启动验证。源码基线为 `b332fd37128dedc34e94fcba2810c8a99a27ede0`，本轮改动尚未提交或推送。

## 自动验证

| 检查 | 结果 |
| --- | --- |
| 后端模型发现、元数据、订阅认证和协议转发专项 | 20 通过，0 失败 |
| 客户端订阅纯逻辑 | 11 通过，0 失败 |
| 登录 IPC 主窗口限制 | 1 通过，0 失败 |
| 网页合并、来源优先级、过期请求及顺序保存逻辑 | 9 通过，0 失败 |
| `@kunbuddy/shared` 构建 | 通过 |
| `@kunbuddy/desktop` 类型检查及 Electron 构建 | 通过 |
| 网页 TypeScript `--noEmit --incremental false` | 通过 |
| `pnpm check:client`、`pnpm check:agent-policy` | 通过 |
| UTF-8、项目 XML、TypeScript 语法及 `git diff --check` | 通过，记录于本轮变更集 |

后端命令为 `dotnet test backed.tests/AiAgent.Backend.Tests.csproj --artifacts-path <隔离目录>/dotnet --filter "FullyQualifiedName~KunBuddy|FullyQualifiedName~Discovery|FullyQualifiedName~MetadataSeed|FullyQualifiedName~KnowledgeModelCatalog"`。首次执行发现新增测试的到期数据更新缺少 Where 条件，修正后专项全通过。编译仍有已有代码的可空和异步警告；客户端构建仍有大分块提示。

## 实际 HTTP 与界面验证

测试在 `E:/项目/know-why/.codex-verification/kunbuddy-subscription/` 中使用临时 SQLite、合成账号、随机回环端口和独立 Chromium／KunBuddy 数据目录。未连接真实业务数据库、读取用户密钥或调用付费模型。复用了现有 Rust host 二进制；本轮没有修改 Rust 源码。

- 通过 Furion 注册实际后端控制器，验证网页登录、管理员模型检测、首次生成公开 `data/models/model.json`、订阅登录、目录获取和退出。网页 Cookie 不能用于订阅接口，订阅令牌不能调用设置接口。
- 通过实际 HTTP 网关验证服务端稳定模型 ID 转换、服务端密钥补充、原始 SSE、工具定义及图片字段保持、未知模型拒绝和退出后 401。
- 在浏览器组件验证环境中加载实际 `ModelServiceEditor`、`SettingsProvider` 和原有 CSS，经真实 HTTP 连接隔离后端。输入合成 Key 后自动检测，将旧的 12000 上下文更新为 1000000、最大输出更新为 65536；写入数据库并刷新页面后保留。保存后 Key 变为掩码，检测列表和成功提示继续可见。
- 启动构建后的真实 Electron 应用，通过设置 → 模型 → 添加服务 → 坤伴订阅弹窗登录。验证 Rust 持久化后的上下文／输出参数；公开 IPC 结果不含合成密码或上游 Key。通过实际 Agent 发送消息，等待助手回复持久化，再退出并确认订阅供应商行移除。

隔离 HTTP 启动器最初遗漏 Furion 所需的 Swagger 注册，补齐测试环境后通过；这不是生产启动配置缺失。浏览器自动化扩展不可用，因此界面检查通过隔离 Electron 的 CDP 完成。

## 证据与边界

本机证据包括 `backend-tests.log`、`client-ipc-test.log`、`client-build.log`、`http-result.json`、`web-ui-result.json`、`desktop-ui-result.json` 和界面截图，位于上述隔离目录。文件 SHA-256、源码清单及本轮指纹见 `kunbuddy-subscription-model-discovery.changeset.json`。

上述端到端检查使用可控模型端点。真实供应商的网络、Key 权限、实际容量声明及部署服务器环境仍取决于运行配置；没有用合成回复声称已验证真实 DeepSeek／其他供应商服务。Anthropic、Google、Responses 的协议字段另由后端专项和客户端配置逻辑覆盖。

本轮“订阅”按用户选择连接现有账号与全局授权模型，不包含套餐支付或新增逐模型授权表。未修改用户原有代码库／Git／部署脚本改动，未提交或推送。

## API Key 未保存问题回归

同日用户反馈：AiAgent 网页诊断在发送请求前报告 `API key is missing`。在实际网页组件和隔离后端上复现：先将合成配置的 Key 清空，再输入合成 Key，让浏览器中的模型列表请求返回 401，原代码不会提交配置，数据库查询仍为空。`verify-key-retention.mjs` 的修复前记录 `key-retention-before.log` 在“应保存为掩码”的断言失败，确立回归信号。

修复网页发现流程，让失败／空列表也保存同一配置快照，同时保留检测错误；没有检测结果时不更改原模型。修复后的 401 与空列表两种场景均验证：保存后读取掩码、刷新页面保留掩码、实际诊断端点使用服务端真实合成 Key 成功。两个场景的日志和结果 JSON 均保存在本轮隔离目录，未使用真实用户 Key。

这验证了保存缺口，不表示用户首次收到的上游 401 一定由它造成，也不证明真实供应商当前可用。已丢失或未保存的真实 Key 需要重新输入；界面继续明确区分连接配置已保存、模型发现失败和本地保存失败。
