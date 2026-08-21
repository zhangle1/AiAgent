# AiAgent 任务面板设计

## 目标

任务面板位于左侧“聊天”下方，以 AiAgent 代码项目为组织单位展示已关联的企业工作项和创建的本地任务。企业固定为 `yun_kun`；用户从新建或导入窗口打开大尺寸工作项选择器，查看详情、图片和文件附件后，再明确选择关联的 AiAgent 项目。每张工作项可一键打开对应项目的新聊天会话，并把任务内容写入浏览器本地的单次交接数据。聊天页读取后预填输入框，不把令牌、浏览器路径或未验证外部内容当作系统指令。

## 信息架构

```text
任务面板
├── 项目筛选（全部 / 单项目）
├── 工作项筛选（负责人、类型、状态、关键词）
├── 新建本地任务（AiAgent 项目、标题、说明）
│   └── 从 Gitee 企业工作项选择
├── CSV 导入（目标 AiAgent 项目、文件、字段映射）
│   └── 从 Gitee 企业工作项选择
├── 企业工作项选择器（固定 yun_kun）
│   ├── 状态、关键词和分页列表
│   ├── 任务详情、正文图片和文件附件预览
│   └── 明确关联到选定的 AiAgent 项目
└── 工作项表格
    ├── 工作项 ID、标题、项目、负责人、类型、状态、更新时间
    ├── 按工作项 ID 在同一用户、项目内插入或更新
    └── 处理 → /chat?project={id}&template_handoff={nonce}
```

## 安全与数据边界

- 企业列表调用固定路径 `GET /v5/enterprises/yun_kun/issues`，详情调用 `GET /v5/enterprises/yun_kun/issues/{number}`；当前用户的加密 Gitee 令牌仅在服务端出站请求时使用，绝不返回浏览器或写入工作项表。令牌可通过 Git 管理中的 Gitee OAuth 授权码流程获取，也可保留手工令牌方式；OAuth 的 ClientId、ClientSecret 和回调地址只从服务端 `GiteeOAuth` 配置读取。
- 选择器每页读取 20 条；仅在用户确认“关联此工作项”时，服务端读取详情并写入 `ai_project_task`。
- 关联和 CSV 导入后的工作项都以 `(UserId, CodeProjectId, WorkItemId)` 去重并更新非空字段。
- Gitee 返回的 `attachments`、`attach_files`、`files` 与正文 Markdown 图片链接会被解析；选择器通过受限的 Gitee HTTPS 附件代理预览或下载，单个附件上限为 20 MB，不持久化附件文件。
- CSV 只接受 UTF-8（可带 BOM），大小限制为 100 MB，且必须映射工作项 ID；导入文件在请求内读取，不持久化保存。
- 以 `(UserId, CodeProjectId, WorkItemId)` 作为工作项的去重范围；同一 ID 再次导入只更新非空字段。
- 工作项正文被视为不可信任务材料。进入聊天时仅作为用户消息内容，不改变工具权限、模型配置或 Codex sandbox。
- 任务归属到创建者与代码项目；后端每次读写均验证用户拥有该项目访问权限。

## 已实现 API

| 操作 | API |
| --- | --- |
| 列表 | `GET /api/v1/project-tasks` |
| 新建 | `POST /api/v1/project-tasks` |
| 企业工作项列表 | `GET /api/v1/project-tasks/gitee/enterprise/issues` |
| 企业工作项详情 | `GET /api/v1/project-tasks/gitee/enterprise/issues/{number}` |
| 企业工作项附件 | `GET /api/v1/project-tasks/gitee/enterprise/attachment?url={giteeHttpsUrl}` |
| 关联企业工作项 | `POST /api/v1/project-tasks/gitee/enterprise/link` |
| 导入 CSV | `POST /api/v1/project-tasks/import` |
| 删除 | `DELETE /api/v1/project-tasks/{taskId}` |

工作画布批量创建草稿会话使用 `POST /api/v1/project-tasks/chat-sessions`，请求体为 `task_ids`。服务端会再次验证任务及关联项目权限，创建不含消息的会话，并只保存受控图片附件的不透明 ID；不会返回 Gitee 令牌、附件物理路径或项目配置。

Gitee OAuth 授权入口为 `GET /api/v1/gitee-oauth/authorize`，回调为 `GET /api/v1/gitee-oauth/callback`；入口要求先登录 AiAgent，回调使用一次性 state 校验后把令牌加密保存到当前用户的 Git 账号。

无法显示工作项时，依次检查：当前用户是否在 Git 管理中配置并启用具备 `yun_kun` 企业 Issue 读取权限的 Gitee 账户、是否被状态或关键词筛选隐藏，以及任务服务返回的 Gitee HTTP 状态。若详情中未显示附件，检查 Gitee 是否在该工作项响应中提供 HTTPS 附件地址；CSV 导入仍可作为离线兜底。
