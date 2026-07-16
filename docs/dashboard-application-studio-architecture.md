# 看板应用工作台：架构与实时协作边界

## 当前能力

看板应用绑定已注册的服务器代码库目录，在独立全屏工作台中完成以下工作：

- 顶部左侧返回应用列表，右侧固定放置“导入 HTML”和“运行预览”。
- 左侧展示受限服务器工作区的目录和文件；左右侧栏均可拖拽改变宽度。
- 中间可编辑文本文件，HTML 可直接使用服务器文件预览；未保存草稿则使用 `iframe srcDoc` 立即预览。
- 上传 `.html`/`.htm` 后，服务器把原文件和一个 React JSX 包装组件写入 `aiagent-dashboard-preview/`，文件树立即刷新。
- 右侧使用首页相同的已配置 LLM 模型目录，可在发送前选择模型；聊天面板提供输入、输出、调用次数和总 Token 统计。
- 聊天优先连接 `/api/v1/chat/ws`，连接不成功时自动使用现有 SSE 流；工具调用、Agent 循环和写文件结果都会显示在活动和终端面板中。
- 活动面板允许设置长期工作目标。该目标会随每次请求一起传给 Agent；服务器目录额外每 5 秒轮询一次，用于显示 Agent 或其他操作者的文件变动。

## 布局

```text
顶层工具栏：返回应用列表                         导入 HTML | 运行预览
┌─左侧（可拖拽）─┬─中央编辑/预览────────────────┬─右侧（可拖拽）──────────┐
│ 活动栏          │ 预览 / 文件编辑              │ 聊天 | 活动 | 实时输出   │
│ 服务器文件树    │ HTML iframe / 文本编辑器     │ 模型选择、Token、调用数  │
│ 5 秒轮询        │                              │ 任务目标、Agent 轨迹     │
└────────────────┴──────────────────────────────┴────────────────────────┘
```

工作台路由会跳过普通业务侧边栏，避免 IDE 区域被固定导航占用；返回入口只出现在顶部工具栏。

## API 与安全边界

后端入口位于 `Services/DashboardApp/DashboardApplicationAppService.cs`，统一挂在 `/api/v1/dashboard-applications`：

| 接口 | 用途 |
| --- | --- |
| `GET {id}/tree` | 枚举服务器工作区目录和文件；二进制/不支持文本文件仍可展示为只读。 |
| `GET {id}/file` | 读取受支持、最大 1 MB 的文本文件。 |
| `PUT {id}/file` | 使用临时文件替换的原子方式保存文本文件。 |
| `POST {id}/upload-html` | 上传最大 2 MB 的 HTML，生成同名 `.jsx` React 包装组件。 |
| `GET {id}/preview/{path}` | 以受限静态资源形式返回 HTML/CSS/JS/图片等预览资产。 |

所有路径先按应用根目录标准化，再验证没有逃逸根目录；`.git`、`node_modules`、`bin`、`obj`、`.next` 等目录不能读取或写入。上传文件名经过字符净化，且写入目录固定为 `aiagent-dashboard-preview`。HTML 包装组件只把上传 HTML 序列化到 `dangerouslySetInnerHTML`，用于在宿主 React 项目中继续改造，不会执行服务器命令。

## AI 写文件与实时信息

聊天请求包含 `dashboard_application_id`、当前文件、绑定代码库、已选模型和可选长期目标。Agent 获得受限工具 `write_dashboard_file(path, content)`；工具复用同一套路径、扩展名、大小和原子写入保护。前端收到 `dashboard_file_written:` 工具结果后会刷新文件树、已打开文件和预览。

`AgentRunStats` 在每个实时事件的 metadata 中暴露 `prompt_tokens`、`completion_tokens`、`total_tokens`、LLM/tool 调用次数和耗时。当前服务端 Token 数据来自 Agent 的文本估算；若模型提供商返回原始 usage，可在 `LlmChatClient` 中补充该值并沿用相同 metadata 字段，前端无需变更。

WebSocket 是首选实时传输。SSE 只是网络代理不支持 WebSocket 升级时的兼容通道，界面会在状态/终端区域明确显示实时事件与降级结果，避免把静态请求误报为实时连接。

## 验收清单

- 进入应用后，返回按钮在最顶层，运行预览按钮在顶部右侧。
- 拖动两条分隔线可以调整文件树与右侧聊天面板宽度。
- 展开文件夹能看到服务器目录中的文件；只读资源不会被当成文本打开。
- 上传 HTML 后，树中出现 `aiagent-dashboard-preview/*.html` 和同名 `.jsx`，HTML 能预览。
- 选择模型、设置长期目标并发送需求后，右侧能持续显示 Agent/工具信息与 Token/调用统计；如果 WebSocket 被反向代理阻断，仍能通过 SSE 收到相同流事件。
