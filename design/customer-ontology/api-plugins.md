# 本体 API 与插件契约 v1

路径前缀 /api/v1/ontology/{projectId}，沿用网页登录，逐次验证项目访问权限。无匿名调用，无令牌写入提示词。

| 请求 | 用途 |
| --- | --- |
| GET /plugins | 可用节点插件 |
| GET /models | 当前用户模型摘要 |
| POST /models | 创建模型，body: name |
| GET /models/{id} | 配置（无密码）和图 |
| PUT /models/{id}/connection | connection、password、version |
| PUT /models/{id}/graph | graph、version，页面和 LLM 共用 |
| GET /models/{id}/context | 脱敏上下文 |

图结构为 {nodes:[{id,kind,label}],edges:[{source,target}]}。最多 40 节点及 100 边；kind 来自插件目录，拒绝重复 ID、重复边、未知引用及循环。模型版本每次变更递增。插件仅定义节点类型，API 不执行脚本或 SQL。

LLM 必须使用宿主提供的已认证 HTTP 能力；告知接口路径不会自动赋予权限，不改写 Codex 配置或接管原生调度。

connection 更新可同时携带 graph，原子校验和保存连接及流程，任一校验失败不写入。纯 graph API 不接受连接变更。当前单实例文件锁；不支持多后端同时写入。
