# AiAgent 会话开发交接

更新时间：2026-07-16  
工作目录：`E:\项目\know-why\AiAgent`

## 本轮目标

围绕“代码项目驱动的聊天会话”完善侧边栏与聊天记录：

- 会话必须关联代码项目，并只展示当前登录用户自己的会话。
- 项目行保留 `+`，用于该项目下新建会话。
- 项目行 `…` 用于项目置顶、重命名、会话排序方式。
- 会话行 `…` / 整行右键用于会话置顶、重命名、优先级、删除。
- 置顶会话要显示在顶部的“置顶会话”分组。

## 已完成的核心实现

### 聊天记录与项目关联

- `ChatCompleteRequest` 新增 `code_project_id`。
- 聊天页面会根据当前选择的代码项目发送该字段和项目内代码库名称。
- `AiChatSession` 已新增：
  - `CodeProjectId`
  - `SortOrder`
  - `Priority`：`high` / `normal` / `low`
  - `IsPinned`
- WebSocket 聊天处理器已经在流式对话开始时保存用户消息、结束时保存助手消息；HTTP 和 SSE 路径也复用会话服务。
- 会话列表接口明确支持：`GET /api/v1/sessions/list?limit=100`。之前前端请求此路径而后端没有显式路由，是历史会话刷新不稳定的主要风险点。

### 用户级项目偏好

新增实体 `AiChatProjectPreference`，表名：`ai_chat_proj_pref`：

- 按 `UserId + CodeProjectId` 保存，不影响其他用户。
- 保存项目是否置顶及项目会话排序方式：`updated` / `priority` / `manual`。
- 后端启动 CodeFirst 时自动创建表和唯一索引。
- 新接口：
  - `GET /api/v1/sessions/project-preferences`
  - `PATCH /api/v1/sessions/projects/{projectId}/preference`

### 侧边栏交互

文件：`front/components/layout/AppSidebar.tsx`

- 空项目不会进入“项目会话”栏；只有当前用户在项目中实际生成过会话后，项目才出现。
- 项目标题右侧：
  - `+` -> `/chat?project={id}`，以该项目开新会话。
  - `…` -> 置顶项目、重命名项目、按最近更新/优先级/手动排序。
- 会话行：
  - 鼠标悬停显示 `…`；右键整行也打开同一个菜单。
  - 菜单支持置顶会话、重命名会话、优先级（低/普通/高）和删除。
  - 置顶会话会出现在顶部“置顶会话”分组。
  - 仅当项目排序为“手动排序”时允许拖拽会话。
- 项目重命名调用既有的 `updateCodeProject`；会话重命名调用 `PATCH /api/v1/sessions/{id}`。

## 当前改动文件

### 后端

- `backed/Dtos/Chat/ChatDtos.cs`
- `backed/Dtos/Chat/SessionDtos.cs`
- `backed/Entities/Chat/AiChatSession.cs`
- `backed/Entities/Chat/AiChatProjectPreference.cs`（新文件）
- `backed/Services/Chat/ChatSessionAppService.cs`
- `backed/Services/Chat/ChatSessionService.cs`
- `backed/Services/Chat/ChatWebSocketHandler.cs`
- `backed/Services/Settings/ModelSchemaInitializer.cs`

### 前端

- `front/components/chat/KnowledgeChatHome.tsx`
- `front/components/code-repositories/CodeProjectSettingsPage.tsx`
- `front/components/layout/AppSidebar.tsx`
- `front/lib/chat-api.ts`
- `front/lib/session-api.ts`

## 必须验证的流程

1. **重启后端一次**。这会执行 CodeFirst，补齐 `AiChatSession` 新列并创建 `ai_chat_proj_pref` 表及索引。
2. 登录一个用户，打开聊天页，选择某个代码项目并发送消息。
3. 发送完成后确认：
   - 项目会出现在侧边栏；
   - 新会话出现在该项目下；
   - 刷新浏览器后仍存在。
4. 在项目行点击 `+`，确认 URL 进入 `?project={id}` 且可发起新会话。
5. 在项目行点击 `…`，确认排序切换可用；切到“手动排序”后验证拖拽。
6. 在会话行点击 `…` 或右键，选择“置顶会话”，确认顶部出现“置顶会话”分组且刷新后仍存在。
7. 验证项目置顶、项目重命名、会话重命名和优先级排序。
8. 用第二个账号登录，确认不会看到第一个账号生成的会话或其项目偏好。

## 已做的静态检查

- 未执行 `dotnet build`、`npm run build` 或其他编译命令。
- `AppSidebar.tsx` 的 TypeScript/TSX 语法检查通过。
- `git diff --check` 通过。
- 本轮修改的文件均为 UTF-8 无 BOM，已检查编码有效性。

## 继续开发时的注意点

- 如果侧边栏显示为空，先在浏览器 Network 中检查：
  - `GET /api/v1/sessions/list?limit=100`
  - `GET /api/v1/sessions/project-preferences`
  这两个请求是否为 200；后端未重启时，新偏好接口可能不存在。
- 侧边栏加载已将“会话/项目请求”和“项目偏好请求”分开处理：偏好接口失败不能再把历史会话一并清空。
- `AiCodeProject` 是全局项目实体；项目重命名会影响所有用户。项目置顶和排序偏好则是按用户保存。
- 如果产品要求“项目名称也按用户自定义”，不能复用 `AiCodeProject.DisplayName`，应在 `AiChatProjectPreference` 增加 `DisplayNameOverride`，并优先显示该字段。
- 当前会话级 `IsPinned` 与项目级 `AiChatProjectPreference.IsPinned` 是两个不同概念：前者进入“置顶会话”，后者使项目分组优先显示。

