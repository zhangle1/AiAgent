# AiAgent 日常开发交接（2026-08-03）

## 接手结论

当前工作区基线为 `a1a718d`（2026-08-03 08:32，DeepSeek Flash 适配、登录与聊天界面优化）。其上有一组**未提交**改动，主要完成：

1. 第三方 Codex Profile 的图片 OCR 降级方案（PaddleOCR CPU）；
2. 聊天项目侧栏的归档、置顶与排序体验；
3. 移动端入口与“提交并推送”对话框的可用性优化。

工作区为脏状态，包含 21 个已跟踪文件修改及 OCR Worker、研究文档、`playground/` 等未跟踪内容。接手时请保留这些改动，不要使用会覆盖工作区的 Git 操作。

## 已提交基线

| 提交 | 内容 | 交接关注点 |
| --- | --- | --- |
| `a1a718d` | DeepSeek Flash 适配；登录调整；聊天界面优化 | 已在当前分支 HEAD，建议先回归模型选择、登录跳转与聊天流。 |
| `e84cc8d` | 移动端优化 | 当前未提交的移动端改动是在此基础上的补充。 |
| `19759f3` | Prompt 提示词模板 | 与本次 OCR 注入同属 Prompt 输入链路，回归时注意模板与图片同时使用。 |

## 未提交功能一：第三方 Profile 图片 OCR

### 目标与边界

原生 Codex 保持受控 `localImage` 图像输入。第三方 `codex exec --profile` 不再传递 `--image`，改为在服务端从受控图片路径提取 OCR 文本，再以明确的“不可信附件数据”边界注入 Prompt。OCR 失败、超时或未安装依赖时必须继续正常的纯文本聊天。

该设计和调研依据见 [third-party-model-image-ocr-research.md](./third-party-model-image-ocr-research.md)。默认引擎为 CPU PaddleOCR；MinerU 仅作为后续复杂文档解析候选，不应作为聊天截图 OCR 默认依赖。

### 已实现内容

| 层级 | 已完成内容 | 主要位置 |
| --- | --- | --- |
| 策略 | 管理员可读取/更新 OCR 开关、语言、图片大小、Prompt 字符数与超时；默认关闭。 | `backed/Services/Chat/ImageOcrPolicyService.cs`、`AgentProviderAppService.cs` |
| 调度 | 顺序处理、单并发、SHA-256 缓存、同键请求合并、超时、SSE 状态通知；Worker 异常只记录警告并继续聊天。 | `backed/Services/Chat/ImageOcrService.cs` |
| 安全 | OCR 前检查受控路径、只接收服务端 `LocalImagePaths`、OCR 文本使用 `<attachment_ocr>` 包装并声明为不可信数据。 | `ImageOcrService.cs`、`CodexChatService.cs` |
| CLI 路由 | 仅在 `ProfileName` 存在时执行 OCR，并让第三方 profile 以空图片列表启动 `codex exec`。 | `backed/Services/Chat/Codex/CodexChatService.cs` |
| Worker | 新增基于标准输入/输出协议的 PaddleOCR CPU Worker 与安装脚本。 | `backed/PythonWorkers/ocr/` |
| 持久化 | 将是否使用 OCR、引擎、语言、置信度、耗时、缓存及截断状态写入助手消息元数据；不写入真实路径或 OCR 正文。 | `backed/Services/Chat/ChatSessionService.cs` |
| 配置与 UI | 示例配置增加 `PythonWorkers:Ocr`、`ImageOcr:CachePath`；管理员“第三方代理”页可维护 OCR 策略。 | `backed/appsettings*.example.json`、`front/components/settings/agents/AgentProvidersSettingsPage.tsx` |

