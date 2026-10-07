# AiAgent 开发与 Agent 协作说明

## 项目边界

AiAgent 是前后端分离的内网 AI 工作台。

```text
front (Next.js) → /api rewrite → backed (.NET 9 API)
                              ├─ Settings
                              ├─ GitAccount / CodeRepository
                              ├─ Knowledge / RAG
                              ├─ Chat / Agentic tools
                              └─ DashboardApp
```

- 后端目录：`backed/`
- 前端目录：`front/`
- 看板模板：`dashboard-templates/`
- 架构与实施文档：`docs/`
- 阶段规格与开发交接：`handoff/`

## 必须遵守的规则

1. 不读取、输出、提交或覆盖真实密钥、Token、数据库连接串和用户本地数据。
2. `backed/appsettings.json`、`backed/data/`、`front/.env*`、`node_modules`、`bin`、`obj`、`.next` 是本地内容，不提交。
3. 新增后端 API 时同步更新 DTO、前端 API 客户端和 TypeScript 类型。
4. 保留现有文件编码；涉及中文字符串或旧文件时先检查 BOM/编码，再做最小编辑。
5. Controller/Dynamic API 只承载 HTTP 协议；业务、路径校验、文件读写和外部进程控制放在 Service。
6. 前端组件不散落直接 `fetch`；接口封装在 `front/lib/*-api.ts`，类型放在 `front/lib/*-types.ts`。

## 后端约定

- 知识提炼采用 Analysis → Generation 两阶段：宿主确定分段，先分析全部原文，再按段生成并校验证据；模型调用预算覆盖两阶段，校验修正每段最多三次生成调用。队列、提炼和设置操作使用独立 `CopyNew()` 客户端，不跨请求共享连接。任务开始、进度及终态异常均隔离在任务边界；终态写库失败暂存内存并在状态查询时补写，不重复模型调用。草稿插入与文档成功状态在同一短事务内提交。

- Office 预览在 `KnowledgeOfficePreviewService` 中进行，与模型提炼无关；返回经过编码的受限 HTML，前端必须使用无权限 sandbox iframe，不得开放脚本或外链。DOC/XLS 仅在临时目录调用配置的 LibreOffice 转换，不修改 raw。提炼进度来自解析阶段和已校验证据覆盖率，不用计时器伪造百分比；取消必须传递到模型调用，`cancelling` 仍视为活动任务以防重复提交。队列列表只包含 `wiki_compile`，不混入 RAG 任务。

- 知识库创建/上传仅写入 raw，不自动索引或提炼。`knowledge_compiler.retrieval_mode` 默认 `wiki`：使用 Codex CLI/LLM API 读取知识表示层并校验引用；`rag` 才使用活动索引。查询不得写草稿或原文。规则与验收见 `docs/knowledge-wiki-retrieval.md`。

- 知识提炼的供应商无关循环位于后端项目内的 `backed/Services/Knowledge/Core/` 文件夹，与 AiAgent 后端一起编译和部署，不单独创建类库项目；仅接收文本与 `IKnowledgeModel`，不依赖 ASP.NET、数据库或文件系统。`KnowledgeModelAdapter` 复用现有 Codex/API 客户端；模型只提出结构化操作，宿主校验原文证据后保存草稿。
- `KnowledgeCompilationWorker` 为单后端实例的独立有界队列，任务类型为 `wiki_compile`；不得混入 `GetLatestJob` 的 RAG 索引进度，也不得改变活动索引版本。重启中断应标记失败供用户重试，不自动重复模型调用。旧 `/process` 接口保留同步返回契约。
- 公司／项目归属保存在知识库 `MetadataJson.organization`，保存时保留其他键。这是目录分类，不是新的租户或项目授权边界。

- 所有后续新增的实体字段必须显式标注 `[SugarColumn(IsNullable = true)]`；除非已明确完成现网数据迁移、回填和非空约束验收，不得新增非空列。

- 聊天图片附件必须先以不透明附件 ID 上传到后端受控临时目录；校验真实图片签名、大小、数量和当前用户归属后，才可转换为 Codex app-server 的 `localImage` 输入。发送后应将图片迁移到按用户和会话隔离的历史目录，并在聊天消息元数据保存不含真实路径的附件信息。不得接受浏览器提供的本地路径、将路径拼入 shell，也不得假定第三方 CLI 兼容该协议。
- 第三方 `codex exec --profile` 不传递 `--image`。如管理员启用图片 OCR，只能把后端从受控图片路径提取、并明确标记为不可信附件数据的文本注入 prompt；OCR 失败必须继续文字聊天，且 Python Worker 只能读取 `PythonWorkers:AllowedRoots` 内的路径。
- Codex 聊天的 `codex_sandbox_mode` 只能接受 `full-access`、`workspace-write`、`read-only` 三个服务端白名单值；`full-access` 才可映射为 `--dangerously-bypass-approvals-and-sandbox`，必须通过 `ProcessStartInfo.ArgumentList` 传参，不能由聊天文本或任意配置字符串拼接命令。

