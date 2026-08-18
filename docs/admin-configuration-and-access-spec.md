# 管理配置、项目权限与审计规格

## 目标

为内部部署提供一个唯一的初始化管理员和可扩展的管理员角色。管理员可以创建账号、分配聊天可选项目、只读审计其他用户会话，并从全员维度查看 Token 消耗。

## 身份与初始数据

- 启动 CodeFirst 后，服务先检查是否已有未禁用的 `admin` 账号；有则不执行初始化。
- 首次部署必须显式配置 `Authentication:InitialAdministratorUsername` 与 `Authentication:InitialAdministratorPassword`。初始化口令至少 16 个字符且没有默认值；缺少任一配置时启动失败，不创建管理员。
- 若使用环境变量，对应名称为 `Authentication__InitialAdministratorUsername` 与 `Authentication__InitialAdministratorPassword`；初始化口令只能通过受控环境或 Secret Store 注入，不得放入命令行、日志或版本库。
- 配置的账号必须尚未存在。创建成功后应立即从配置文件和环境变量/Secret Store 中移除初始化口令；升级、重启和后续初始化检查不会重置任何已有用户或管理员密码。
- `/api/v1/auth/register` 固定返回 403，前端 `/register` 重定向至登录页。
- 管理权限取自服务端 `AiUser.Role`，不可由请求参数或前端状态授予。
- 用户可选填最长 64 个字符的别名；管理员可按账号或别名搜索。密码重置会重新生成密码哈希并撤销该用户的所有有效登录会话。

## 项目可见性

普通用户与代码项目通过 `ai_user_code_project` 多对多表关联；管理员拥有全部未删除项目。项目范围在两个边界生效：

1. `GET /api/v1/code-repositories/projects` 只返回该用户可选项目；
2. 创建或更新会话、保存项目偏好时再次验证项目访问权，且代码库名称必须属于当前项目，防止绕过前端直接提交项目 ID 或代码库名称。

## 管理 API

| API | 用途 |
| --- | --- |
| `GET/POST /api/v1/admin/users` | 查询与创建用户 |
| `PUT /api/v1/admin/users/{id}/alias` | 设置或清空用户别名 |
| `POST /api/v1/admin/users/{id}/reset-password` | 重置密码并使旧会话失效 |
| `PUT /api/v1/admin/users/{id}/projects` | 替换普通用户的项目授权 |
| `GET /api/v1/admin/sessions` | 按用户筛选并查看会话摘要 |
| `GET /api/v1/admin/users/{id}/sessions/{sessionId}` | 只读会话消息 |
| `GET /api/v1/admin/usage` | 根据 day/week/month/year 与用户筛选聚合使用量 |

所有上述接口在控制器和服务层都会检查 `IsAdministrator`。使用量基于 `ai_usage_record` 的追加账本，第三方代理未提供 usage 时仍保留估算标记供后续适配器替换。
