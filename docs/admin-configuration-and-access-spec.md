# 管理配置、项目权限与审计规格

## 目标

为内部部署提供一个唯一的初始化管理员和可扩展的管理员角色。管理员可以创建账号、分配聊天可选项目、只读审计其他用户会话，并从全员维度查看 Token 消耗。

## 身份与初始数据

- 启动 CodeFirst 后，服务确保 `superadmin` 存在并具备 `admin` 角色。
- 默认密码仅用于首次创建；如果该账号已存在，绝不覆盖密码。
- `/api/v1/auth/register` 固定返回 403，前端 `/register` 重定向至登录页。
- 管理权限取自服务端 `AiUser.Role`，不可由请求参数或前端状态授予。

## 项目可见性

普通用户与代码项目通过 `ai_user_code_project` 多对多表关联；管理员拥有全部未删除项目。项目范围在两个边界生效：

1. `GET /api/v1/code-repositories/projects` 只返回该用户可选项目；
2. 创建或更新会话、保存项目偏好时再次验证项目访问权，且代码库名称必须属于当前项目，防止绕过前端直接提交项目 ID 或代码库名称。

## 管理 API

| API | 用途 |
| --- | --- |
| `GET/POST /api/v1/admin/users` | 查询与创建用户 |
| `PUT /api/v1/admin/users/{id}/projects` | 替换普通用户的项目授权 |
| `GET /api/v1/admin/sessions` | 按用户筛选并查看会话摘要 |
| `GET /api/v1/admin/users/{id}/sessions/{sessionId}` | 只读会话消息 |
| `GET /api/v1/admin/usage` | 根据 day/week/month/year 与用户筛选聚合使用量 |

所有上述接口在控制器和服务层都会检查 `IsAdministrator`。使用量基于 `ai_usage_record` 的追加账本，第三方代理未提供 usage 时仍保留估算标记供后续适配器替换。