---

## 阶段二：代码库运行、侧边工具与部署（2026-07-22）

### 当前目标

让已登记的 C# 后端和 TypeScript/JavaScript 前端能从聊天顶部“项目程序运行”启动、停止、查看终端输出和浏览器预览；并完成可部署的前后端打包脚本。

### 已完成

#### 1. 代码库文件与调试配置

- 代码库配置页只保留 `C#` 和 `TypeScript/JavaScript` 两个语言选项，改为单选。
- 恢复“选择文件”入口：
  - C# 可选择 `.sln`、`.csproj`；
  - 前端可选择 `package.json` 和配置文件。
- 调试配置分为 `C# 后端` 与 `前端（TypeScript/JavaScript）`：
  - `.sln` 仅用于组织解决方案；真正启动必须选可运行的 Web/API 或 `OutputType=Exe` 的 `.csproj`；类库不能启动。
  - 前端必须选 `package.json`，并可填写 `dev` / `start` 等 npm 脚本和端口。
- 文件刚被选择、但尚未保存代码库配置时，调试入口下拉框也会立即显示该文件，避免界面仍显示“选择调试入口”。

关键文件：

- `front/components/code-repositories/CodeProjectSettingsPage.tsx`
- `front/lib/code-repository-api.ts`
- `backed/Services/CodeRepository/CodeRuntimeManager.cs`

#### 2. 后端运行目录与许可证

- `CodeRuntimeManager` 启动 C# 项目时，工作目录已由“代码库根目录”改为“所选 `.csproj` 所在目录”。
- 原因：目标 CPS 项目通过 `Directory.GetCurrentDirectory()` 读取 `appsettings.json` 和 `license.json`；工作目录不正确会导致读取不到项目目录的许可证，甚至创建空许可证文件。
- C# 运行命令保留 `--no-launch-profile`，并通过 `--urls http://0.0.0.0:{port}` 由 AiAgent 管理端口；同时使用 `ASPNETCORE_ENVIRONMENT=Development`。

已确认的 Visual Studio 启动项目：

- 解决方案：`E:\项目\欣灵\xinlingCPS\srm-cps-api\GuoKun.CPS.SRM.XL.Api.sln`
- API 启动工程：`E:\项目\欣灵\xinlingCPS\srm-cps-api\GuoKun.CPS.SRM.Api\GuoKun.CPS.SRM.Api.csproj`

注意：最近一次日志的 Content root 是 `E:\项目\欣灵\xinlingCPS\guokun-srm-api\GuoKun.SRM.Api\`，它不是上面的 `GuoKun.CPS.SRM.Api`。若日志仍显示前者，说明当前保存的运行入口选错了代码库/项目，需要在代码库配置页重新选择正确的 `.csproj`。

#### 3. npm / Umi 停止与重启

- 停止运行时现在会等待 `Process.Kill(true)` 的退出结果；npm/Node 未退出时，会在 Windows 上以 `taskkill /PID <pid> /T /F` 作为进程树兜底。
- 停止成功后立即将运行状态标记为 `stopped`，避免状态一直停在 `stopping` 并导致“此项目已有正在运行的 frontend 进程”。
- 已手动清理过 `xinling-cps-srm-management-web` 遗留的 npm/UMI 进程树；未影响 AiAgent 自身的 Next 开发服务。

#### 4. 聊天工作区与部署

- 聊天顶部已加入项目运行菜单、右侧工作区面板、终端/文件/浏览器标签等交互；右侧区域可打开文件、浏览器和终端内容。
- 部署脚本与中文部署文档已创建在 `scripts/deploy/`，用于将前后端打包为一个服务器包；前端端口可配置。
- 前端生产构建遇到的 TypeScript 问题已逐项修正过，包括运行配置类型、可空 Git 状态和 `/login` 的 Suspense 边界。

### 当前需要执行的验证

1. 重新编译/重启 AiAgent 后端，使本阶段的 `CodeRuntimeManager.cs` 改动生效。
2. 在“项目与代码库”中确认 C# 入口是：
   `GuoKun.CPS.SRM.Api/GuoKun.CPS.SRM.Api.csproj`。
3. 启动后在终端确认 Content root 为：
   `...\srm-cps-api\GuoKun.CPS.SRM.Api\`。
4. 若仍出现许可证错误，检查该目录的 `license.json` 是否与本机硬件/有效期匹配；不要修改 AiAgent 来绕过外部系统的许可证校验。
5. 启动前端后，点击停止，再立即启动一次，确认不会出现 `This project already has a running frontend process.`。

### 继续开发时的约束

- 工作区有用户未提交的改动；不要使用 `git reset --hard` 或覆盖无关文件。
- 代码库包含中文和部分历史编码文件，编辑使用局部补丁，保持原编码。
- 除非用户明确要求，不执行 `dotnet build`、`npm run build` 等构建命令。
- 当前 `CodeRuntimeManager.cs` 的改动属于 AiAgent 后端，必须重新编译/重启运行中的 AiAgent 才会生效。

### 新会话起始提示

> 请先阅读 `E:\项目\know-why\AiAgent\NEW_CHAT_HANDOFF.md` 的“阶段二”部分。当前重点是：重启 AiAgent 后端后验证 C# API 的工作目录与许可证读取、验证 npm 前端停止后可重启；不要对外部 CPS 项目的许可证逻辑做绕过修改。
