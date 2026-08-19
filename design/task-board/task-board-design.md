# AiAgent 任务面板设计

## 目标

任务面板位于左侧“聊天”下方，以 AiAgent 代码项目为组织单位管理用户创建的本地任务。创建任务时可选关联一条已筛选的 Gitee Issue，但不会批量同步或导入 Gitee Issue。每张任务卡的一键处理会打开对应项目的新聊天会话，并把任务内容写入浏览器本地的单次交接数据；聊天页读取后预填输入框，不把令牌、浏览器路径或未验证外部内容当作系统指令。

## 信息架构

```text
任务面板
├── 项目筛选（全部 / 单项目）
├── 新建本地任务（AiAgent 项目、标题、说明）
├── 关联 Gitee Issue（可选）
│   ├── Gitee 项目、人员、状态、关键词筛选
│   └── 分页浏览后明确选择一条 Issue
└── 任务卡
    ├── 本地状态、关联的 Gitee 标识与原始链接
    ├── Gitee 负责人和最近更新时间（如已关联）
    └── 处理 → /chat?project={id}&template_handoff={nonce}
```

## 安全与数据边界

- Gitee 访问令牌复用 `ai_git_account` 中数据保护加密的令牌；从不返回给浏览器或写入任务表。
- Gitee 项目、成员和 Issue 仅由后端使用已加密令牌分页读取；浏览结果不写入任务表。
- 只有用户明确选择一条 Issue 并保存本地任务时，才保存关联标识、链接、负责人和更新时间。
- 外部 Issue 的正文被视为不可信任务材料。进入聊天时仅作为用户消息内容，不改变工具权限、模型配置或 Codex sandbox。
- 任务归属到创建者与代码项目；后端每次读写均验证用户拥有该项目访问权限。

## 已实现 API

| 操作 | API |
| --- | --- |
| 列表 | `GET /api/v1/project-tasks` |
| 新建 | `POST /api/v1/project-tasks` |
| Gitee 项目 | `GET /api/v1/project-tasks/gitee/projects` |
| Gitee 成员 | `GET /api/v1/project-tasks/gitee/members` |
| Gitee Issue | `GET /api/v1/project-tasks/gitee/issues` |

Gitee 筛选接口不提供全量同步能力。新建任务必须选择 AiAgent 项目；关联 Gitee Issue 是可选且一次只关联一条的用户动作。
