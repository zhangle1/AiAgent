# 聊天项目运行、预览与文件检查器规格

> 状态：待实现
>
> 范围：聊天工作区顶部工具栏、项目运行控制、动态端口分配、右侧浏览器预览、右侧文件检查器，以及聊天代码引用到文件定位的联动。

## 1. 目标与边界

用户已经在“项目与代码库”中登记项目、前后端代码库、构建入口和输出目录。本功能让用户可以从聊天工作区显式启动这些已登记的程序，并在同一页面查看运行状态、前端预览和代码文件。

本期目标：

1. 聊天顶部提供工具栏，可显示/隐藏左侧应用栏和右侧工具栏，并打开/关闭顶部菜单。
2. 顶部菜单增加“项目程序运行”入口；用户可启动、停止、重启当前项目的前端和后端程序。
3. 服务端通过受控 shell 进程运行程序，按角色动态分配端口，持续读取进程输出。
4. 右侧工具栏提供“浏览器预览”和“文件查看”两个页签。
5. 聊天回答中的代码检索引用显示为可点击文件卡片；点击后右侧文件页打开对应代码库文件并定位行号。

明确不在本期做的事情：

- 不把用户输入直接交给 `cmd /c`、PowerShell 或 shell 执行。
- 不自动执行 `npm install`、`dotnet restore`、数据库迁移、Git 拉取或修改源文件。
- 不暴露任意内网地址作为浏览器代理目标；预览只能代理本系统启动并登记的本地进程。
- 不在首次版本中支持 Docker、远程主机、生产环境部署或多个用户共享同一运行进程。

## 2. 现有基础与复用点

| 已有能力 | 位置 | 本期复用方式 |
| --- | --- | --- |
| 聊天工作区、顶部栏、流式消息 | `front/components/chat/KnowledgeChatHome.tsx` | 增加工具栏状态、运行菜单和右侧面板宿主。 |
| 聊天 Markdown 渲染 | `front/components/chat/MarkdownMessage.tsx` | 将代码引用渲染为文件卡片，并向页面抛出打开文件事件。 |
| 代码项目与代码库绑定 | `CodeProjectSettingsPage`、`CodeRepositoryManager` | 当前聊天所选 `CodeProjectId` 是运行范围；只能运行该项目下已登记的代码库。 |
| 代码目录树、读文件、Grep | `CodeRepositoryIndexService` | 右侧文件查看器直接调用已有 tree/read API，不重复实现文件访问。 |
| 代码检索引用元数据 | `CodeRepositoryIndexService.ToCitation` | 已提供 `repository_name`、`file_path`、`source`；本期补充可选 `start_line`。 |
| 打包 WebSocket 和进程输出 | `CodeRepositoryPackageWebSocketHandler` | 参考其受控 `ProcessStartInfo`、UTF-8 输出泵送和 WebSocket 事件格式。 |

## 3. 交互规格

### 3.1 顶部工具栏

聊天页顶部从左到右提供：

1. **显示/隐藏左侧栏**：折叠全局 `AppSidebar`，仅保留一个窄恢复按钮；状态按浏览器 `localStorage` 保存。
2. **会话标题/当前项目**：保留现有标题和项目徽标。
3. **工具菜单**：按钮点击打开菜单，点击空白处、再次点击、按 `Esc` 均关闭。
4. **显示/隐藏右侧栏**：打开右侧工具栏；若已打开则收起。首次点击“浏览器预览”或文件卡片时自动打开。
5. 保留现有新建会话和刷新操作。

工具菜单第一组固定为“项目程序运行”：

- `运行前端`
- `运行后端`
- `运行全部`
- 分隔线后显示当前运行项及 `停止`、`重启`、`查看日志`

无当前项目、项目未配置相应运行配置或已有同角色进程正在启动时，按钮必须显示原因并禁用，不允许静默失败。

### 3.2 运行面板

点击运行菜单中的任一项，在右侧栏自动切换到“运行”子视图（位于浏览器页签中运行状态区域，避免新增第三个主侧栏）。每一个运行项显示：

