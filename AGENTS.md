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

## 必须遵守的规则

1. 不读取、输出、提交或覆盖真实密钥、Token、数据库连接串和用户本地数据。
2. `backed/appsettings.json`、`backed/data/`、`front/.env*`、`node_modules`、`bin`、`obj`、`.next` 是本地内容，不提交。
3. 新增后端 API 时同步更新 DTO、前端 API 客户端和 TypeScript 类型。
4. 保留现有文件编码；涉及中文字符串或旧文件时先检查 BOM/编码，再做最小编辑。
5. Controller/Dynamic API 只承载 HTTP 协议；业务、路径校验、文件读写和外部进程控制放在 Service。
6. 前端组件不散落直接 `fetch`；接口封装在 `front/lib/*-api.ts`，类型放在 `front/lib/*-types.ts`。

## 后端约定

- 聊天图片附件必须先以不透明附件 ID 上传到后端受控临时目录；校验真实图片签名、大小、数量和当前用户归属后，才可转换为 Codex app-server 的 `localImage` 输入。发送后应将图片迁移到按用户和会话隔离的历史目录，并在聊天消息元数据保存不含真实路径的附件信息。不得接受浏览器提供的本地路径、将路径拼入 shell，也不得假定第三方 CLI 兼容该协议。

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

- Next.js App Router，默认开发端口 `3782`，监听 `0.0.0.0`。
- 后端地址由 `NEXT_PUBLIC_AIAGENT_API_BASE_URL` 控制；`next.config.js` 负责 `/api/*` rewrite。
- 页面保持轻量；复杂状态放在 Provider、hook 或领域组件中。
- 长列表、终端、聊天内容必须拥有独立滚动容器，不能撑破工作台布局。
- 看板工作台相关代码在 `components/dashboard-applications/` 与 `lib/dashboard-application-api.ts`。

## 看板 Agent 规则

看板 AI 只操作 `dashboard_application_id` 指向的唯一工作区；绑定 Git 仓库仅用于 Git 管理，不作为同轮 AI 搜索/写入范围。

修改现有看板文件必须遵循：

```text
inspect_dashboard_workspace
→ search_dashboard_code
→ read_dashboard_file
→ apply_dashboard_patch (SHA-256)
→ validate_dashboard_change
```

- 先读取再修改，禁止猜测路径。
- 默认不能创建新文件；不要生成没有被入口引用的 `App.jsx`、组件或样式文件。
- 多处联动的视觉修改可以对同一“已读取且 SHA 一致”的文件执行完整替换；否则使用最小精确替换。
- 只有收到 `dashboard_change_applied` 后才能声称已写入文件；验证失败必须明确说明，不可假装成功。
- 文件写后刷新树、打开文件和预览 iframe。

详见：

- `docs/dashboard-ai-editing-reliability-plan.md`
- `docs/dashboard-ai-editing-implementation-log.md`

## Git 规则

- 提交信息使用简洁中文或 Conventional Commit，例如 `feat: 增加看板工作区快照`。
- 推送前检查暂存列表，确认不存在本地配置、运行时数据、知识库原文、依赖目录和构建输出。
- 不使用破坏性 Git 命令（如 `reset --hard`）处理未知改动。
- Git 拉取/推送的命令输出应回传给用户；认证失败时不要输出凭据。

## 文档更新

新增或显著调整能力时同步更新：

- `README.md`：面向使用者的能力、启动和安全说明。
- `AGENTS.md`：面向开发者与 Agent 的边界、契约和改码流程。
- `docs/`：需要保留设计理由、实现记录或验收说明时新增专题文档。
