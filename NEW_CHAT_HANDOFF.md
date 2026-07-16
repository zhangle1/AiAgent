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
