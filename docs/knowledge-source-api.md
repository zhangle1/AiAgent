# 知识库来源层 API

## 目的

知识库现在分成两层：

1. **来源层（raw source）**：上传文件按原始字节保存，文件哈希和原始文件名写入 `ai_knowledge_document`，可下载、可追溯，不能被提炼结果覆盖。
2. **知识表示层（wiki/artifact）**：按需从来源层解析和提炼，失败或取消不会改写来源文件。

这对应 LLM Wiki 的“来源事实先入库，再生成可检索表示”的思路。上传完成后，即使模型未配置，也可以验证来源层是否入库并执行文本检索。

## 接口

### 上传原始文件

```http
POST /api/v1/knowledge/{kbName}/upload
Content-Type: multipart/form-data

Files=<file1>&Files=<file2>
```

接口只保存到 `raw`，不会隐式调用模型或创建向量索引。保存采用临时文件 + 原子移动，上传被取消时不会留下半个文件。

### 下载原始文件

```http
GET /api/v1/knowledge/{kbName}/documents/{documentId}/file
GET /api/v1/knowledge/{kbName}/documents/{documentId}/file?download=1
```

### 读取解析结果和提炼草稿

```http
GET /api/v1/knowledge/{kbName}/documents/{documentId}/content
```

该接口返回最新解析文本和知识草稿，不返回服务器本地路径。

### 来源层检索

```http
POST /api/v1/knowledge/{kbName}/sources/search
Content-Type: application/json

{ "query": "订单取消", "top_k": 5 }
```

检索顺序是：最新解析文本（如果存在）→ 可直接读取的文本类原文件 → 原始文件名。PDF、Office 等二进制文件在完成解析后即可参与内容检索；未解析时仍可通过文件名命中。返回格式兼容普通知识检索响应，`provider` 为 `raw-source`，每个 citation 的 metadata 含 `document_id`、`file_hash`、`raw_file_url`，方便交付侧保留证据链。

## 取消与切换

切换知识库或文档时，前端会取消旧的 Office 预览请求。后端将该请求返回为 HTTP 499，而不是 500；这不是文档损坏。提炼队列仍由独立后台 token 控制，页面切换不会取消已经排队的提炼任务，只有显式点击“取消”才会终止。

真正的模型、数据库或文件格式错误会保留在文档和任务消息中（截断到 500 字符），便于定位，而不是只显示“请检查配置”。