- 角色：前端 / 后端
- 所属代码库、启动命令摘要、PID、分配端口
- 状态：`starting`、`running`、`stopping`、`stopped`、`failed`
- 健康检查状态及最近一行输出
- 启动、停止、重启、复制预览地址、打开预览按钮
- 末尾 300 行可滚动终端输出；运行过程通过 WebSocket 持续追加。

启动策略：

- 前端成功条件：进程仍存活，且端口可连接并返回 HTTP 响应；默认等待 60 秒。
- 后端成功条件：进程仍存活，且端口可连接；默认等待 90 秒。
- 失败时保留终端输出、退出码和错误原因；不把失败进程标记为 `running`。
- 同一用户、同一项目、同一角色最多保留一个活动运行实例；“重新运行”先停止旧实例并等待结束。

### 3.3 浏览器预览

右侧栏“浏览器预览”页签包含运行选择器、地址栏、刷新、在新窗口打开和停止按钮。

- 默认选择当前项目最新的 `running` 前端实例。
- iframe 不直连 `http://服务器IP:随机端口`，而是使用同源代理：
  `/api/v1/code-runtime/runs/{runId}/preview/{**path}`。
- 后端代理只允许目标为该 `runId` 已登记的 `127.0.0.1:{port}`，同时代理普通 HTTP 请求和 WebSocket 升级请求，保证 Vite/Umi 热更新可用。
- 地址栏只允许该运行实例路径，例如 `/`、`/login`；禁止填写主机、协议、`..` 或其他 runId。
- iframe 的加载、连接失败和后端错误要在面板内明确提示，不使用空白页代替错误信息。

### 3.4 文件查看与聊天引用联动

右侧栏“文件查看”页签包含代码库选择器、目录树和只读代码编辑区。

1. 文件树来源为现有 `getCodeTree(repository, path)`；文件内容来源为 `getCodeFile(repository, path)`。
2. 代码显示行号、语言标识、文件路径、加载状态和最大文件限制提示；初期不提供编辑。
3. 聊天回答下方的代码引用按 `repository_name + file_path` 去重，显示文件名、相对路径、来源（实时搜索/索引/概览）和可用时的行号。
4. 点击引用卡片触发 `openCodeFile({ repositoryName, path, line })`：自动展开右侧栏、切换到文件查看、选中对应代码库、加载文件并滚动到目标行；目标行使用高亮标记。
5. Markdown 正文中的普通文本链接不自动获得文件访问权限；只有后端返回的受控 citation 元数据可以打开文件。

## 4. 运行配置模型

现有“构建配置”不能直接推导可靠的开发启动命令，因此增加**运行配置**，与构建配置独立保存。每个代码库最多配置一个前端和一个后端运行档案。

### 4.1 运行档案字段

建议实体：`AiCodeRepositoryRunProfile`，表名使用短名称 `ai_code_repo_run`。

| 字段 | 说明 |
| --- | --- |
| `Id` | 主键。 |
| `CodeRepositoryId` | 已登记代码库。 |
| `Role` | `frontend` / `backend`。同一代码库同一角色唯一。 |
| `WorkingDirectory` | 相对代码库根目录，默认 `.`。 |
| `Command` | 已保存的受控命令模板。 |
| `ArgumentsTemplate` | 参数模板，允许唯一变量 `{port}`。 |
| `PortRangeStart/End` | 角色默认范围允许覆盖。 |
| `HealthPath` | 可选 HTTP 健康检查路径，默认 `/`。 |
| `PreviewEnabled` | 前端默认 `true`，后端默认 `false`。 |
| `CreatedAt/UpdatedAt` | 审计字段。 |

运行配置由“项目与代码库”设置页维护，不在聊天中临时编辑。推荐初始模板：

| 场景 | 命令 | 参数模板 |
| --- | --- | --- |
| .NET 后端 | `dotnet` | `run --project {publish_target} --urls http://127.0.0.1:{port}` |
| npm 前端 | `npm` | `run dev -- --port {port}` |
| Umi 前端 | `npm` | `run start -- --port {port}` |

不能可靠传端口的命令必须在设置页提示用户改为支持 `{port}` 的脚本，而不是用环境变量或猜测输出文本覆盖端口。