- Codex 模型目录与分版本升级由 `CodexModelPolicyService` 管理。新增 `gpt-6.1-sol` 时保留默认模型及已禁用的旧模型；仅支持 `low/medium/high/xhigh/max`，前端从策略接口获取模型与推理等级。

- Target framework：`.NET 9`。
- 使用 Furion Dynamic API；领域入口通常位于 `backed/Services/<Domain>/*AppService.cs`。
- SqlSugar 的 `Queryable` 排序不要使用 LINQ 的 `ThenBy`/`ThenByDescending`；多字段排序使用 `.OrderBy(x => new { x.Field1, x.Field2 })`，需要倒序时使用 SqlSugar 对应的 `OrderBy` 重载。
- DTO JSON 字段使用 `[JsonPropertyName]` 并与前端类型保持一致。
- 文件修改采用临时文件 + 原子替换；所有工作区路径必须验证仍在允许的根目录内。
- 不把业务逻辑塞进 `Program.cs`；仅在其中完成依赖注入与中间件配置。

### 主要领域

| 领域 | 位置 | 说明 |
| --- | --- | --- |
| 设置 | `Services/Settings` | 模型供应商与目录配置 |
| 聊天/Agent | `Services/Chat` | WebSocket/SSE、工具协议、Agent loop |
| 知识库 | `Services/Knowledge` | 文档、索引任务、RAG 状态 |
| 代码库 | `Services/CodeRepository` | 受限服务器目录、Git、索引 |
| 看板 | `Services/DashboardApp` | 工作区、预览运行时、Git、AI 文件操作 |

## 前端约定

- 聊天图形选择器仅展示架构、工作流、时序图、数据流、生命周期。共用 `aiagent-architecture` version 1，新增可选 `diagramType`、节点 `kind`、边 `style` 均须白名单校验；缺失类型按旧架构图处理。布局与避让位于 `architecture-layout.ts`；时序图不得合并重复消息，edges 顺序即消息顺序。HTML 导出只序列化受控 SVG 和转义文本，禁脚本、禁外部资源，不执行模型 HTML。导出恢复全部节点及边的可见度；离线说明使用原生 details，尺寸开关只使用 CSS。

- 聊天 AI 运行通过 `ChatRunDialog` 多选当前项目的仓库相对入口并组合普通聊天提示词；`ChatRuntimeService` 只消费显式准备请求对应的 `artifacts/aiagent-runs/<requestId>.json`，一次请求仅启动一次。服务端校验项目/仓库归属、路径及链接边界、显式端口和环境变量白名单；清单只描述现有 npm scripts 或可运行 .NET 工程，不接受任意 shell 命令。使用 `CodeRuntimeManager` 管理真实进程树，按后端→前端做 HTTP 就绪检查；端口冲突不能悄悄换端口，部分启动失败回收本组进程。状态、日志、停止接口必须验证项目访问权。浮窗不续期，`/runtime-test` 可见窗口才续期整组；独立端口访问无法计入，必须向用户明确说明。URL 主机来自浏览器当前地址，不声称已验证公网连通。切换项目或会话关闭配置弹窗；正常宿主关闭回收进程，重启不重放旧清单。验证使用临时测试工程，不启动用户真实仓库。

- 正式交互架构图通过 `aiagent-architecture` fenced JSON 与普通聊天消息一起持久化，使用 `chat-architecture.ts` 投影和校验后由 React/SVG 渲染，不执行模型 HTML、不写 public、不增加后端渲染服务。限制 version 1、1–40 个节点、100 条边、100000 字符及字段长度；校验唯一 ID 和边引用。来源为模型标注，不能当作系统验证结论。代码分析范围复用 tree 接口选择仓库相对路径，随消息组成提示词；会话/项目切换清空范围，重试复用已存请求。路径选择和只读提示不是新的权限隔离，实际读取沿用现有项目与仓库授权。可视化弹窗默认选择 interactive，不能用固定示例代替生成入口；固定示例不出现在聊天弹窗。正式画布支持深浅主题、搜索、节点拖动及画布平移，拖动位置仅属于当前视图，JSON 导出保持原始语义数据。正式画布与 Archify 固定示例独立。

- Archify 开发参考页面仅加载 `front/public/archify/` 的固定公开示例。iframe 使用 `allow-scripts allow-downloads`，不授予同源访问；CSP 禁止联网。不可把用户资料或任意生成 HTML 放入公开目录。更新使用固定上游版本和 `front/scripts/build-archify-demo.mjs`，保留许可证；AI 生成接入需另行设计数据校验与产物权限。

- 聊天打包先通过 `ChatPackageDialog` 复用代码库 tree 接口浏览相对路径，只读取目录元数据；选择单个解决方案/工程/JSON 和额外要求后填入聊天，不直接执行命令。切换项目关闭弹窗，异步目录响应在离开后失效；目标路径仅作为提示数据，模型必须重新验证目标存在、根目录边界和构建用途，不得静默换用其他入口。
- 聊天打包入口使用 `front/lib/chat-packaging.ts` 填充普通聊天提示词，由模型判断构建、验证与 ZIP 生成方式。产物统一放在目标仓库 `artifacts/aiagent-packages/` 并配置 Git 忽略；下载复用项目权限及仓库归属校验，仅该目录 ZIP 可作为交付包下载。ZIP 不做文本解析或解压预览。聊天卡片只识别当前项目的相对下载 API 链接，不信任任意外链或本机路径。

