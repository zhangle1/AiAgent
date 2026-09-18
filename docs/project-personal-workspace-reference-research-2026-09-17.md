# 项目个人空间参考研究（2026-09-17）

## 结论

AiAgent 不需要推倒重做。现有项目、代码仓库、聊天附件、项目 Markdown 资料、知识库、Codex 本地执行和 Git 操作已经覆盖了大部分底层积木；缺少的是统一的项目空间模型和一致的页面入口。

建议把“项目”定义为上下文与权限容器，容器内并列管理：

1. Git 代码仓：代码修改、测试、diff 和提交仍走 Git；
2. 项目资料：用户上传的需求、会议纪要、SQL、截图和临时分析材料，不默认进入 Git；
3. AI 产物：Agent 生成的 HTML、Markdown、DOCX、XLSX、PPTX、PDF 等，可预览、下载，也可由用户显式转入项目资料或代码仓；
4. 项目知识：从资料或代码派生的可检索表示，由项目默认范围自动提供给聊天，同时允许用户查看和调整本轮范围。

## 当前 AiAgent 可复用能力

- 项目可以关联多个代码仓库，聊天请求已有项目 ID、代码仓选择和本地 Codex 执行模式。
- 项目资料链路已有 Markdown 文档浏览、上传、导入、下载、删除和文件引用解析接口。
- 聊天已有图片和文档临时附件；它适合“仅本轮使用”，不等同于项目长期文件空间。
- 知识库可以被聊天显式选择，但目前与项目资料、代码仓和 AI 产物不是同一个统一上下文。
- 原型工作室已有 HTML 保存与预览能力，说明“AI 产物”并非全新技术方向，但尚未成为通用项目能力。
- Git 状态、diff、pull、提交/推送和代码运行配置已经存在，应继续作为代码交付边界，而非让普通资料混入代码仓。

## 开源一手参考

### Open WebUI

Open WebUI 的 Workspace 设计允许任意文件夹成为工作区；工作区会同时限定文件浏览器、编辑器、Git、终端、新聊天与搜索范围。其 Knowledge 还区分“全文随消息注入”的 Notes 与“按需检索”的 Knowledge，并支持目录、重命名、增量目录同步和导出。值得借鉴的是统一项目范围与目录式资料管理，不应直接照搬其全部知识库管理界面。

- Workspace 官方文档：<https://github.com/open-webui/docs/blob/main/docs/ecosystem/computer/workspace/workspaces.md>
- Knowledge 官方文档：<https://github.com/open-webui/docs/blob/main/docs/features/workspace/knowledge.mdx>
- 文件访问控制实现：<https://github.com/open-webui/open-webui/blob/main/backend/open_webui/utils/access_control/files.py>

### AnythingLLM

AnythingLLM 将文档与聊天绑定到 workspace，近期还提供逐文件嵌入进度、Filesystem Agent 和文档生成 Agent，可生成文本、PDF、Excel、DOCX 和 PPTX。值得借鉴的是“工作区资料 + 生成产物”的用户心智和后台任务反馈；其代码开发、Git diff 与测试闭环不是主要优势。

- 官方仓库：<https://github.com/Mintplex-Labs/anything-llm>
- 官方发布记录：<https://github.com/Mintplex-Labs/anything-llm/releases>

### Cline

Cline 将每个任务作为独立工作单元，保存对话、代码变更、命令执行和决策，并通过 Git 快照提供 compare/restore checkpoint。值得借鉴的是任务过程可见性、diff 和可恢复性；它是 IDE 内的本地 Agent，不适合作为 AiAgent 的资料中心模型直接复制。

- 官方 Marketplace 说明：<https://github.com/cline/cline/blob/main/apps/vscode/README.marketplace.md>
- 官方任务文档：<https://github.com/cline/cline/blob/main/docs/core-workflows/task-management.mdx>

## 建议借鉴矩阵

| 需求 | 主要参考 | AiAgent 落点 |
| --- | --- | --- |
| 一个项目统一上下文 | Open WebUI Workspace | 项目成为聊天、资料、代码、运行环境的范围容器 |
| 上传并长期保存个人文件 | Open WebUI Knowledge / AnythingLLM Workspace | 项目资料空间，目录化、可预览、可下载、可引用 |
| AI 生成可下载文件 | AnythingLLM Document Generation | 独立 AI 产物区，不默认 Git 提交或知识化 |
| 改码、测试与提交 | Cline + 现有 AiAgent Git 能力 | 代码仓内执行，展示工具过程和 diff，提交仍需人工批准 |
| 临时文件只用于一次聊天 | 现有 AiAgent 聊天附件 | 保留“仅本轮”入口，并允许显式保存到项目 |
| 知识自动可用但范围透明 | Open WebUI attached knowledge | 项目默认知识范围 + 输入框可见的本轮范围控制 |

## 不建议

- 不建议把整个项目根目录无差别索引成知识库；代码、二进制、密钥、构建产物与个人资料需要不同规则。
- 不建议把所有上传文件写进 Git；业务资料与 AI 产物应默认保存在受控项目空间，只有用户显式选择才进入代码仓。
- 不建议把 Harness 与项目空间混为一个开关。项目空间是数据与上下文模型，Harness/本地 Codex 是执行通道；没有 Harness 时仍应允许上传、检索、生成和下载文件，只是不能访问用户电脑上的任意本地目录。
- 不建议第一期同时实现在线 Office 编辑、多人实时协作、全量目录同步和任意数据库写入。
