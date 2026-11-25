# 聊天右侧的对象模型

在聊天右侧「＋ → 本体模型」中新建项目模型，点「添加组件」，填写数据库连接或选择已登记的接口别名。数据库连接可以单独保存。需要按键查询时，展开「按键查询配置（可选）」，填写表或视图、唯一查询键和返回字段。「文本说明」组件直接保存文字，勾选后随模型注入 Prompt。测试数据库连接会使用填写的账号登录并执行常量查询。已配置按键查询的组件在保存时会额外校验表和字段的读取权限。测试 HTTP 组件会按测试键读取一条记录并核对返回字段。保存时服务端再次测试，在版本仍匹配时原子写入按用户和项目隔离的加密模型。

左侧「对象模型」列出当前项目的模型，右键模型名称或点行末的「…」可重命名、删除。删除需要确认，并移除模型中的组件与凭据。「组件树」列出当前模型和组件，点击根节点聚焦画布中的模型，点击组件聚焦对应节点并打开配置。

点击顶部「预览与注入」，勾选整份本体或个别组件。多个模型可以合选，每轮最多八个来源。填入键值并预览，可以先核对数据再插入草稿。「挂载到本轮提问」会在左侧输入框显示本体和可修改的记录键。发送时按挂载的版本重新查询，将实际来源、时间和选定字段随问题提交。留空键值只附加模型说明。查询失败时保留草稿，模型已变更时重新加载并选择。

## 数据库连接测试与项目目标

新增网络数据库时，默认可以在面板直接填写公网 IP、端口、库名和账号，点击「测试连接」，成功后保存。域名和内网数据库通过项目目标清单接入。项目显式配置 `Targets` 时，测试、保存和读取都限定为该清单中的目标。

服务端从 ASP.NET Core 配置读取 `Ontology:Projects:<项目编号>`。部署人员可以使用配置文件或环境变量维护项目清单。网络数据库使用有登录权限的数据库账号和受信任的 TLS 证书，连接密码在面板输入。SQLite 使用服务端已有数据库文件的绝对路径。HTTP GET 操作需要已登记的 HTTPS 地址和一个键值占位符，返回一个包含键和标量字段的 JSON 对象。可选 Bearer Token 通过组件面板输入。以下为项目 43 的配置示例。

```json
{
  "Ontology": {
    "Projects": {
      "43": {
        "Targets": [
          "sqlserver/db.example.invalid:1433/Catalog",
          "postgresql/pg.example.invalid:5432/catalog",
          "mysql/mysql.example.invalid:3306/catalog",
          "oracle/db.example.invalid:2484/app.example"
        ],
        "Sqlite": { "sample": "D:/project-data/example.sqlite" },
        "Http": { "records": "https://api.example.invalid/records/{key}" }
      }
    }
  }
}
```

Oracle 使用 TCPS，默认端口为 2484，可按监听器配置修改。填写服务名（Service Name）和数据库账号，使用项目目标清单时，主机、端口与服务名须与面板一致。当前支持 Oracle 常规表名与字段名，按大写解析。

服务端按登录身份和项目权限解析勾选的来源。数据库按参数化键值最多读取两条记录，用于检测重复键。HTTP 返回体上限为 64 KiB。连接凭据在服务端加密保存。Prompt 包含模型引用、来源、字段及读取时间，每个来源最多带入一个字段的 1500 个字符。

## API

项目本体前缀为 `/api/v1/ontology/{projectId}/models/{id}`。各接口校验当前登录身份、项目权限和模型版本。

| 请求 | 用途 |
| --- | --- |
| PUT /name | 按 version 和 name 重命名，返回新版本 |
| DELETE ?version={version} | 删除指定版本的模型 |
| POST /sources/test | 接收 version、source、password，测试成功返回 checkedAt |
| PUT /sources | 接收同一请求结构，重新测试、保存并返回新版本模型 |
| POST /lookup | 接收 version、sourceIds、key，返回可预览的 text 和 version |
| DELETE /sources/{sourceId}?version={version} | 从模型移除组件及其凭据 |