- 聊天可视化第一版是发送前的显式文本组合，位于 `front/lib/chat-visualization.ts` 与独立工具栏；只在普通聊天提交时应用，重试直接使用已保存消息，不重复包装或改变 `ChatStreamProvider` 协议。嵌入式编辑器不启用。Mermaid 使用现有 strict 渲染，Markdown 的 pre 组件身份须稳定。此模式的只读描述是模型提示，不是额外权限隔离。
- 图形及 Git 来源在独立弹窗确认后生效；切换项目或会话清空提交选择。Git 历史接口先验证项目访问权及仓库归属，仅分页读取本地 HEAD（每页 50 条），不 fetch、不切换分支；最多 20 条提交元数据作为不可信来源文本随消息保存，不表示已审阅代码差异。

- AppSidebar 使用 56px 图标栏与独立模块内容面板，展开总宽 296px；AuthGate 的桌面内容缩进须同步。收起仅隐藏桌面内容面板，移动端抽屉保留完整内容；保留全局 Ctrl / ⌘ + K、sidebar-toggle 与 mobile-drawer 事件及项目/会话操作。

- 聊天失败的 20 秒自动重试由 `ChatStreamProvider` 管理，复用同一 stream ID 和原始请求快照；底部 `ChatRetryNotice` 仅显示倒计时及立即重试／取消入口。重试必须先结束旧请求，取消倒计时与手动重试须互斥，停止与成功不能安排重试；错误事件不能广播成功完成事件。
- 原型 PSD 导出逻辑位于 `front/lib/prototype-psd.ts`。导出 iframe 禁止脚本，仅允许同源 DOM 读取，不得同时开启 `allow-scripts`；PSD 缓存必须随 HTML 变化失效。图层为区域像素图层，不得标为原生可编辑文字。

- Next.js App Router，默认开发端口 `3782`，监听 `0.0.0.0`。
- 后端地址由 `NEXT_PUBLIC_AIAGENT_API_BASE_URL` 控制；`next.config.js` 负责 `/api/*` rewrite。
- 页面保持轻量；复杂状态放在 Provider、hook 或领域组件中。
- 长列表、终端、聊天内容必须拥有独立滚动容器，不能撑破工作台布局。
- 项目资料树只展示文档白名单格式，不展示代码后缀；PDF/HTML 使用当前页受限 iframe，Office/文本走服务端受控提取预览，下载保留原文件。聊天中的项目资料引用复用右侧资料面板，不得通过刷新或页面跳转打开。
- 看板工作台相关代码在 `components/dashboard-applications/` 与 `lib/dashboard-application-api.ts`。
- 代码库养护入口为 `components/code-repositories/RepositoryMaintenancePage.tsx`；HTTP 契约位于 `lib/repository-maintenance-{api,types}.ts`。后台调度与编译分别位于 `RepositoryMaintenanceService`、`RepositoryMaintenanceBuildService`，测试使用临时本地 Git 远端，不能连接用户仓库。


## Git 规则

- 提交信息使用简洁中文或 Conventional Commit，例如 `feat: 增加看板工作区快照`。
- 推送前检查暂存列表，确认不存在本地配置、运行时数据、知识库原文、依赖目录和构建输出。
- 不使用破坏性 Git 命令（如 `reset --hard`）处理未知改动。
- Git 拉取/推送的命令输出应回传给用户；认证失败时不要输出凭据。
- Agent 完成项目代码修改后应创建代码变更集并通过自动校验；只有具备代码提交权限的人工审批人可以批准和执行交付。交付前必须复核审批快照指纹，禁止绕过变更集直接把 Agent 修改提交到远端。
- 代码库养护的定时执行仅负责生成建议或编译验证后的变更集；交付仍需有提交权限的用户逐次批准，交付时复核权限、编译快照与基线。仅推送本轮独立 `maintenance/*` 分支。普通变更集 API 不得重新校验、批准或交付养护副本。

## 文档更新

新增或显著调整能力时同步更新：

- `README.md`：面向使用者的能力、启动和安全说明。
- `AGENTS.md`：面向开发者与 Agent 的边界、契约和改码流程。
- `docs/`：需要保留设计理由、实现记录或验收说明时新增专题文档。
- `handoff/`：阶段性交接、实施快照和仍需对照的专题规格；新增或移动交接资料时更新 `handoff/README.md`。仓库顶层只保留项目入口文档，不新增零散规格或交接文件。

## DeepSeek Harness 客户端插件

- 可选客户端网关不得改变原有浏览器聊天和服务端 Agent 行为。
- 本地文件只能由客户端 Harness 的文件系统能力访问；远程 Codex 仅接收有长度上限的显式文本片段，在服务端隔离只读工作区运行，接口不得接受客户端本地路径。
- 插件登录令牌必须短期有效且只保存哈希；密码、令牌和平台模型密钥不得写入配置、日志或源码。