### 4.2 命令安全规则

1. `Command` 是白名单可执行文件：初期仅 `dotnet`、系统绝对路径解析后的 `npm` / `npm.cmd`。
2. `ArgumentsTemplate` 不经过 shell；以受限参数解析器拆分后写入 `ProcessStartInfo.ArgumentList`。
3. 仅允许 `{port}` 变量，替换值必须在已分配端口范围内。
4. 拒绝重定向、管道、命令连接符、环境变量展开和相对可执行文件路径。
5. `WorkingDirectory`、项目文件和健康路径都必须经过代码库根目录边界校验。
6. 启动、停止、读取日志、预览代理和文件读取都要验证当前登录用户；任何 API 不接受任意绝对路径、PID 或 URL。

## 5. 动态端口与进程生命周期

### 5.1 端口分配

`ICodeRuntimeManager` 维护进程和端口租约。端口按角色默认范围分配：

| 角色 | 默认范围 |
| --- | --- |
| 前端 | `4300-4399` |
| 后端 | `5100-5199` |

分配算法：

1. 对 `projectId + role` 加异步锁，防止并发启动获得同一个端口。
2. 从对应范围顺序查找；端口既不在内部活动租约中，也没有被系统 TCP 监听器占用，才可预留。
3. 创建受控进程后持续检测；启动失败、进程退出或停止完成时释放租约。
4. 范围耗尽时返回明确错误，不尝试任意系统端口。

### 5.2 运行记录和重启行为

建议运行记录为内存单例 `CodeRuntimeManager`，并增加只读审计实体 `AiCodeRepositoryRunLog`（可选，二期）保存最近状态。第一期进程控制不依赖数据库恢复：服务端重启后所有旧记录标记为未知，用户需要显式重新启动；不得根据旧 PID 杀进程。

每个活动实例记录：`RunId`、`ProjectId`、`RepositoryId`、`Role`、`Pid`、`Port`、`Status`、`StartedAt`、`ExitCode`、`LastError`、滚动日志缓冲区。

停止时先请求正常退出，等待 10 秒；仍未退出才调用 `Kill(entireProcessTree: true)`，并记录强制停止原因。

## 6. 后端接口与事件协议

所有新 HTTP 入口放在 `Services/CodeRepository/CodeRuntimeAppService.cs`；进程、端口、日志与路径校验放在 `CodeRuntimeManager`，不要写进 AppService 或 `Program.cs`。

