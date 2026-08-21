# AiAgent 原型设计工作台方案

## 结论

建议不要新建一套孤立的“Claude Design 仿制品”，而是在现有 **看板应用工作台（Dashboard Studio）** 上增加“原型设计模式”。AiAgent 已有工作区文件、AI 写文件、流式对话、HTML 预览、文本编辑、运行时和 Git 等底座；产品层主要补齐版本、分享、导出和页面内编辑。

本目录的 `prototype-studio.html` 是可交互概念原型：支持模拟流式对话、设备预览、页面内文字编辑、版本切换、分享设置，并可真实下载当前示例页面为单文件 HTML。它不调用后端，不代表分享权限和版本持久化已经实现。

## 用户主流程

```text
创建原型 → 对话描述需求 → Agent 写入受控工作区
        → 文件事件触发预览刷新 → 用户对话迭代 / 页面内编辑
        → 形成不可变版本 → 发布分享链接 / 下载单文件 HTML
        → 访客查看 → 获准时创建个人副本继续编辑
```

工作台采用三栏结构：左侧是设计对话，中间是实时页面预览，右侧是版本、运行活动和交付检查。顶部只保留两个最终动作：下载 HTML、分享。

## 与现有代码的关系

| 能力 | 现有证据 | 处理建议 |
| --- | --- | --- |
| AI 实时对话 | `front/lib/chat-api.ts` 已实现 WebSocket 优先、SSE 回退 | 直接复用事件 reducer；增加原型版本和预览构建事件 |
| AI 修改文件 | Dashboard Agent 已能通过受限工具写工作区文件 | 继续使用相同路径校验、扩展名白名单和原子写入 |
| 实时预览 | `DashboardStudio.tsx` 已有运行时 iframe、HTML `srcDoc` 草稿预览 | 静态 HTML 用受控同源预览；Vite 项目继续使用运行时代理/HMR |
| 源码编辑 | 已有文件树、标签页、草稿自动保存 | 保留“代码”高级入口，默认界面转为设计对话 + 预览 |
| 导入 HTML | Dashboard 服务已有 HTML 导入和 JSX 包装思路 | 作为创建原型入口之一，不作为发布格式 |
| 分享、版本、单文件导出 | 当前未发现完整领域模型和 API | 新增独立后端服务，不把逻辑堆入 Controller/Dynamic API |

## 功能边界

### 1. 对话与实时预览

- 每轮设计请求关联 `prototype_id`、`base_version_id` 和当前选中元素的稳定 `data-ai-id`，避免只靠自然语言猜目标。
- Agent 文件写入成功后发送 `prototype_file_changed`，构建完成后发送 `prototype_preview_ready`；前端收到后再切换 iframe URL，避免展示半写入状态。
- 预览使用带 `revision` 的 URL，而不是每 2.5 秒无条件刷新。编辑中的 DOM 不能被后台轮询覆盖。
- 单文件 HTML 可用 `iframe srcDoc` 快速预览；含依赖的项目走现有受控运行时。

### 2. 页面内编辑

P0 只支持低风险属性：文本、链接、图片附件 ID、颜色 token、间距和显隐。不直接把任意 DOM 序列化覆盖源码。

```text
点击元素 → iframe postMessage({ elementId, rect, editableProps })
修改属性 → PATCH prototype element mutation
         → 服务端 AST/结构化补丁更新源文件
         → 创建草稿 revision → 构建 → iframe 原子切换
```

预览 iframe 必须使用 `sandbox`。父子页 `postMessage` 同时校验 `origin`、消息 schema、原型 ID 和一次性编辑会话 ID。用户脚本不能访问 AiAgent 主站 cookie、API 或父页面 DOM。

访客的“编辑”默认创建 fork/个人副本，不能直接修改发布者工作区。团队共同编辑应后置到具备角色、锁或冲突合并之后。

### 3. 版本

- 草稿 revision：频繁生成，可合并、可清理。
- 命名版本：用户确认或发布时创建，不可变。
- 发布版本：分享链接固定指向某一版本；更新发布必须显式操作。
- 每个版本保存 manifest、文件内容哈希、入口文件、构建产物引用、创建来源（AI / 页面编辑 / 源码编辑）和父版本。
- 版本创建使用乐观并发：客户端提交 `base_version_id`；不匹配时提示比较/重放，不能静默覆盖。

### 4. 分享与下载

分享权限建议为：仅本人、组织内持链接可看、公开持链接可看。独立控制 `can_download`、`can_fork`，不提供匿名直接覆盖源项目。

