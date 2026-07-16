# 看板 AI 精准改码实施记录

## 目标

将看板 AI 的改码流程从“模型猜文件路径并整文件覆盖”改为“锁定当前工作区、识别入口、搜索与读取真实文件、基于版本做最小补丁、静态验证、结构化刷新”。

## 实施约束

- 只允许操作 `dashboard_application_id` 对应的工作区。
- 关联 Git 仓库只承担 Git 管理，不作为看板 AI 的第二检索/写入范围。
- 写入既有文件前必须读取，并携带 SHA-256 版本。
- 默认不创建新文件。
- 本次按要求不启动调试服务、不运行编译、构建或自动化测试。

## 2026-07-15：开始实施

### 已确认的改造顺序

1. 后端新增工作区快照、入口识别和当前工作区搜索。
2. 后端新增受 SHA-256 保护的文本补丁与静态变更验证工具。
3. Agent 对看板请求使用专用工具策略，禁用直接整文件写入。
4. 前端发送结构化的当前文件/工作区版本，并按结构化结果刷新文件树、编辑器和预览。

### 当前状态

- 已完成：后端工作区快照与专用工具。

## 2026-07-15：P0 工作区定位与受控写入

### 1. 唯一工作区

- `AgentContext.FromRequest` 在存在 `dashboard_application_id` 时强制清空 `CodeRepositoryNames`。
- 看板聊天不再把关联 Git 仓库传入通用代码检索工具；Git 关联仍保留给拉取、提交、推送功能。
- 前端改为发送结构化字段：`dashboard_file_path` 与 `dashboard_workspace_revision`，不再把当前文件和写入规则拼接进自然语言消息。

### 2. 工作区快照与搜索

- 新增 `GET /api/v1/dashboard-applications/{id}/inspect`。
- 快照会枚举受限工作区的可见文件、框架类型、入口、源码、样式、局部 import、图表候选和工作区版本。
- 新增 `GET /api/v1/dashboard-applications/{id}/search`；只检索当前工作区，不依赖已建立的代码库索引。
- 工作台启动时获取快照，优先打开识别到的源码入口，并在右侧显示工作区、当前文件、入口和版本摘要。

### 3. 版本化补丁

- 读取文件返回 `sha256`。
- 新增 `POST {id}/file/patch`：补丁需提供已读取版本、精确查找片段和替换片段；版本不一致或匹配不唯一时拒绝写入。
- 新增 `POST {id}/file/validate`：静态检查目标是否为已识别的源码/样式、本地 import 是否可解析，以及可选目标片段是否存在。
- 看板 Agent 的 `write_dashboard_file` 被服务端禁用，避免整文件覆盖或把猜测路径写成幽灵文件。

### 4. Agent 工具流程

- 新增：`inspect_dashboard_workspace`、`search_dashboard_code`、`apply_dashboard_patch`、`validate_dashboard_change`。
- `AgentLoop` 对每个看板会话自动先执行工作区检查。
- `read_dashboard_file` 要求检查已完成；`apply_dashboard_patch` 要求目标文件在本轮已经读取。
- Prompt 明确禁止看板会话使用通用代码库工具，改码顺序固定为：检查 → 搜索 → 读取 → 补丁 → 验证。

### 5. 前端刷新闭环

- 工具返回 `dashboard_change_applied:<path>` / `dashboard_change_validated:<path>`。
- 工作台收到结构化变更事件后刷新文件树、对应打开文件、工作区快照，并通过 iframe key 触发预览重新加载。

## 本轮未执行项

- 未启动前端或后端。
- 未执行 `dotnet build`、`npm run build`、测试、安装依赖或调试验证。
- 未实现“显式确认后新增文件”；当前策略是默认禁止 AI 在看板工作区内新建猜测文件。

## 2026-07-15：兼容性修正

- 将本地 import 校验中的 C# collection expression 改为传统 `new[] { ... }` 数组写法，以兼容当前项目配置的 C# 语言版本。

## 2026-07-15：Agent 读取后未提交补丁修正

### 观察到的轨迹

- 工作区快照、`src/main.jsx` 和 `src/styles.css` 都已成功读取。
- 模型没有提交 `apply_dashboard_patch`，随后消耗完原本的 5 个循环；后端错误地把完整原始工具证据作为兜底回答显示给用户。

### 修正措施

- 看板 Agent 循环上限调整为 8 次，普通知识问答仍保持 5 次。
- 看板改码轮次禁止 `THINK` 与空 `TOOL` 停止；读取所需文件后必须补丁、验证或明确结束。
- `apply_dashboard_patch` 继续支持单处精确替换，同时增加 `content` 参数：当一个视觉请求必须同时调整数据、ECharts series 和 JSX 布局时，可在同一已读取文件、同一 SHA-256 版本下进行完整替换。
- 工具轮次耗尽时，看板不再回显原始 JSON 证据；会明确说明“未写入”或“已写入但未生成总结”。
