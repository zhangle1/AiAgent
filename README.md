# AiAgent

面向内网开发场景的 AI 工作台：管理模型与 Git 账号、登记服务器代码库、构建知识库，并让 AI 在受限工作区中读取、修改、预览和 Git 管理看板应用。

## 当前能力

- Codex 图片上下文：选择 Codex 本地代理后，聊天框可上传 PNG、JPEG、WebP 或 GIF 截图；后端校验文件签名并以受控 `localImage` 输入传给 Codex，不会将浏览器提供的路径交给 CLI。

- 模型服务配置：LLM、Embedding、Search、TTS、STT、图像与视频模型。
- Git 账号与代码库：令牌加密保存、连接测试、克隆、代码库登记、拉取与提交推送。
- 知识库：文档导入、解析、索引、检索与聊天问答。
- Agent 对话：WebSocket 优先、SSE 降级，实时显示工具调用、Token 与结果。
- 流量统计：成功完成的自有 Agent 与第三方代理轮次会写入用户隔离的 Token 账本，可按代理、模型和日期查看趋势。
- Codex 接管：聊天中选择项目后可勾选“Codex 接管”，后端把项目目录与问题交给本机 `codex app-server`，并实时返回回答、执行轨迹与文件修改完成状态。
- 看板应用工作台：从 React + Vite + ECharts 模板创建独立工作区，动态端口预览、编辑文件、运行日志和 Git 管理。
- 可靠看板改码：Agent 先检查当前工作区与入口，再搜索、读取、基于 SHA-256 补丁写入并静态校验；不会把关联 Git 仓库作为第二个 AI 写入目标。

## 目录

```text
AiAgent/
├─ backed/                 # .NET 9 / Furion / SqlSugar 后端
│  ├─ Services/            # Settings、Chat、Knowledge、CodeRepository、DashboardApp 等领域服务
│  ├─ Dtos/                # API DTO
│  ├─ Entities/            # 数据实体
│  ├─ Rag/                 # RAG 适配与 Python 工作脚本
│  └─ PythonWorkers/       # 文档解析与索引 Worker
├─ front/                  # Next.js 16 / React 19 前端
│  ├─ app/                 # App Router 页面
│  ├─ components/          # 设置、聊天、知识库、代码库、看板工作台组件
│  └─ lib/                 # API 客户端与类型
├─ dashboard-templates/    # 可创建的看板模板
├─ docs/                   # 架构、Agent 与实施记录
├─ AGENTS.md               # 面向开发者和 AI Agent 的协作规则
└─ backed/appsettings.example.json
```

## 本地启动

### 1. 后端

复制配置模板并填入本机数据库与允许访问的代码根目录：

```powershell
cd backed
Copy-Item appsettings.example.json appsettings.json
dotnet restore
dotnet run
```

默认 Swagger：`http://localhost:5000/swagger`

### 2. 前端

```powershell
cd front
npm install
$env:NEXT_PUBLIC_AIAGENT_API_BASE_URL="http://localhost:5000"
npm run dev
```

默认地址：`http://localhost:3782`

前端通过 Next.js rewrite 将 `/api/*` 转发到后端。开发服务器监听 `0.0.0.0`，可通过内网 IP 访问；请在防火墙和 `next.config.js` 的开发来源白名单中配置实际网段。

### 3. 本机 Codex 接管（可选）

选择 Codex 本地代理时，点击聊天输入框左下角的图片按钮，或在输入区按 `Ctrl+V`，即可附加截图。默认最多 4 张、单张最大 10 MB，支持 PNG、JPEG、WebP、GIF；未发送图片暂存于后端 `data/chat_attachments`，发送后的图片会迁移到按用户和会话隔离的历史目录，并随聊天记录回显。CodeBuddy 当前未验证图片输入协议，因此不能使用该功能。

后端运行账户需要能够执行 Codex CLI，且已经完成 Codex 登录。默认执行 `codex app-server --stdio`；若 `codex` 不在后端账户的 `PATH` 中，设置 `AIAGENT_CODEX_COMMAND` 为可执行文件的绝对路径，或在后端配置中设置 `Codex:Command`。聊天窗口在选择项目后会默认勾选“Codex 接管”（可手动取消）；该轮会将项目根目录作为 Codex 的工作目录，并以完整权限执行，请只选择允许 Codex 修改的项目目录。

### 4. 流量统计与管理员视图

