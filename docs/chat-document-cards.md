# 聊天文档卡片

已登记仓库的根目录及文档子目录均可识别，无需把 HTML、DOC 移入 `doc/`。新文件可通过资料面板的刷新按钮更新目录。聊天链接兼容 `D:/...` 和 `/D:/...` 形式；后者只去除 Windows 盘符前的额外斜杠，仍由服务端验证当前项目仓库归属，再与授权资料列表匹配。仓库外的项目上级目录不会因此开放。

AI 将文件保存到当前项目已登记仓库后，在回复中返回 Markdown 文件链接，例如 `[演示文稿](仓库名/docs/slides.pptx)`。聊天渲染器将链接、行内文件引用及已识别的普通文本路径显示为文件卡片，历史消息无需迁移。卡片展示文件名、格式、预览入口和独立下载按钮。只有文本说明或代码块、没有实际文件时，不会自动创建文档。

点击卡片复用右侧项目资料面板及目录选中状态，不跳转页面。下载先解析当前项目文件，再请求受权限保护的原文件下载接口，保留原文件名及二进制内容。下载失败显示卡片内提示，可重新点击下载。不存在或重名的文件明确报错，不选择任意同名文件。

| 格式 | 右侧展示 | 下载 |
| --- | --- | --- |
| MD、Markdown | Markdown 渲染 | 原文件 |
| HTML、HTM | 现有受限 iframe | 原文件 |
| PDF | 现有 PDF 查看器 | 原文件 |
| DOCX、XLSX、PPTX | 现有服务端提取的内容预览，不保证原版式 | 原文件 |
| TXT、CSV、JSON、JSONL、XML、YAML、YML | 现有文本内容预览 | 原文件 |
| 仓库内 DOC、XLS、PPT、RTF | 提示下载后在本地打开，或生成新版 Office 格式 | 原文件 |

此功能不新增模型供应商协议、数据库字段或生成器。模型仍需实际生成符合格式的文件；把文本改名为 DOC/PPT 不会得到真实 Office 文件。仓库外未登记路径不提供卡片下载。旧版格式没有加入上传和 Markdown 转换白名单。

实现入口为 `front/components/chat/ChatDocumentCard.tsx` 与 `MarkdownMessage.tsx`。解析和下载封装在 `front/lib/code-repository-api.ts`，预览继续使用 `ChatInspectorPanel`。外部链接不根据显示名称映射到本地文件，代码块内的路径保持代码。切换项目或会话，以及连续点击多个文件时，过期预览请求不会覆盖最新选择。

验证：

- 前端类型检查：`npx tsc --noEmit --incremental false`。
- 39 项回归：`node --test tests/chat-file-links.test.mjs tests/chat-document-download.test.mjs tests/chat-code-copy.test.mjs tests/chat-packaging.test.mjs`。
- 浏览器隔离夹具：`tests/chat-document-cards.browser.mjs`，通过 `DOCUMENT_CARD_TEST_TOOLS` 指定含 Playwright 和 esbuild 的目录。验证真实 DOM、预览回调、下载文件名和字节、403/缺失文件提示、外链、代码块和手机宽度；不连接用户项目或调用模型。
- 后端 11 项文档及交付包测试：`dotnet test backed.tests/AiAgent.Backend.Tests.csproj --no-restore --filter FullyQualifiedName~ProjectPackageDocumentTests`。

尚未在用户登录后的生产会话中验证；变更不包含部署、Git 提交或推送。本地审阅快照见 `chat-document-cards.changeset.json`，不等同于平台审批记录。
