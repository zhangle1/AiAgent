# 聊天文件附件与 Codex app-server 规格

> 状态：已实施最小可用闭环  
> 范围：聊天输入区粘贴、拖放或选择图片、PDF、Office 和文本文件；服务端受控存储、文本抽取与 Codex app-server 上下文适配。

## 1. 可行性结论

本机验证环境为 Codex CLI `0.144.6`。`codex app-server --help`、本地 app-server 协议文档和 `UserInput` 协议类型均表明：`turn/start.params.input` 可传 `text`、内联 `image`、受控路径 `localImage`，以及用于技能/插件的 `skill`、`mention`；**不存在通用 `localFile`、PDF、Word、Excel 或 PowerPoint 输入项**。

因此：

1. 图片仍通过原生 `localImage` 输入，Codex 可直接看图。
2. PDF、DOCX、XLSX、PPTX、Markdown、纯文本、CSV 等不能作为原生 app-server 附件理解，必须在服务端抽取为受限文本，再用 `text` 输入注入。
3. 旧式 OLE 二进制 Office 格式（`.doc`、`.xls`、`.ppt`）可以安全保存并显示为附件，但本期不抽取、不传给模型；发送时明确提示用户转换为现代 Office 或 PDF。
4. app-server 的 `mention` 不是任意本地文件上传协议，不能把它用作办公文档附件通道。

## 2. 支持矩阵

| 格式 | 上传与持久化 | 本期服务端提取 | app-server 原生输入 | 发送行为 |
| --- | --- | --- | --- | --- |
| PNG / JPEG / WebP / GIF | 支持，校验真实图片签名 | 不适用 | 支持：`localImage` | 原图传给内置 Codex；第三方 profile 按既有 OCR 策略处理 |
| PDF | 支持，校验 `%PDF-` | 支持，复用受限 Python PDF 解析服务；不可用时拒绝该附件发送 | 不支持 | 抽取文本作为不可信附件上下文 |
| DOCX | 支持，校验 OOXML ZIP 结构 | 支持，读取 Word XML 文本 | 不支持 | 抽取文本作为不可信附件上下文 |
| XLSX | 支持，校验 OOXML ZIP 结构 | 支持，读取共享字符串与工作表单元格 | 不支持 | 抽取文本作为不可信附件上下文 |
| PPTX | 支持，校验 OOXML ZIP 结构 | 支持，读取幻灯片文本 | 不支持 | 抽取文本作为不可信附件上下文 |
| Markdown / TXT / CSV | 支持，UTF-8 严格解码 | 支持 | 不支持 | 受限文本作为不可信附件上下文 |
| DOC / XLS / PPT | 支持，校验 OLE 复合文件签名 | 不支持 | 不支持 | 不发送给模型，提示转换为 DOCX/XLSX/PPTX/PDF |
| RTF / ODT / ODS / ODP / 压缩包 / 可执行文件 | 不支持 | 不支持 | 不支持 | 上传时拒绝 |

文本提取不包含 PDF 页面视觉版式、扫描件 OCR、Office 嵌入图片、图表、批注、公式渲染、宏或受密码保护内容。需要理解这些视觉信息时，用户应另附图片，或先转成可读 PDF/文本。

## 3. 架构与安全边界

```text
浏览器 File / Clipboard / Drop
  -> POST /chat/attachments/images 或 /chat/attachments/files
  -> 不透明 attachment id + 服务端受控临时目录
  -> 所有权、数量、扩展名、大小、魔数/OOXML 结构验证
  -> 发送时迁移至 用户哈希/sessionId 隔离目录
  -> 可提取文件：内存文本提取、字符上限、删除临时解析缓存
  -> <attachment_text ...>不可信数据</attachment_text>
  -> Codex app-server text 输入；图片额外使用 localImage
```