设置中的“流量统计”会记录每次成功完成的对话，按用户、代理类型、代理标识、模型和日期聚合。当前内置 Agent 与 Codex CLI 的 Token 由本地文本估算，页面会标记为预估；后续适配器若返回原始 usage，可写入同一账本。

接口为 `GET /api/v1/usage/summary?scope=me&days=365`。普通用户只能读取自己的数据；具备服务端 `admin` 角色的账号可使用 `scope=all` 获取全员汇总。不要在示例配置中填写真实账号或其他敏感信息。

## 项目与代码库运行

项目可包含多个前端或后端代码库。每个代码库可在“项目与代码库”中登记多个配置文件，并选择其中哪些允许在聊天菜单中直接编辑；聊天菜单会显示这些已授权文件、单库运行和打包入口。

- **单库运行与全部运行**：可以只启动某一个代码库的已启用运行配置，也可以启动项目内全部未运行的配置。
- **前端依赖安装**：首次点击运行时，如对应 `package.json` 所在目录不存在 `node_modules`，系统会先执行 `npm install`；克隆代码库本身不会自动安装依赖。
- **端口与进程隔离**：运行配置保存入口、脚本和首选端口；后端运行使用独立构建产物目录，避免锁定 Visual Studio 的 `bin/obj` 文件。
- **强制结束 Shell**：运行菜单仅显示活跃 Shell。每一项均可“强制结束”，会终止对应进程及其子进程；依赖安装阶段同样可结束。
- **配置文件安全**：聊天只能读取和保存明确勾选为“聊天可改”的文件，并在保存时使用 SHA-256 检查磁盘版本是否已变化。

## 看板应用与 AI 改码

看板应用从 `dashboard-templates/` 复制到独立工作区。若选择已登记代码库，工作区位于：

```text
<repository>/.aiagent-dashboard/<application-id>
```

看板 Agent 的安全流程：

```text
检查工作区 → 识别入口/依赖 → 搜索 → 读取真实文件
→ SHA-256 版本保护的补丁 → 静态校验 → 刷新编辑器与预览
```

因此，类似“在产线趋势旁增加报废柱状图”的请求会定位真实的 ECharts 页面与样式文件，而不是猜测或新建无引用的 `App.jsx`。

详细设计和实施记录：

- [看板 AI 精准改码解决方案](docs/dashboard-ai-editing-reliability-plan.md)
- [看板 AI 精准改码实施记录](docs/dashboard-ai-editing-implementation-log.md)

## 安全与提交规则

- 不提交 `backed/appsettings.json`、`.env`、`backed/data/`、知识库原文、索引、运行时工作区和构建产物。
- 提交前使用 `backed/appsettings.example.json` 作为配置样例，不得在 README、Issue、日志或代码中写入真实密码、Token 或 API Key。
- 代码库访问受后端允许根目录约束；看板 Agent 只能写入当前看板工作区。

## 技术栈

| 层 | 技术 |
| --- | --- |
| 后端 | .NET 9、ASP.NET Core、Furion、SqlSugarCore、Swagger |
| 前端 | Next.js 16、React 19、TypeScript、Tailwind CSS |
| AI/RAG | 可配置 LLM、Python Workers、LlamaIndex 适配 |
| 看板 | React、Vite、ECharts、受控 npm 运行时 |

## 开发协作

开始改动前请阅读 [AGENTS.md](AGENTS.md)。其中定义了前后端契约、Agent 工具流程、编码/密钥处理和不应提交的本地内容。

## 管理配置与账户权限

- 首次以 CodeFirst 启动后会确保存在默认管理员：账号 `superadmin`、密码 `123456`。如果该账号已经存在，只会补齐管理员角色，不会重置其密码；首次登录后请立即修改默认密码。
- 公开注册已关闭，`/register` 会返回登录页，创建新账号只能通过“设置 → 管理配置 → 用户管理”。
- 管理员可为普通用户分配一个或多个代码项目。聊天的项目选择菜单、项目偏好保存和实际聊天请求都会使用同一授权校验，用户不能通过直接调用接口绕过项目范围。
- 用户管理采用可搜索表格，可按账号或别名筛选；新增、编辑授权和重置密码均在弹窗内完成。重置密码会使该用户所有现有登录会话失效。
- 管理配置提供三个卡片入口：用户管理、历史会话（只读审计）和用户流量。流量支持按日、周、月、年聚合，并提供时间范围、用户筛选与柱状趋势图。

管理员接口均位于 `/api/v1/admin/*`，只接受服务端根据会话识别出的管理员身份；浏览器不会提交可伪造的管理员或用户身份字段。