### 6.1 REST

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/api/v1/code-runtime/projects/{projectId}` | 获取项目运行档案与活动实例。 |
| `PUT` | `/api/v1/code-runtime/repositories/{repositoryName}/profiles/{role}` | 保存前端/后端运行档案。 |
| `POST` | `/api/v1/code-runtime/projects/{projectId}/start` | 启动 `frontend`、`backend` 或两者。 |
| `POST` | `/api/v1/code-runtime/runs/{runId}/stop` | 停止实例。 |
| `POST` | `/api/v1/code-runtime/runs/{runId}/restart` | 重启实例。 |
| `GET` | `/api/v1/code-runtime/runs/{runId}/logs?after={sequence}` | 补拉日志。 |
| `GET` | `/api/v1/code-runtime/runs/{runId}/preview/{**path}` | 仅对活动前端实例的同源预览代理。 |

### 6.2 WebSocket

新增 `/ws/code-runtime`，鉴权方式与现有代码库打包 WebSocket 一致。客户端首帧：

```json
{ "type": "subscribe", "run_ids": ["run-id-1"] }
```

服务端事件：

```json
{ "type": "status", "run_id": "...", "status": "starting", "port": 4300 }
{ "type": "output", "run_id": "...", "sequence": 18, "stream": "stdout", "line": "..." }
{ "type": "status", "run_id": "...", "status": "running", "preview_url": "/api/v1/code-runtime/runs/.../preview/" }
{ "type": "completed", "run_id": "...", "status": "failed", "exit_code": 1, "message": "..." }
```

## 7. 聊天引用数据补充

当前代码检索 citation 已携带 `repository_name`、`file_path` 和 `source`。为精确定位，`CodeRepositoryIndexService.ToCitation` 与实时扫描返回值新增：

```json
{
  "repository_name": "web",
  "file_path": "src/pages/index.tsx",
  "start_line": 42,
  "end_line": 64,
  "source": "code_index"
}
```

聊天流 `sources` / `done` 事件继续传递 citation 列表。前端不需要从 LLM 正文解析路径；`MarkdownMessage` 只渲染后端结构化引用，避免错误路径和越权读取。

## 8. 前端状态与组件划分

建议新增以下前端模块：

```text
front/components/chat/ChatWorkspaceToolbar.tsx
front/components/chat/ChatRightPanel.tsx
front/components/chat/RuntimePanel.tsx
front/components/chat/RuntimePreview.tsx
front/components/chat/CodeFileInspector.tsx
front/components/chat/CodeCitationCards.tsx
front/lib/code-runtime-api.ts
front/lib/code-runtime-types.ts
```

`KnowledgeChatHome` 只持有跨组件状态：

```ts
leftSidebarOpen
rightPanelOpen
rightPanelTab // "preview" | "files"
selectedRunId
selectedCodeFile // { repositoryName, path, line? } | null
```

右侧面板在桌面端占 `minmax(360px, 32vw)`；窄屏改为覆盖式抽屉。关闭后不清空已选运行项或文件，重新打开可恢复当前上下文。

## 9. 分阶段实施计划

### Phase 1：运行域后端

1. 增加运行档案 DTO、实体、CodeFirst 初始化和 `CodeRuntimeManager`。
2. 实现路径校验、命令白名单、参数模板解析、动态端口租约、生命周期与日志缓冲。
3. 提供 REST、WebSocket 和前端预览代理；为 .NET 与 npm 添加最小运行档案。
4. 为启动/停止/端口冲突/异常退出编写单元或集成验证。

### Phase 2：设置页与聊天工具栏

1. 在代码库设置页配置前端/后端运行档案并进行输入校验。
2. 在聊天顶部加入左/右栏切换与可关闭的工具菜单。
3. 接入运行状态、启动/停止/重启和终端日志。

### Phase 3：右侧预览和文件检查器

1. 实现同源 iframe 预览、地址栏、运行选择器和错误态。
2. 复用代码库 tree/read API 实现文件树、代码显示、行号和高亮定位。
3. 处理窄屏抽屉、键盘 `Esc` 关闭和状态持久化。

### Phase 4：聊天引用联动

1. 补充 citation 行号元数据。
2. 渲染可点击文件引用卡片。
3. 从聊天卡片打开文件侧栏并定位到行。
4. 完成端到端验收和错误提示整理。

## 10. 验收标准

1. 选择一个含前端与后端档案的项目后，可从聊天顶部运行菜单启动前端、后端或全部；每个服务获得不同且未占用的端口。
2. 运行中的日志实时显示；异常退出有退出码和清晰错误；停止和重启可用且不会杀死非本系统启动的进程。
3. 右侧浏览器能通过同源地址预览前端，不需要用户手工处理随机端口或 CORS。
4. 顶部左/右栏按钮、工具菜单和 `Esc` 行为正确，刷新后栏位开关状态可恢复。
5. 代码搜索产生的 citation 在聊天中展示为文件卡片；点击 `src/x.ts:42` 后，右侧文件栏打开正确代码库、正确文件并高亮 42 行。
6. 任意绝对路径、任意 shell 字符串、任意 PID、任意 URL 都不能通过 API 执行、读取或代理。
7. 现有聊天、代码检索、代码库打包和 Git 功能不回归。

## 11. 待确认但不阻塞 Phase 1 的产品项

- 默认是否同时启动所有已配置前端/后端，还是每个项目只允许一个前端和一个后端。
- 开发服务器运行在 AI 服务主机本地是否符合部署环境；若后端与浏览器跨机器，使用同源预览代理仍能工作。
- 是否需要把运行日志和历史运行记录持久化；第一期仅保留活动实例和内存环形日志。
- 预览是否需要支持需要登录的项目页面；第一期 iframe 只做网络代理，不注入认证信息。
