# 聊天知识目录 Scope

## 目的

把知识库中的一个或多个资源文件夹作为聊天本轮的可选检索范围，支持书籍、规范、Wiki、示例资料等增量补充。Scope 是目录范围，不是一本书，也不替代现有知识库名称。

## 兼容边界

- `knowledge_scope_uri` 为空：沿用原有知识库全范围检索。
- `agent=codex`：聊天编排器仍优先进入现有 Codex CLI 分支，Scope 不参与 CLI 参数、工作区或工具权限决策。
- Scope 只影响非 Codex 知识检索：Wiki 页面按来源文档过滤，RAG chunk 按 `document_id` 过滤。
- Scope 解析失败或目录无权访问时，本轮返回明确错误，不扩大到全库检索。
- 代码修改、补丁应用、测试运行、提交和推送继续使用原有显式流程。

## 请求字段

```json
{
  "knowledge_base_names": ["shared-books"],
  "knowledge_scope_uri": "viking://resources/books/refactoring/",
  "code_project_id": 12,
  "agent": "codex"
}
```

前端从资源树选择 `kind=directory` 的节点，只发送服务端返回的 URI。后端再次规范化 URI、检查可见目录，并解析该目录下的文档 ID；浏览器或模型提供的本机绝对路径不具有授权意义。

## 会话持久化

Scope URI 与知识库选择一起写入会话 preferences，重新打开会话时恢复选择。资源树刷新后如果 URI 已不可见，前端清除选择，后端仍会拒绝无权或不存在的 URI。

## 后续扩展

- 增加多个目录的 Scope 保存和 Playbook 默认范围。
- 在 `KnowledgeContext` 内统一加入关键词、向量、图谱和结构过滤。
- 为 Scope 保存代码仓库、目录和 commit 快照，形成知识到代码的可复现审查。
- 接入本地 Knowledge Agent 或自定义 API 时只替换 Provider，保留 Scope、权限和现有 Codex 改码链路。