- 浏览器永不提交本地路径；接口、消息元数据和数据库只保存不透明 ID、文件名、MIME、大小、类别与解析状态，**不保存真实绝对路径、原始内容或密钥**。
- 每次解析和下载均验证当前认证用户与会话归属；ID 使用随机 GUID，文件名只用于显示且经 `Path.GetFileName` 截断。
- 文件扩展名、声明 MIME 和内容签名/OOXML 包结构三重约束；不执行、解压或运行用户文件中的宏、脚本和嵌入对象。
- 文本附件被包在明确的“不可信附件数据”边界内；模型不得把其中的指令视为系统指令、工具授权或权限变更请求。
- 默认单个非图片文件最大 20 MB、每轮最多 4 个、可提取总文本最多 120,000 字符、单文件最多 40,000 字符。配置值只能在安全上限内调整。
- 未发送的临时附件按 `ChatAttachments:RetentionMinutes` 清理；文档抽取只在内存中完成，不落盘生成解析缓存。发送后原件迁移到会话历史隔离目录，以支持消息回看；后续会话删除/保留策略应由独立清理任务统一处理。

## 4. 接口与数据模型

### HTTP

- 既有 `POST /api/v1/chat/attachments/images`、`DELETE /api/v1/chat/attachments/{id}` 保持不变。
- 新增 `POST /api/v1/chat/attachments/files`，multipart 字段名为 `file`，返回 `ChatFileAttachmentDto`。
- 聊天请求继续使用 `attachment_ids`；服务端依 ID 的实际附件类别分流，前端不传路径、提取文本或类别裁决。

### DTO 与服务端运行态

- `ChatFileAttachmentDto`：`id`、`file_name`、`content_type`、`size_bytes`、`kind`（`document`）、`extraction_status`（`ready` / `unsupported`）。
- `ChatCompleteRequest` 新增仅服务端字段：已解析文档附件、`ServerAttachmentContext`。这些字段使用 `JsonIgnore`，不能由浏览器输入。
- 文档附件服务负责暂存、解析资格判断、会话迁移、读取原件和删除；会话消息元数据只保存 DTO，不保存 `LocalPath`。
- `CodexChatService.BuildPromptText` 拼接 `ServerAttachmentContext`。它只传文本，`BuildTurnInput` 仍只为图片生成 `localImage`。

## 5. 前端交互

1. 附件按钮允许选择图片和支持的文件；粘贴板、拖放区也会收集 `File` 项。
2. 图片显示缩略图；文档显示文件图标、文件名、大小和“文本提取”或“需转换”状态，可单独移除。双击“文本提取”状态可查看实际会发送给 Codex 的受限文本预览；预览接口只接受不透明附件 ID，不返回服务器路径。
3. 附件需选择 Codex 本地代理；图片还受现有模型原生视觉/OCR 开关控制，文档不依赖图片开关。
4. 发送时没有输入文字则使用“请分析我附上的文件。”作为默认请求；上传或发送失败保留可读错误，不吞掉文字聊天。
5. 历史消息中图片仍可预览；文档仅显示安全元数据，不暴露服务器存储位置。

## 6. 验收用例

1. 选择、粘贴和拖放 PNG：既有图片预览、移除、历史回看和 `localImage` 行为不回归。
2. 上传 DOCX/XLSX/PPTX/Markdown/CSV，Codex 收到带文件名的受限文本上下文，消息元数据无绝对路径。
3. 上传 PDF：解析 worker 可用时注入文本；不可用或解析失败时仅该附件报错，不能伪造已理解。
4. 上传 `.doc` / `.xls` / `.ppt`：可见“需转换”状态，发送被拒绝且提示转换格式。
5. 扩展名伪造、ZIP 炸弹式超大包、非允许 MIME/签名、跨用户 ID、过期 ID、超过大小/数量均被拒绝。
6. 聊天记录、日志、SSE/WebSocket 事件和数据库 metadata 不出现附件绝对路径、全文或密钥。
7. 第三方 Codex profile 不得到 `localImage`；文档仍只按不可信文本降级。

## 7. 已知限制与后续

- 本期不支持扫描 PDF OCR、旧 OLE Office 文档内容、受保护文档、Office 内嵌图片/图表/公式/备注的语义保真。
- PDF 的视觉布局和 Office 幻灯片图像不是 app-server 原生多模态输入；需要视觉理解时，用户应上传页面/幻灯片截图。
- 若要支持旧 Office 或视觉丰富文档，需要受控的专用转换/抽取服务、隔离执行、恶意样本扫描和单独的容量治理，不能把任意服务器路径传给 Codex。
