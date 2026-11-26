# 聊天勾选知识目录后，Codex 怎样结合书籍审查代码

当前本地实现及复测方式已记录在[聊天知识目录契约](knowledge-chat-scope.md)。以下分析记录改动前的链路与方案。

**改动前的结论：AiAgent 已有 LLM Wiki 检索，但项目 Codex 聊天没有把本轮勾选的知识目录转成模型可用的书籍证据。** 对截图中的提问，再强调一次“结合我选中的知识目录”，也不能让这条调用链自动取得《Factory Physics》的章节。优先改动应放在聊天服务端准备本轮上下文的位置。

本文依据本地 AiAgent 工作目录的源码（Git 基线 `059fba58`，含未提交修改）和两张截图。未读取用户书籍、数据库或那一轮运行日志。下文可以解释截图中的行为，无法据此认定该书已经生成哪些 Wiki 页，也无法验证回答中关于报工代码的判断。

## 一次提问在哪里断开

| 环节 | 当前代码事实 | 对截图的意义 |
| --- | --- | --- |
| 确认目录 | 桌面选择器保存勾选 URI，从可见后代文件推导知识库名。发送时把 `knowledge_scope_uris`、旧单值字段、知识库名和代码项目一起交给流请求。 | “已选 1”表示选择状态。用户无需在消息正文里再粘贴路径。仅凭截图不能确认那次请求的实际载荷。[选择与发送](../front/components/chat/KnowledgeChatHome.tsx#L1050) |
| 进入聊天编排 | Codex 命中默认 Provider 后直接执行。只有继续走普通 Agent 路径才创建携带 Scope 的 `AgentContext`。Scope 也会进入会话偏好和投递指纹，这两处只用于恢复选择和识别请求。 | 最近的 Provider 改造统一了执行入口，知识检索仍是另一层职责。[聊天路由](../backed/Services/Chat/Orchestration/ChatOrchestrator.cs) · [上下文](../backed/Services/Chat/Agentic/AgentContext.cs#L107) · [投递指纹](../backed/Services/Chat/Codex/CodexTurnControl.cs#L11) |
| Codex 实际输入 | `BuildPromptText` 合并项目引用、显式 Markdown、项目 Agent 文档索引、附件和记忆。这个列表没有选中知识范围的章节、Wiki 摘录或知识引用。 | 模型能查看授权代码仓库及其中的 `docs/context`。回答里提到仓库文档，只能证明它声称使用了该来源，不能证明读过所选书籍。[提示构造](../backed/Services/Chat/Codex/CodexChatService.cs#L714) |
| 普通知识检索 | 知识搜索工具被调用时，服务端从当前可见资源树把 Scope 解析成文档 ID，再按配置进入 Wiki 或 RAG。Wiki 查询在选中的文档 ID 内取页并返回引文。 | LLM Wiki 已经接入支持该检索工具的聊天路径。“工具可用”和“本轮确实取回引文”还要分开观察。[工具分发](../backed/Services/Chat/Agentic/ToolDispatch.cs#L124) · [检索分支](../backed/Services/Chat/Retrieval/KnowledgeRetrievalService.cs#L53) · [现行 Scope 契约](knowledge-chat-scope.md) |

## 《Factory Physics》这里还有第二个问题

截图勾选的是“解析正文 → Factory Physics”，下方同名 PDF 是另一个条目。资源服务把解析章节投影为目录和文件，并记录来源文档 ID、来源 URI 和解析版本。因此这次选择表达的是“可以参考这本书的解析内容”。[解析正文投影](../backed/Services/Knowledge/KnowledgeResourceService.cs#L123)

当前 Scope 解析收集所选目录后代的文档 ID。Wiki 接下来从可见的 `AiKnowledgeArtifact` 生成页面并按文档 ID 过滤。**有可读章节，不代表有可检索 Wiki 页。** 而且选中某一章所在的子目录后，如果只保留文档 ID，Wiki 查询最多能限定到这本书，不能保证引文来自那一章。当前没有可用 Wiki 页时给出的“请先解析文档”提示，也无法区分“已有正文，缺少 Wiki 页”。[范围解析](../backed/Services/Chat/Retrieval/KnowledgeRetrievalService.cs#L263) · [Wiki 页来源](../backed/Services/Knowledge/KnowledgeWorkspaceService.cs#L37) · [空结果与引文](../backed/Services/Knowledge/KnowledgeWikiRetrievalService.cs#L19)

换成 RAG 也不能直接解决截图的问题。Codex 仍走同一条执行分支。即使进入普通 RAG 搜索，当前实现也是先在库内取 Top K，再按 Scope 过滤。相关章节如果排在候选集之外，结果可能为空。[RAG 候选与过滤](../backed/Services/Chat/Retrieval/KnowledgeRetrievalService.cs#L80)

## 当前首版：投递可读目录，由 Codex 自行翻书

对“结合我选中的知识目录，看现有代码有哪些可以改善”的提问，这条路线更短。用户已经选好了书，项目 Codex 也可以查看代码。宿主只需告诉它“这次可以读哪些材料、从哪里开始”，由同一轮 Codex 决定读哪些章节。这里省去的是服务端预检索、排序和额外模型调用。阅读哪些章节仍由 Codex 自主决定。

以截图为例，选择“解析正文 → Factory Physics”时，交给 Codex 的应是**本轮可读取的《Factory Physics》章节目录**。只把书名或界面里的 `viking://…` 拼进提示词，CLI 仍然打不开。AiAgent 的目录是虚拟资源树；解析文件有自己的物理目录、解析版本和来源校验。现有文件接口可按 URI 与解析版本校验并解析具体章节，适合复用这层判断。[资源树与章节投影](../backed/Services/Knowledge/KnowledgeResourceService.cs#L123) · [授权文件解析](../backed/Services/Knowledge/KnowledgeResourceService.cs#L427)

### 发送时做什么

1. **复用当前勾选。** 前端继续提交 Scope URI。后端在实际执行时，按当前用户的可见树解析选择。先支持截图中已经解析的书籍目录；直接选 PDF 时，如有受控解析目录就指向相应版本。如无可读章节，告诉用户该书当前可提供的材料状态。
2. **生成可浏览的目录入口。** 单本书已有独立、不可变的章节目录时，确认其全部内容均属于本次获准范围、当前 CLI 运行位置可读，再交付目录入口。多选、选子目录或底层目录混有其他内容时，用本轮临时目录只投影获准的章节，保留原章节层级和相对链接。清单记录书名、章节相对路径和解析版本。运行根目录仍为当前代码项目。若 CLI 与存储分机，需在执行端提供对应投影；仅传后端盘符不能完成交付。
3. **给一个具体的阅读任务。** 例如：“选中的参考书为《Factory Physics》，目录入口为〈本轮可读目录〉。先浏览目录，再结合当前项目选一个值得核查的问题，按需打开相关章节和代码。每条有书籍依据的建议给出实际读过的章节相对路径、代码文件及验证办法；材料没有提供依据时请直接说明。”这样即便原问题没有“搜索”两字，Codex 也知道目录的用途。
4. **只记录可证明的状态。** 目录成功交付后显示“已提供 1 个书籍目录”。只有取得文件读取记录时，才显示“已读某章”。书籍文本按参考资料传入，仓库和知识库的写权限沿用已有策略。失效目录或 CLI 无法读取时要显式提示，避免模型自行把仓库文档当作所选书籍。

### 放在哪一层

放在项目聊天的执行准备层，给 Codex 增加一个仅服务端构造的“本轮参考目录”输入。已选 URI 用于排队和身份检查，执行端路径只在运行时解析。普通发送、HTTP／WebSocket 流式发送以及原生追加都要覆盖；临时投影如用于追加，至少保留至当前轮次终态，不能在追加收到确认时清理。打开选择器、命令回放和空 Scope 都无需创建目录。同步更新旧的 Scope 契约，明确目录交付的适用范围。[Codex 输入拼装](../backed/Services/Chat/Codex/CodexChatService.cs#L714) · [追加入口](../backed/Services/Chat/Codex/CodexChatService.Steering.cs) · [投递生命周期](chat-message-delivery.md)

### 怎么证明做到了

| 用例 | 首版验收 |
| --- | --- |
| 用截图中的问题，选一本含唯一规则编号的合成书 | 在实际 CLI 工作目录和沙箱下成功列出书籍目录、打开对应章；提示词只给目录，不预埋规则编号。记录真实读取，再核对建议是否同时指向书章和代码。 |
| 只选一章、选两本同名书、另有他人私有书籍 | 交付清单严格匹配获准范围，可区分同名书。子目录选择不能通过交付整个父目录扩大。 |
| 空选择、未解析、权限撤销、目录不存在 | 空选择沿用现有聊天。其余情况返回可辨认的状态，不生成虚构引文。 |
| 消息排队后改变勾选，或向当前任务追加材料 | 各条消息使用自己提交的选择，真正执行前检查访问权；追加材料在运行中可读，回放不会重新读取或启动模型。 |

### 后续接 Wiki 的位置

保留“选择 → 服务端解析 → 向当前轮次提供参考材料”这一处入口。第一版供应章节目录；后续 Wiki 可用时，同一入口可以再供应 Wiki 概览、主题页和原文章节链接。届时再根据实测的阅读耗时、选章准确率决定是否增加宿主检索。当前可先用一个判断评估效果：同一问题，有无所选书籍时，Codex 给出的建议能否指出可复核的书章和代码差异。