- 分享令牌使用高熵随机值，数据库只存哈希，可撤销、可过期。
- 分享页从只读发布快照提供资源，不暴露服务器真实路径、仓库信息、模型配置或聊天记录。
- 响应设置严格 CSP，禁止任意网络请求；外部资源需先代理、校验并归档，或在导出时明确报告无法内联项。
- 下载采用服务端流式生成。纯静态原型输出单文件 `.html`；存在无法安全内联的资源时输出 `.zip`，不要伪装成单 HTML。
- HTML 导出前移除编辑桥、内部 ID、调试信息和令牌；脚本按产品策略选择禁用或保留。

## 建议数据模型

新增字段遵守项目约定，迁移期显式使用 `[SugarColumn(IsNullable = true)]`。

| 实体 | 核心字段 |
| --- | --- |
| `PrototypeProject` | Id, UserId, DashboardApplicationId, Name, Status, CurrentDraftVersionId |
| `PrototypeVersion` | Id, PrototypeId, ParentVersionId, Revision, ManifestJson, ContentHash, SourceType, CreatedAt |
| `PrototypeShare` | Id, PrototypeId, VersionId, TokenHash, Scope, CanDownload, CanFork, ExpiresAt, RevokedAt |
| `PrototypeExportJob` | Id, PrototypeId, VersionId, Format, Status, ArtifactId, ErrorCode |

构建产物和附件由不透明 `ArtifactId` 引用，不在 DTO、消息元数据或日志中返回真实文件路径。

## API 草案

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `POST` | `/api/v1/prototypes` | 从空白、模板或已导入 HTML 创建原型 |
| `GET` | `/api/v1/prototypes/{id}` | 读取工作台状态和当前版本 |
| `POST` | `/api/v1/prototypes/{id}/versions` | 由当前草稿创建不可变版本 |
| `GET` | `/api/v1/prototypes/{id}/versions` | 版本历史 |
| `POST` | `/api/v1/prototypes/{id}/element-mutations` | 提交受限页面属性修改 |
| `POST` | `/api/v1/prototypes/{id}/shares` | 创建分享令牌和权限 |
| `DELETE` | `/api/v1/prototypes/{id}/shares/{shareId}` | 撤销分享 |
| `GET` | `/p/{token}` | 独立只读分享壳 |
| `POST` | `/api/v1/prototypes/{id}/exports` | 创建 HTML/ZIP 导出 |
| `GET` | `/api/v1/prototype-exports/{jobId}/download` | 鉴权下载产物 |

DTO、`front/lib/prototype-types.ts` 和 `front/lib/prototype-api.ts` 应同步新增；页面组件放在 `front/components/prototype-studio/`。业务、路径验证、AST 修改、打包与文件操作放在 `backed/Services/Prototype/`，HTTP 入口只做协议适配。

## 实施顺序

1. **P0，约 5–8 人日**：在 Dashboard Studio 增加设计布局、对话驱动刷新、静态 HTML 版本快照、桌面/手机预览、鉴权下载。
2. **P1，约 6–10 人日**：分享令牌、过期/撤销、只读分享壳、fork、导出检查报告。
3. **P2，约 8–15 人日**：稳定元素 ID、受限页面内属性编辑、AST 补丁、冲突提示和版本比较。
4. **P3**：评论、团队协作、设计系统/组件库上下文、截图或视觉模型反馈。

不建议 P0 就实现任意拖拽式设计器或多人实时 DOM 合并；这两项会显著扩大编辑模型、安全隔离和冲突处理范围。

## 验收重点

- Agent 写入期间不展示半成品；一次构建只对应一个 revision。
- 连续对话、页面内编辑和源码编辑均能形成可追踪版本，旧版本可稳定预览。
- 分享令牌撤销/过期后立即失效；越权用户无法读取原型、版本和导出物。
- 访客编辑只产生副本，不改变发布版本。
- 下载文件脱离 AiAgent 后可打开，且不包含内部 API、绝对路径、令牌和编辑桥。
- 恶意 HTML 不能读取主站身份信息、调用授权 API、逃逸工作区或访问内网地址。
- 手机、平板、桌面三种视口均有独立滚动容器，不撑破工作台布局。

## 原型使用

直接用浏览器打开 `prototype-studio.html`。建议依次体验：发送修改需求、切换设备、进入“页面编辑”修改文案、切换版本、打开分享弹窗、下载 HTML。
