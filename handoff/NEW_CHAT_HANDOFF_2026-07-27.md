# AiAgent 开发交接（2026-07-27）

## 工作目录与 Git 状态

- 工作目录：`E:\项目\know-why\AiAgent`
- 当前分支：`main`
- 最新提交：`84cccbb feat: 完善代码库运行、Git 管理与聊天文件定位`
- 工作区：干净（本次交接创建本文件后会新增此未跟踪文件；如需保留请提交它）。

## 远程仓库与推送

- `origin` 获取地址：`https://gitee.com/yun_kun/ai-agents.git`
- `origin` 推送地址：码云和 GitHub 两个地址，执行 `git push origin main` 会同时推送。
- `github`：`https://github.com/zhangle1/AiAgent.git`
- 当前码云、GitHub 的 `main` 都已包含 `84cccbb`。

### SourceTree / GitHub 推送修复

用户级 Git 配置把 GitHub 指向了 `http://127.0.0.1:7897` 代理；该代理对 GitHub 的 TLS 连接报过 `SSL_ERROR_SYSCALL`。本仓库已增加本地 `remote.origin.proxy` 直连覆盖，双远程实际推送与干跑均正常。

不要删除 `origin` 的第二个 GitHub `pushurl`，除非明确要取消“一次推送同时发布码云和 GitHub”。

## 本轮已交付功能

### 1. 聊天中的代码文件定位

规格文档：[聊天文件引用与右侧定位规格](CHAT_CODE_FILE_NAVIGATION_SPEC.md)

- Codex app-server 结束任务后，会把已修改文件附加为内部 Markdown 文件链接。
- 聊天回答中的常见源码路径、行内代码路径、以及模型错误生成的“文件名 + http 链接”，都会优先按代码文件处理。
- 点击文件后，聊天右侧检查器打开对应代码库文件；带行号时高亮并滚动到目标行。
- 自有 Agent Loop 的代码检索引用本来就带代码库名、相对路径和行号，继续复用右侧检查器。
- 新增受限解析接口：
  `POST /api/v1/code-repositories/projects/{projectId}/resolve-file-reference`
  它只解析当前用户可访问项目内、已登记代码库里的唯一文件；拒绝不存在、歧义或越界路径。

关键文件：

- `front/components/chat/MarkdownMessage.tsx`
- `front/components/chat/KnowledgeChatHome.tsx`
- `front/lib/code-repository-api.ts`
- `backed/Services/CodeRepository/CodeRepositoryAppService.cs`
- `backed/Services/CodeRepository/CodeRepositoryManager.cs`
- `backed/Services/Chat/Codex/CodexChatService.cs`
- `backed/Dtos/CodeRepository/CodeRepositoryDtos.cs`

### 2. 代码库运行与配置

- 聊天顶部“项目程序运行”支持按代码库单独运行、打包与查看状态。
- 运行配置支持选择解决方案/工程文件、配置文件和上传文件到当前目录。
- 前端运行时首次启动依赖安装遵循显式运行触发，不在克隆后自动执行。
- 运行面板可展示 Git 分支、远程领先/落后及文件数量。

相关文件主要位于：

- `front/components/chat/ChatRuntimeToolbar.tsx`
- `front/components/code-repositories/CodeProjectSettingsPage.tsx`
- `front/components/code-repositories/CodeRepositoryCenter.tsx`
- `backed/Services/CodeRepository/CodeRepositoryManager.cs`

### 3. Git 管理

- Git 页面按项目/代码库查看状态。
- 支持远程分支、分支切换、工作区/待推送/待拉取差异查看。
- “更新代码库”执行撤回本地修改后拉取远程，并在界面上提供非开发人员可理解的确认提示。

关键文件：

- `front/components/settings/git/GitWorkspacePage.tsx`
- `front/lib/code-repository-api.ts`
- `front/lib/code-repository-types.ts`
- `backed/Services/Git/CodeRepositoryGitService.cs`
- `backed/Services/Git/GitWorkspaceService.cs`

### 4. 会话与侧边栏

- 会话模型保存项目关联及排序/优先级相关字段。
- 侧边栏的项目与会话菜单、置顶/归档/重命名等既有改动仍在本提交范围内。

关键文件：

- `backed/Entities/Chat/AiChatSession.cs`
- `backed/Services/Chat/ChatSessionAppService.cs`
- `backed/Services/Chat/ChatSessionService.cs`
- `front/components/layout/AppSidebar.tsx`
- `front/lib/session-api.ts`

## 建议的人工验证

> 本轮遵循编码保护规则，没有执行 `dotnet build`、`npm run build` 或其他编译命令。

1. 刷新或重启浏览器前端，选择一个有已挂载代码库的项目。
2. 在 Codex 本地代理中要求修改/检查一个具体文件；确认回答的“涉及文件”点击后在右侧打开，而不是跳转新网页。
3. 在自有 Agent Loop 中检索带文件路径的代码；确认代码引用能在右侧打开并按行定位。
4. 点击普通 `https://` 文档链接，确认仍在新标签页打开。
5. 在 Git 页面确认“更新代码库”弹窗文案、远程分支、Diff 与分支切换均符合预期。
6. 在 SourceTree 刷新或重启后执行 `origin/main` 推送；预期码云和 GitHub 均成功。

## 开发约束

- 代码库包含中文和历史编码文件；对现有文件只做局部补丁，避免整文件重编码。
- 除非用户明确要求，不执行编译、构建、发布或会影响外部代码库的破坏性 Git 操作。
- “更新代码库”会撤回本地代码修改，后续若调整该功能必须保留明确确认提示。
- 不要把 Agent 给出的绝对本地路径直接返回给浏览器或用作文件读取权限；必须先经过项目范围解析。

## 新会话起始提示

```text
请先完整阅读 `handoff/NEW_CHAT_HANDOFF_2026-07-27.md`。
确认当前 Git 状态、交接中的已交付功能和未验证项后，再处理新的需求。
不要执行 build/compile，除非用户明确要求。
```
