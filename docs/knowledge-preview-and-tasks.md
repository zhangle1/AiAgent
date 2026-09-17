# 原始 Office 文件预览与提炼任务

## 行为与边界

- DOCX：独立读取正文、表格及内嵌 PNG/JPEG/GIF 图片，不依赖模型或已解析正文。内容预览不保证原文件分页、字体、批注和复杂排版完全一致。
- XLSX：按工作表切换，读取单元格显示文本，公式采用缓存值，不执行外部链接或重新计算公式。最多 20 个工作表，每表 200 行、50 列，单元格最多 2000 字符，总 HTML 约 400 万字符；达到上限显示截断提示。
- Office 原文件预览限制 32 MB，ZIP 展开限制 100 MB／10000 个条目；DOCX 另有正文与图片预算。超过限制可下载原文件。
- DOC/XLS：导入 raw 后，通过服务器本地 LibreOffice 转为临时 DOCX/XLSX，再预览或提炼。设置环境变量 `Knowledge__LibreOfficePath` 为可执行文件完整路径，例如 Windows 的 `C:\Program Files\LibreOffice\program\soffice.com`；不设置则从 PATH 查找 `soffice`。转换最多 60 秒，取消或超时终止进程树并清理本轮临时目录。缺少转换器时提供明确提示，不显示空白或声称转换成功。
- 原文件不写回。预览不调用模型，不生成知识页，不创建索引；不使用在线 Office 预览站点。HTML 文本编码，iframe 使用空 sandbox 和拒绝外部资源的 CSP。

LibreOffice 的转换参数和独立用户目录用法见[官方命令行说明](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html)。

## 提炼队列

知识库首页和详情顶部展示全部活动提炼任务及最近 20 条结束任务，切换文件、标签或刷新页面后从服务端恢复。单后端实例串行执行，最多排队 32 条，同一文档正在排队、运行或取消时不重复入队。

状态：`queued → processing → success/error`；排队任务取消后为 `cancelled`；运行中先为 `cancelling`，模型停止后为 `cancelled`。如果取消抵达时保存已经完成，保留真实的 `success` 结果。失败或取消不删除上次成功草稿，结束状态可重试。

原来的任务在开始后固定显示 10%，整个模型循环没有可见反馈，无法从界面区分正常等待与停滞。本次接通解析、模型步骤、证据覆盖和保存阶段；15–90% 按有已校验证据的原文分段比例计算，保存阶段为 95%，成功为 100%。模型校验失败显示正在修正，进度不会按时间自动增加。失败／取消保留最后进度。

`updated_at` 表示最近一次阶段进展，不是模型心跳。超过 90 秒没有进展会提示继续等待或取消，不自动宣判失败。任务保留配置的总超时，取消令牌传递到已有 Codex/API 调用。刷新请求失败显示重连提示。重启把遗留活动任务标记为失败供手动重试，不重新消耗模型额度。

## API

- `GET /api/v1/knowledge/compilations`：活动与近期提炼任务，包含知识库、文件名、阶段、进度和时间。
- `POST /api/v1/knowledge/{kbName}/compilations/{jobId}/cancel`：幂等取消指定知识库的提炼任务。
- `GET /api/v1/knowledge/{kbName}/documents/{documentId}/office-preview`：只读预览，返回 `sections[{name,html}]`、`truncated`。
- 原来的 `/compile`、`/compilation`、同步 `/process` 保留。

`AiKnowledgeJob.UpdatedAt` 为可空新增列，通过现有初始化机制同步。队列接口复用现有知识库访问边界，不新增公司／项目权限模型。RAG 任务与活动索引不受提炼队列影响。

## 验证

自动测试覆盖 Word 表格和危险文本转义、Excel 多工作表与稀疏大范围截断、缺少旧版转换器的提示、排队取消、运行中取消向处理器传播、取消后下一任务执行、终态幂等取消、原有模型提炼与检索回归。

2026-09-17 续验：知识库相关 29 项测试通过，前端生产构建及 TypeScript 检查通过。任务终态写入与取消操作使用同一同步锁，避免完成结果被并发取消覆盖为 `cancelling`；新增回归覆盖保存期间取消、取消中禁止重复入队、保存完成后保留成功终态。

未使用用户原始文件或真实模型进行测试；旧版 DOC/XLS 的完整转换需在安装 LibreOffice 的部署机器验收。部署后重启后端以加载新接口和字段，并刷新前端；旧进程中的任务不会原地获得新取消能力。
