# 目录资源 L0/L1 语义生成

知识工作区的资源上传（启用解析）及文件「重新解析」复用 `wiki_compile` 有界后台队列。任务先保存解析正文，再生成语义内容；不要求部署 OpenViking，不创建向量索引，不改变活动索引版本。

## 数据流

1. `ParseResourceAsync` 保存解析正文（L2）。文本解析不调用 LLM；图片沿用现有 VLM 解析。
2. `KnowledgeResourceSemanticService` 使用设置中的 Codex 或 LLM API 生成文件摘要。
3. 从文件所属目录开始，自底向上收集直接文件摘要和子目录 L0，生成当前目录 L1。
4. 从 L1 的首个简介段落提取 L0，最多 500 字符，不再单独调用模型。
5. `KnowledgeSemanticStore` 写入同一版本的 `.overview.md` 和 `.abstract.md`，最后原子替换 `current.json` 发布整对文件。

用户在目录页切换「L0 · 目录摘要」「L1 · 目录概览」，可下载对应 Markdown。文件页的「知识摘要」展示文件摘要。任务中心提供状态、取消和失败重试，文件页恢复任务轮询。已有资料点击「重新解析」即可补建语义内容；空目录和未解析文件不会凭空生成知识。

## 存储与缓存

```text
<knowledge-root>/.resource-semantic/<URI SHA-256>/
  current.json
  <generation-id>/
    .abstract.md
    .overview.md
```

URI 只用于逻辑导航，不转为本机路径。真实 Markdown 存放在受控知识目录，两个文件使用不可变版本，取消或失败不会发布半对结果。未发布的版本目录可能保留，当前版本只由 manifest 指定。

缓存指纹包含生成器版本、后代资源 URI、文档 ID、原文哈希、文件名、解析正文哈希。新增、删除、移动或正文变化使相关目录缓存失效；重复解析相同内容保留缓存。`AiKnowledgeParsedDocument.ContentHash` 为新增可空列，由现有初始化流程补充，历史记录没有该值时回退到解析记录 ID。调整模型设置不会主动清除已有语义缓存。

生成前后校验来源指纹，变化时拒绝发布；读取也重新校验，失效内容显示待生成。manifest 格式损坏、语义文件缺失或内容为空时视为缓存失效，重新解析可补建，不阻止正文读取；路径和访问权限错误仍按异常处理。语义阶段失败时正文仍可读取，任务显示失败；重试复用已完成且指纹匹配的结果。服务重启沿用队列中断处理，不自动重新付费调用模型。

## 边界

- 共享资料及项目分别汇总到各自根目录；个人资料只汇总到本人根目录，不汇总到 `viking://user/`。资源读取继续经现有目录访问校验。
- 模型只接收显式文本，CLI 使用独立空工作目录和只读模式。提示词将资料标记为不可信数据；不把此提示宣称为新的权限隔离。
- 正文最多 256000 字符，按 16000 字符分段；目录摘要按约 24000 字符分批归并，受现有模型调用预算和超时约束。过大任务明确失败，不把截断的正文冒充完整结果。
- 导航 URI 由宿主生成，前端语义预览禁用图片和外链，不执行模型 HTML。
- 本阶段未接入 EmbeddingQueue、向量检索或 L0/L1 引导问答；原目录问答链路保持现有行为。

## 验证

后端使用临时 SQLite、合成 Markdown 和替身模型测试生成、L0 提取、分段归并、调用预算、取消、原子发布、重启读取、内容缓存、缓存损坏重建、后台任务成功链路与私有目录边界。浏览器测试使用模拟 API，验证 L0/L1 切换、Markdown 下载、导航、目录和文件摘要的安全渲染、连续完成任务刷新和切换文件的异步响应隔离。

```powershell
dotnet test backed.tests/AiAgent.Backend.Tests.csproj --filter "FullyQualifiedName~Knowledge"
cd front
npx tsc --noEmit --incremental false
# KNOWLEDGE_TEST_TOOLS 指向已有 playwright/esbuild 的目录
node tests/knowledge-semantic.browser.mjs
```

## 队列与任务中心阶段

队列调度、去重、取消、重启中断处理和终态补写集中在 `Services/TaskQueue/KnowledgeCompilationWorker.cs`；`KnowledgeCompilationHandler` 保留正文解析和语义生成的领域顺序。继续复用 `wiki_compile`，目录资料作为一个串行任务执行，只有语义文件发布完成才成功，不增加 RAG 索引任务。

`AiKnowledgeJob.Stage` 为可空列，值包括 queued、validating、parsing、semantic、compiling、completed。失败保留阶段，并记录错误原因；语义依赖缺失也必须报错。任务中心读取时补写内存终态，数据库仍不可写时使用内存状态和错误覆盖；筛选不改变摘要统计（统计范围仍为最近 limit 条）。取消或重试旧任务按 ID 回读，不依赖列表窗口。

上传 Process 默认 true；显式取消解析只保存原文。旧任务不会被自动重跑，任务中心允许已完成任务重新处理，重复点击复用同一活动任务。部署需要更新前后端，启动时按实体初始化可空 Stage 列。测试覆盖语义失败后正文可读、失败阶段与错误展示、重试成功、缺失语义服务报错及终态写库失败时的任务中心结果。
