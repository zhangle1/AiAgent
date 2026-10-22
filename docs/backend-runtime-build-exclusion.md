# 后端运行数据误入构建内容修复

2026-10-22，用户反馈 Visual Studio 后端生成失败，截图未显示具体错误。在隔离输出目录执行真实后端构建，复现 5 条 `MSB3030`：复制 `data/knowledge_bases/.../conversation.md.json` 时报告找不到文件。C# 编译本身没有报错，失败发生在内容复制阶段。

根因是 Web SDK 默认项目项扫描包含运行中的 `data` 目录。用当前项目文件和合成数据做 MSBuild 求值，修复前确认：会话状态和模型缓存 JSON 进入 Content、运行文本进入 None，甚至该目录中的 `.cs` 会进入 Compile。运行数据既不是工程源码，也不是部署包内容；具体文件缺失／路径问题无需通过删除或修补用户知识库解决。

在 `backed/AiAgent.Backend.csproj` 的 `DefaultItemExcludes` 末尾追加 `data\**\*`，保留 SDK 原有排除项。该规则从默认项目扫描阶段排除运行目录；应用的 DataPath 和实际知识库文件未修改。原有源码、程序集内公开模型资源及配置文件保持原规则。

## 验证

- 修复前真实命令：`dotnet build backed/AiAgent.Backend.csproj --artifacts-path <隔离目录>/artifacts --verbosity minimal --nologo`，失败，5 条 MSB3030。
- 合成项目求值：`dotnet msbuild Fixture.csproj -nologo -getItem:Content,None,Compile`，修复前含 4 个运行数据项，修复后为 0。合成的示例配置、公开模型资源及 PythonWorkers 配置仍保留。
- 同一后端项目在全新 `artifacts-fixed` 目录完整构建：成功，17 个既有警告、0 个错误；新输出中没有 `data` 目录。
- Git 空白、UTF-8 编码及原有 BOM／换行检查通过；未启动后端或访问业务数据库。

验证证据与合成驱动保存在 `E:/项目/know-why/.codex-verification/backend-build-20261022/`，指纹见 `backend-runtime-build-exclusion.changeset.json`。自动审批策略拒绝清理最初的临时构建输出，已保留该目录并改用新的输出目录完成验证，没有重试其他删除方式。日志只用于本机排障，不作为真实用户数据样本提交。

本轮仅改后端项目配置并新增修复记录。既有客户端首页、代码库、Git、部署脚本等未提交修改保留；未提交或推送本次修复。
