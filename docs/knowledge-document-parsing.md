# 文档转换与结构解析

## 流程与边界

知识任务共用文档解析流程，从原始文件转换出完整 Markdown，规划章节后整体发布解析版本。资源导入在发布后完成。手动语义编译在发布后继续生成 L0/L1，证据型 Wiki 入口继续分析、生成和校验证据。

实现借鉴本地 OpenViking `openviking/parse/parsers/markdown.py` 的标题优先、短章节合并、段落后备拆分及表格行保护思路，使用独立的 C# 实现，不引入 OpenViking 服务或 AGFS。参考仓库中的 `_find_headings`、`_build_structure`、`_smart_split_content`、`_split_markdown_table_by_rows` 和 `openviking_cli/utils/config/parser_config.py`；行号会随参考仓库版本变化。

- `Services/Parsing/MarkdownDocumentPlanner.cs`：纯结构规划，不访问数据库、文件系统或模型。
- `Services/Parsing/MarkdownBundleWriter.cs`：统一写入章节、每级目录索引及哈希，转换入口和知识库发布共用。多文件目录、TXT 编码与 Word 结构的补充说明见 [C# Markdown 目录产物](knowledge-markdown-bundles.md)。
- `Services/Parsing/OfficeMarkdownConverter.cs`：Word、PPT、Excel 的结构化 Markdown 转换。
- `Services/Parsing/DocumentConversionService.cs`：统一格式注册、分发、受控路径、取消、结果和资产输出；PDF 固定适配现有 C# 引擎，独立图片仍显式配置 VLM。
- `Services/Knowledge/KnowledgeIngestionService.cs`：调用统一转换入口，执行章节规划与原子发布；聊天附件和项目资料也复用该转换入口，保持各自授权、长度限制和存储职责。
- `Services/Knowledge/KnowledgeParsedDocumentStore.cs`：验证写入计划、保存不可变版本、验证章节路径与内容哈希。
- `KnowledgeResourceService`：将最新有效版本投影为只读资源树，复用预览、下载及 Agent 工具接口。

原始文件不修改，完整解析正文仍供 Wiki 证据、L0/L1、检索和旧正文接口使用。章节文件用于组织与逐文件浏览，不取代完整正文，不成为固定模型输入上限。解析结构不由 LLM 决定；PDF 的文字、标题、表格和图片区域使用本地代码提取，独立图片转录本身仍需要 VLM，转录结果再进入相同的确定性规划。

## 拆分规则

1. `split_content=false` 时整篇保存为一个以文档名命名的 `.md`；允许拆分的小文档，同时符合字符及 token 估值阈值时也保持单文件。
2. ATX 标题 `#` 至 `######` 构成层级。反引号、波浪线围栏代码块、四空格／Tab 缩进代码、HTML 注释和转义 `\#` 不构成章节。有效 YAML frontmatter 保留在正文中，但其内部内容不作标题。没有闭合 frontmatter 的开头 `---` 不会吞掉后续标题。
3. 相邻短章节尽量合并。较大章节有子标题时递归生成目录，跳级标题归属最近的较低级标题；短的父级标题及介绍随首个子章节保存，避免丢失介绍。
4. 没有标题或没有子标题的超长章节按空行组合段落。超长段落再按长度拆分，不截断 UTF-16 代理对、代码围栏或完整图片／链接引用；不可分结构可超过目标阈值。来源区间保持原顺序，空行、换行和空白也完整覆盖。
5. 表格优先按行切分，能够容纳且小于 1000 字符的表头在后续章节重复。单独超长的一行保持完整，允许超过章节目标阈值；后续模型仍执行自身的输入容量检查。重复表头的长度记录在 manifest，不能将其当成原文中重复存在的证据。
6. 文件名来自文档名、标题或合并章节名；无标题的分片使用 `文档名_1.md`，超长章节使用 `章节名/章节名_1.md`。不是固定的 `part-1.md`。去除路径控制字符、处理 Windows 保留名和名称长度，并以确定性的数字后缀避免同名、大小写及清洗后的重名覆盖。

与直接照搬参考代码不同，本实现对普通合并结果继续检查字符和 token 双阈值，避免合并小尾段后再次超过限制。完整表格行及不可分的代码／图片／链接结构允许超过目标阈值。标题结构通过栈建立，避免对每个同级标题重新扫描全部标题。

## 解析设置与任务快照

全局知识设置及 `/compiler` 本批配置增加 `parsing`，示例：

```json
{
  "parsing": {
    "split_content": true,
    "max_section_tokens": 2048,
    "max_section_chars": 6000,
    "min_section_tokens": 512
  }
}
```

估值采用中日韩字符约 0.7 token、其他非空白 Unicode 字符约 0.3 token，不调用 tokenizer。字符限制按 .NET UTF-16 字符数计算，切点避开代理对。它们与模型百万级上下文、输出预留和聊天压缩参数相互独立。

阈值校验：token 估值 1–1000000，字符数 2–4000000，短章节合并阈值 0–章节 token 估值。服务端拒绝 `parsing=null` 或无效数值。旧设置和历史配置没有该字段时采用上述默认值。

新任务入队时深拷贝配置并写入 `AiKnowledgeJob.ConfigurationJson`，包括解析设置。后台处理使用快照；取消、失败和重试继续由既有知识任务中心管理。旧 `/process` 请求也可显式提供 `parsing`，缺省使用已保存的全局设置。

## 格式转换

| 格式 | 转换方式 |
| --- | --- |
| Markdown／文本 | 保留完整文本；Markdown 结构统一由规划器识别。 |
| CSV／TSV | 解析引号、跨行字段、转义双引号及空列，生成列名并保留首行数据，输出 Markdown 表格。 |
| PDF | C# PdfPig 提取文字、书签、字号、网格表格与图像位置；PDFtoImage / PDFium 限像素渲染页面，再用 SkiaSharp 裁出图片和矢量图区域。缺少书签时按字号识别标题，生成相对图片链接后进入同一章节规划器。保存页数和扫描页提示；取消向上传递。 |
| DOCX | 保留段落、换行、标题样式／outline、列表、表格和脚注／尾注。按关系引用导出内嵌位图为 PNG，保持正文位置；样式 XML 和未引用图片不混入正文。 |
| PPTX | 按 presentation 关系中的幻灯片顺序读取，保留标题、文本段、表格及备注；导出引用图片为 PNG，不按 ZIP 路径字典序把 slide10 排在 slide2 前面。 |
| XLSX | 工作表作为标题，保留使用区域的列位置、空单元格、行及表格文本，转义单元格中的竖线。 |
| XLS／XLSB／Excel 模板 | 沿用聊天的 ExcelDataReader，保留工作表及空列，不执行宏；每个上传入口仍按自己的格式白名单控制。 |
| DOC | 保留受控 LibreOffice 转换，再进入 DOCX 转换器；未安装环境时明确失败，不改变原文件。 |
| HTML | AngleSharp 只解析 DOM，清理活动内容，ReverseMarkdown 转为 Markdown；保留标题、列表、表格与代码，不自动读取远程或服务器本地图片。 |
| RTF | RtfPipe 从文件流读取代码页，转 HTML 后进入同一 DOM／Markdown 流程。 |
| EPUB | 受限 ZIP 包读取 container、OPF 和 spine，按真实阅读顺序转换章节；包内图片规范化为 PNG。加密／混淆资源、非 HTML 正文明确失败。 |
| 图片 | 选择配置的 VLM 转录可见文本及结构，明确不摘要、不虚构、不执行图片中的指令；供应商默认输出策略，之后统一结构拆分。 |

PDF 主链路不启动 Python，不调用模型重写正文，不自动回退旧 worker。无文本层页面保留整页图片，正文和任务定位记录明确提示尚未 OCR；不把图片占位当成已提取文字。书签、字体和图文排序为确定性启发式，不承诺复杂跨栏／公式的出版级还原。完整横纵网格可转换为 Markdown 表格，无框线／合并单元格等不可靠网格保留文字及图像，不猜测删字。Word／PPT 的文本抽取也不等同于复杂版面视觉理解。

## 存储与原子发布

一次解析先建立 `parsed/<documentId>/.staging-<随机值>/`，写入：

```text
document.md                  完整转换正文
layout.json                  写入计划与校验信息
sections/                    标题／段落组织的 Markdown，每级含 _index.md
assets/                      PDF 裁图与 Office／EPUB 等导出的 PNG，带内容哈希
conversion/                  此次 PDF 等转换器的中间结果
```

计划记录版本 `markdown-layout-2`（兼容旧 `markdown-layout-1`）、完整正文哈希、根名称、实际解析设置，以及每个章节的路径、顺序、原文起止偏移、重复表头长度、估算 token 数和内容哈希，以及独立的索引路径与哈希。发布前检查来源区间连续完整，章节文本在图片路径改写前等于来源区间加上显式重复表头；图片引用随后按章节实际层级计算相对路径，manifest 保存最终章节哈希。完整正文使用 assets/ 相对路径，章节使用 ../ 等正确相对路径，下载后也能按生成目录结构查找图片。

全部文件写入成功且未取消后，将临时目录重命名为 GUID 版本目录。随后在短事务中插入 `AiKnowledgeParsedDocument` 与更新正文状态。数据库记录是可见版本指针，资源树不扫描磁盘目录寻找“最新文件”。写入失败或取消不会发布半套结果；临时目录清理校验存储根目录及符号链接。数据库提交失败可能留下不可见的无引用版本，不能从资源 API 访问。

不增加数据库字段；沿用 `ContentPath`、`ContentHash`、`ParserVersion`、`LocatorJson`。旧 `ParserVersion=1` 的单文件正文仍可读取；重新解析后才产生新布局，不自动重跑历史模型任务。

## 资源、预览与权限

原文件 URI 和下载保持原始资料身份。解析版本投影在原始父目录的系统只读目录中，例如：

```text
资料/技术资料/
├── 技术报告.md
└── 解析正文/                   URI 段为 _parsed/
    └── 技术报告-123/           文档 ID 隔离同名来源
        ├── 概述.md
        └── 安装/
            ├── Linux.md
            └── Windows.md
```

该示例是否拆出目录取决于内容大小。小文档只有一个章节文件。

- 资源节点新增 `source_uri`、`parsed_document_id`、`section_order`；原文件读取结果增加 `parsed_outputs`。正文接口增加 `parsing_version` 和 `parsed_section_count`，不返回物理路径。
- 原文件“解析正文”页显示章节入口；打开章节会展开目录祖先，支持 GFM 表格预览、原文件返回及 Markdown 下载。窄屏继续使用既有堆叠面板。
- 章节目录、章节文件禁止上传、新建和再次编译，不进入文件处理列表或原始文件选择器，也不轮询原文件任务。下载参数使用后端支持的布尔值 `download=true`。
- Agent `ls/read/search` 复用可见树与既有范围检查，可逐章节读取。`status` 只用于原始资料。
- 仅投影当前可见原文的最新有效解析版本，来源哈希必须匹配；重新解析替换布局，来源删除后撤下对应节点。个人来源的章节继承个人范围。
- 下载按文档 ID、解析版本和清单路径定位，不把请求 URI 拼成文件路径；校验目录边界、符号链接和文件内容哈希。损坏章节报错，损坏／缺失清单不影响原始文件和完整正文的兼容读取。

手动编译的 L0/L1 阶段在正文保存后开始。语义阶段失败，完整正文和已发布章节仍然可读；任务中心保留具体失败原因。RAG 活动索引不变，不会因为多了章节而隐式重建。

## 验证

后端专项测试使用临时 SQLite、Markdown／Office／C# 生成的 PDF 和假模型，覆盖层级、短章合并、代码和注释排除、frontmatter、无标题、双阈值、Unicode 全文覆盖、表格行与表头、重名与保留名、配置快照、发布／替换、取消、损坏清单、历史正文、个人隔离、资源下载、Agent 读取和语义失败后正文可读。旧 PDF 替身继续验证异常边界；CSharpPdfParsingTests 与真实入库测试直接加载 PdfPig / PDFium，验证书签、字号、表格、PNG、旋转和裁切框。未验证 OCR 或用户原书。

前端 `front/tests/knowledge-parsing.browser.mjs` 使用已运行前端和全模拟接口，验证章节跳转、只读标记、下载链接、表格、拆分开关和本批参数、生成文件过滤及窄屏。与既有 `knowledge-layout.browser.mjs`、`knowledge-agent.browser.mjs` 一起执行回归；不访问真实资料或在线模型。


## PDF 图片、原文预览与 raw 目录（2026-11-17）

- 新导入的资源按逻辑 URI 命名空间和子目录保存。例如 `viking://resources/Books/Factory%20Physics.pdf` 对应 `resource-store/raw/resources/Books/Factory Physics.pdf`；项目和个人目录分别在 `raw/projects/`、`raw/user/` 下，避免不同命名空间撞名。旧 flat raw 文件不批量搬迁，继续按已有数据库 StoragePath 读取。目录创建同步建立受控 raw 子目录。
- `raw_relative_path` 只展示 raw 内相对位置，不返回服务器绝对路径；导入完成刷新当前目录，PDF 是否解析成功不影响原文件可见性。
- C# PDF 服务直接复用 KnowledgePathService 的实际知识库根目录计算，兼容外置 KnowledgeStorage:RootPath 和旧 DataPath；不依赖 worker，继续拒绝旁路目录和符号链接。其他 Python worker 保留之前的根目录修复。
- PDF 图片作为解析版本中的只读文件投影到 `_parsed/<文档名-ID>/assets/`。图片读取继续经过本人／项目可见树、最新解析版本、源文件指纹及图片 SHA-256 校验。Markdown 展示只将 manifest 中的图片改写为受控 Viking URI，再由前端使用原文件接口加载，不允许外部图片地址。
- 原文预览改为 PDF.js 分页 canvas，不依赖受 sandbox 限制的浏览器 PDF 插件。锁定 react-pdf 10.4.1 与 pdfjs-dist 5.4.296，保证 API 与 worker 版本一致。图片 URL 携带 parsed_document_id，重解析后避免沿用旧图片缓存；服务端拒绝旧版本读取。
- PDF.js 的 JPEG 2000 解码器通过站内路径加载，随前端独立部署包一起发布。资料树按来源顺序浏览解析章节，图片在 Markdown 内联显示。
- 有书签的 PDF 沿用书签层级。无书签且含隐藏 OCR 文字的扫描 PDF 根据页面位置与字号合并多行章名，避免把页眉、独立页码或图表编号识别成章节。旧的已发布版本可继续预览，重新解析后使用新规则。
- 本地 PDF 提取上限为 256 MiB 输入、2000 页、正文 32 Mi 字符。单页限 20 万字形和 2 万路径／子路径，裁图前限制约 400 万像素及最长边 4096 像素。图片随页导出和去重，图片张数、单图文件大小及图片总量没有额外配额。
- 知识库 PDF 每 20 页一批显示进度，逐页保存正文、图片及校验信息。单页内容错误时继续处理后续页，任务会标出未完成页。失败、超时或取消后可在任务中心重试，原始文件及图片选项一致时复用已校验的页面。全部完成后按原页顺序合并并整体发布，随后清理进度文件。
- PDF 的书签／字号标题与图片区域提取参考本地 OpenViking 的处理思路，采用独立实现，没有复制其解析器源码。无需启动 OpenViking 服务。

### C# 主解析部署与配置

后端 NuGet 固定 `PdfPig 0.1.16`、`PDFtoImage 5.4.0`，使用其传递的 PDFium 与 SkiaSharp 原生资产。`dotnet publish` 时必须保留目标平台的 `pdfium` 与 `libSkiaSharp`，不能只复制主 DLL。当前已在 Windows x64 加载并测试，Linux／macOS 需要在实际目标环境运行 `/api/v1/knowledge/parsing/preflight` 验收；Linux 仍需匹配发行版的基础运行库，未验证 Alpine／单文件裁剪发布。不需要单独安装 Python、PyMuPDF、Poppler 或 OpenViking。旧 `PythonWorkers/parsing/` 保留在源码作为历史参考，已从发布内容排除；RAG 与独立图片 OCR 的 Python 依赖保持各自原用途。

```json
"DocumentParsing": {
  "TimeoutSeconds": 300,
  "AllowedRoots": ["data"]
}
```

`AllowedRoots` 可省略：先兼容旧 `PythonWorkers:AllowedRoots`，未配置则使用 `data`；相对根同时按工作目录和应用目录解析。实际知识库存储根额外加入，外置知识库不需要放开整个 E 盘。聊天附件或项目导入使用其他存储根时，额外列入其确切目录；Web 请求不接受此配置或物理路径。旧请求中的 `engine` 字段兼容接收，实际结果统一报告 `pdfpig-pdfium-layout`。`WriteImages=false` 沿用聊天附件的纯文本模式。

PDF 使用独立超时（5–3600 秒）及单任务限流，不再使用 PythonWorkers 的解释器、脚本路径和总超时。知识库 PDF 每次报告页进度时刷新解析超时窗口，任务自身的执行时限仍覆盖整个任务。取消在排队、页间、布局循环及写入边界检查；PdfPig 单页处理和 PDFium 同步原生渲染无法强制中断，超时后等待当前调用退出再返回，保证不会有后台线程继续写已清理的 staging。需要严格硬超时或原生崩溃隔离时应另设受控 C# 进程，当前实现没有声称具备此能力。

环境检查会用内存生成的微型 PDF 实际运行文字提取和原生渲染，返回 `python_required=false` 与 `native_renderer_ready`，无需接触用户文档。发布保留第三方许可证和 PDFium notices，参考 [PdfPig](https://github.com/UglyToad/PdfPig/tree/v0.1.16)、[PDFtoImage 5.4.0](https://github.com/sungaila/PDFtoImage/tree/v5.4.0) 和 [PDFium 许可](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/LICENSE)。

前端按 lockfile 安装依赖并重启。后端更新后旧失败任务不会自动重跑，在资料页点“重新解析”；正文发布后若后续 L0/L1 模型阶段失败，仍可浏览已保存的章节与图片。

验证记录（2026-11-17）：隔离环境复现外置知识库被 Python worker 白名单拒绝；修复后知识工作区、raw 上传、结构规划、清理／状态和 Swagger 共 130 项后端测试通过。2 项 Python 测试用真实生成的 PDF 验证书签、字号标题、文本、嵌入图、矢量图、扫描页提示、相对路径和 stdin/stdout 协议，原始 PDF 字节不变。TypeScript 在归档旧版生成类型并重建后通过。真实 Next.js 页面使用合成 API／PDF 验证子目录上传刷新、raw 相对位置、PDF.js canvas 与翻页、完整正文及章节图片、版本参数和 390px 布局；16.1 MiB 上传错误／重试回归通过。已检查桌面和手机截图。未读取用户原书或真实数据库，未调用模型。

C# 迁移验证（同日，取代上述 Python 主链路）：176 项后端回归通过，包括真实 C# PDF 提取、四种旋转与非零 CropBox、超大页面限像素渲染、2001 页拒绝、旧根目录配置兼容、书签／字号、网格表格、位图／矢量／扫描页、缺失 Python 路径下的 preflight、输入输出根隔离、取消、不覆盖旧结果，以及真实资源发布和聊天上传提取。浏览器 PDF 分页／图片／raw 子目录／窄屏回归通过，样本改为 Node 直接生成，也无需 Python。已核对隔离发布目录的 Windows x64 PDFium／SkiaSharp 原生资产齐全、旧 parsing 脚本排除，未复制实际 appsettings.json／data。旧 Python 验证记录仅保留为迁移前历史。

## 统一多格式转换（2026-11-17）

生产入口使用 `IDocumentConversionService.ConvertAsync`，旧 `IDocumentParsingService.ParsePdfAsync` 保留为 PDF 专用能力。依赖新增 ReverseMarkdown 6.3.0 与 RtfPipe 2.0.7677.4303，没有引入上游整套默认转换器，也没有复制其业务实现。ReverseMarkdown v6 使用 Default + GithubFlavored 配置实际转换 HTML，避免 GitHub flavor 透传整块 HTML；产物统一 LF。

知识库输出正文、PNG、警告和定位信息后，继续使用既有章节规划／manifest／来源授权和版本哈希。聊天只输出文本 Markdown，长度预算与不可信附件标记不变。项目先从已授权来源读取原文件字节，转存到知识库存储根下独立的受控临时目录进行转换，避免放开项目所在整块磁盘；保存原文件与文本 Markdown 后清理临时目录，不保存失效图片链接，导入结果明确提示图片请查看原文件。

允许根和 TimeoutSeconds 继续使用 DocumentParsing 配置，兼容旧 PythonWorkers 允许根，但不启动 Python。额外纳入服务端配置的聊天附件 files 目录；未开放任意本地路径、远程 URL 或源码文件上传白名单。全局限两项转换并发；取消和超时必须等待转换真正退出再清理文件。

输入和产物均有预算：非 PDF 转换输入至多 64 MiB，正文 32 Mi 字符；包内最多 5000 条、声明展开总量 100 MiB，实际读取的二进制／HTML 部件逐个限 32 MiB，XML 禁止 DTD 并限制字符；CSV 限 200 万单元格，XLSX 单个使用区域同样限 200 万单元格，避免稀疏尾格扩大成数十亿空格。上传仍保留原场景 20／25 MiB 等限制，不因转换引擎上限增大而自动放宽。

新位图只发布解码后规范化的 PNG，单图最长边 4096、400 万像素；未支持的图形类型、缺失或外部图片保留警告，不生成虚假资源路径。PDF 的独立预算、扫描提示和原生渲染限制继续按上一节执行。格式能力和后续范围见 [统一转换实施记录](E:/项目/know-why/AiAgent/docs/aiagent-multiformat-markdown-plan.md)。