### 新增/变更接口

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/api/v1/agent-providers/image-ocr-policy` | 读取当前 OCR 策略。 |
| `PUT` | `/api/v1/agent-providers/image-ocr-policy` | 管理员更新 OCR 策略。 |

### 部署前必做

1. 在后端运行环境执行 `backed/PythonWorkers/ocr/install.ps1`，或将 `PythonWorkers:Ocr:PythonPath` 指向已安装 `paddlepaddle` 与 `paddleocr` 的 Python 环境。
2. 确认 `PythonWorkers:Ocr:WorkerPath`、`ImageOcr:CachePath` 均为服务端可写目录，且图片历史目录位于 `PythonWorkers:AllowedRoots` 内。
3. 保持策略 `Enabled=false` 完成冒烟后，再由管理员启用；首次调用会下载/加载 OCR 模型，应观察 CPU、内存与首请求耗时。
4. 发布清单加入 PaddlePaddle/PaddleOCR 许可证与模型来源声明。

### 待验证项

- 原生 Codex：含 PNG/JPEG/WebP/GIF 附件时仍能接收原图，且不会依赖 OCR。
- 第三方 Profile：启用 OCR 后请求命令不带 `--image`，Prompt 只含有带安全边界的 OCR 文本。
- 关闭策略、图片超限、Worker 缺依赖、超时、空识别结果与取消请求均不应中断文字聊天。
- 同一图片、相同语言的重复发送应命中缓存；不同语言不能错误复用缓存。
- 管理员与普通用户的策略写权限、SSE 中 `image_ocr` 状态事件、聊天消息元数据均符合预期。

## 未提交功能二：项目会话侧栏

### 已实现内容

- 项目偏好新增 `is_archived`；归档项目默认从主列表隐藏，可在项目菜单恢复。
- 新增每用户的侧栏偏好表 `ai_chat_sidebar_pref`，支持“最近会话优先”与“按项目名称”排序。
- 项目排序始终先考虑置顶状态，再应用选定排序方式。
- 后端启动时创建/补齐 `IsArchived` 列、侧栏偏好表及 `UserId` 唯一索引。
- 前端 `AppSidebar` 增加项目菜单、归档项目入口和排序菜单；`front/lib/session-api.ts` 已同步 DTO/API 类型。

### 新增接口

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/api/v1/sessions/sidebar-preference` | 读取当前用户项目栏排序方式。 |
| `PATCH` | `/api/v1/sessions/sidebar-preference` | 更新当前用户项目栏排序方式。 |

### 待验证项

- 首次登录、已有项目偏好记录、无项目、全部归档、多个置顶项目的显示与排序。
- 归档/恢复后项目及当前会话的选择状态是否正确。
- 数据库升级时旧 `ai_chat_proj_pref` 表能无损补齐 `IsArchived`，并成功创建 `ai_chat_sidebar_pref` 与唯一索引。

## 未提交功能三：界面可用性补充

- 非聊天页在移动端提供工作台菜单按钮；聊天页仍使用自身的抽屉入口，避免重复按钮。
- “提交并推送”弹窗支持移动端底部展示、内容滚动、多行提交说明和 `Ctrl/Cmd + Enter` 提交。
- `README.md` 与 `AGENTS.md` 已补充第三方 Profile 的图片输入与 OCR 安全约束。

建议在窄屏设备或浏览器 DevTools 的移动视图回归：工作台菜单开关、长提交信息、软键盘遮挡和弹窗关闭交互。

## 验收顺序建议

1. 先运行后端数据库初始化，并确认两张聊天偏好表的结构与索引。
2. 未开启 OCR 时回归原生 Codex、第三方 Profile 与普通纯文本聊天。
3. 配置 OCR Python 环境后，以单张中英文截图验证第三方 Profile 的成功、缓存、失败和取消四条路径。
4. 回归侧栏项目归档/恢复、置顶、两种排序及移动端导航。
5. 完成后再将 OCR Worker、服务、DTO、前端 API/UI、数据库初始化、文档与示例配置作为同一变更集提交；不要提交真实 `appsettings.json`、`data/`、Python 虚拟环境或 `playground/` 内的临时演示产物。

## 本次整理范围与状态

- 已检查：最近提交、工作区改动统计、未跟踪 OCR 文件、现有研究文档与配置示例。
- 已执行：`git diff --check`，未发现空白符错误。
- 未执行：前后端构建、数据库迁移/启动、PaddleOCR 安装与端到端聊天测试；因此本交接文档将上述内容均标为待验证，而非已验收。
