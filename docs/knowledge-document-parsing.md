# 文档转换与结构解析

## 流程与边界

知识任务使用同一条解析流程：原始文件 → 格式转换后的完整 Markdown → 确定性章节计划 → 临时写入 → 完整解析版本发布 → L0/L1 语义生成。旧的证据型 Wiki 入口也复用这一解析阶段，再进行原有分析、生成和证据校验。

实现借鉴本地 OpenViking `openviking/parse/parsers/markdown.py` 的标题优先、短章节合并、段落后备拆分及表格行保护思路，使用独立的 C# 实现，不引入 OpenViking 服务或 AGFS。参考仓库中的 `_find_headings`、`_build_structure`、`_smart_split_content`、`_split_markdown_table_by_rows` 和 `openviking_cli/utils/config/parser_config.py`；行号会随参考仓库版本变化。

- `Services/Parsing/MarkdownDocumentPlanner.cs`：纯结构规划，不访问数据库、文件系统或模型。
- `Services/Parsing/OfficeMarkdownConverter.cs`：Word、PPT、Excel 的结构化 Markdown 转换。
- `Services/Knowledge/KnowledgeIngestionService.cs`：按格式调用转换器，统一执行规划与发布，保留现有 VLM、PDF worker 和旧 Office 转换入口。
- `Services/Knowledge/KnowledgeParsedDocumentStore.cs`：验证写入计划、保存不可变版本、验证章节路径与内容哈希。
- `KnowledgeResourceService`：将最新有效版本投影为只读资源树，复用预览、下载及 Agent 工具接口。

原始文件不修改，完整解析正文仍供 Wiki 证据、L0/L1、检索和旧正文接口使用。章节文件用于组织与逐文件浏览，不取代完整正文，不成为固定模型输入上限。解析结构不由 LLM 决定；图片转录本身仍需要 VLM，转录结果再进入相同的确定性规划。

## 拆分规则

1. `split_content=false` 时整篇保存为一个以文档名命名的 `.md`；允许拆分的小文档，同时符合字符及 token 估值阈值时也保持单文件。
2. ATX 标题 `#` 至 `######` 构成层级。反引号、波浪线围栏代码块、四空格／Tab 缩进代码、HTML 注释和转义 `\#` 不构成章节。有效 YAML frontmatter 保留在正文中，但其内部内容不作标题。没有闭合 frontmatter 的开头 `---` 不会吞掉后续标题。
3. 相邻短章节尽量合并。较大章节有子标题时递归生成目录，跳级标题归属最近的较低级标题；短的父级标题及介绍随首个子章节保存，避免丢失介绍。
4. 没有标题或没有子标题的超长章节按空行组合段落。超长段落再按长度拆分，不截断 UTF-16 代理对。来源区间保持原顺序，空行、换行和空白也完整覆盖。
5. 表格优先按行切分，能够容纳且小于 1000 字符的表头在后续章节重复。单独超长的一行保持完整，允许超过章节目标阈值；后续模型仍执行自身的输入容量检查。重复表头的长度记录在 manifest，不能将其当成原文中重复存在的证据。
6. 文件名来自文档名、标题或合并章节名；无标题的分片使用 `文档名_1.md`，超长章节使用 `章节名/章节名_1.md`。不是固定的 `part-1.md`。去除路径控制字符、处理 Windows 保留名和名称长度，并以确定性的数字后缀避免同名、大小写及清洗后的重名覆盖。

与直接照搬参考代码不同，本实现对普通合并结果继续检查字符和 token 双阈值，避免合并小尾段后再次超过限制。仅完整表格行拥有超限例外。标题结构通过栈建立，避免对每个同级标题重新扫描全部标题。

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
| PDF | 既有 Python worker 先输出 Markdown，再使用同一规划器。保存页数等定位信息，输出必须在本次临时目录内；取消继续向上传递。 |
| DOCX | 仅读取正文，保留段落、换行、标题样式／outline、列表、表格和脚注／尾注。样式 XML 不混入正文。 |
| PPTX | 按 presentation 关系中的幻灯片顺序读取，保留标题、文本段和表格；不再按 ZIP 路径字典序把 slide10 排在 slide2 前面。 |
| XLSX | 工作表作为标题，保留使用区域的列位置、空单元格、行及表格文本，转义单元格中的竖线。 |
| DOC／XLS | 沿用受控临时目录的 LibreOffice 转换，再进入 DOCX／XLSX 转换器，不改变原文件。 |
| HTML | 移除脚本、样式及注释，保留标题、段落、表格和代码块，解码 HTML 实体；不会执行 HTML。 |
| 图片 | 选择配置的 VLM 转录可见文本及结构，明确不摘要、不虚构、不执行图片中的指令；供应商默认输出策略，之后统一结构拆分。 |

PDF 当前沿用文本解析 worker，不把选择了 VLM 等同于已实现扫描 PDF 的逐页 OCR。空转换结果明确失败；扫描件需先 OCR，或按图片入口处理。Word／PPT 的文本抽取也不等同于复杂版面和图片的视觉理解。真实转换与模型能力取决于部署环境中的 worker、LibreOffice 和模型配置。

## 存储与原子发布

一次解析先建立 `parsed/<documentId>/.staging-<随机值>/`，写入：

```text
document.md                  完整转换正文
layout.json                  写入计划与校验信息
sections/                    标题／段落组织的 Markdown
conversion/                  此次 PDF 等转换器的中间结果
```

计划记录版本 `markdown-layout-1`、完整正文哈希、根名称、实际解析设置，以及每个章节的路径、顺序、原文起止偏移、重复表头长度、估算 token 数和内容哈希。发布前检查来源区间连续完整，章节文本等于来源区间加上显式重复表头。

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

L0/L1 仍由原知识任务在正文保存之后执行。语义阶段失败，完整正文和已发布章节仍然可读；任务中心保留具体失败原因。RAG 活动索引不变，不会因为多了章节而隐式重建。

## 验证

后端专项测试使用临时 SQLite、临时 Markdown／PDF worker 替身／Office 文件及假模型，覆盖层级、短章合并、代码和注释排除、frontmatter、无标题、双阈值、Unicode 全文覆盖、表格行与表头、重名与保留名、配置快照、发布／替换、取消、损坏清单、历史正文、个人隔离、资源下载、Agent 读取和语义失败后正文可读。PDF 替身验证流水线接入，不声称已验证真实 PDF 环境或 OCR。

前端 `front/tests/knowledge-parsing.browser.mjs` 使用已运行前端和全模拟接口，验证章节跳转、只读标记、下载链接、表格、拆分开关和本批参数、生成文件过滤及窄屏。与既有 `knowledge-layout.browser.mjs`、`knowledge-agent.browser.mjs` 一起执行回归；不访问真实资料或在线模型。
