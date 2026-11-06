# 聊天主动技能

普通聊天输入框输入 `/`，按中文名称或英文命令搜索技能。选择后显示标签，可预览完整中文提示词和固定版本来源，再发送消息才启用。↑↓ 切换，Enter / Tab 选择，Esc 关闭；项目与文档搜索保留在菜单底部。也可在消息开头直接输入完整命令，例如 `/grill-me 我想改知识库`。

首批共 23 个，全部为独立编写的中文流程，参考 Matt Pocock 与 Anthropic 的对应技能，不安装原生技能、脚本或自动发现配置，不代表完整移植上游工具能力。

| 分类 | 命令 | 中文名称 | 用途 | 固定版本来源 |
| --- | --- | --- | --- | --- |
 | 需求 | /grill-me | 需求追问 | 通过逐个追问，把想法收敛成明确需求。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/productivity/grill-me/SKILL.md) |
 | 需求 | /to-spec | 整理需求规格 | 把讨论整理成可评审的需求规格。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/to-spec/SKILL.md) |
 | 需求 | /to-tickets | 拆分任务 | 把规格拆成可独立验收的实施任务。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/to-tickets/SKILL.md) |
 | 需求 | /to-questionnaire | 生成调研问卷 | 将待确认事项整理成便于回答的问卷。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/productivity/to-questionnaire/SKILL.md) |
 | 研发 | /research | 技术调研 | 比较技术方案、证据和适用条件。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/research/SKILL.md) |
 | 研发 | /code-review | 代码审查 | 按影响程度检查代码缺陷与风险。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/code-review/SKILL.md) |
 | 研发 | /diagnosing-bugs | 定位故障 | 用证据、假设和最小实验排查问题。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/diagnosing-bugs/SKILL.md) |
 | 研发 | /domain-modeling | 领域建模 | 厘清业务术语、实体、状态和约束。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/domain-modeling/SKILL.md) |
 | 研发 | /improve-codebase-architecture | 架构改进建议 | 识别耦合与职责问题，规划渐进式改进。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/improve-codebase-architecture/SKILL.md) |
 | 研发 | /tdd | 测试驱动方案 | 从行为和失败场景设计测试与实现顺序。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/tdd/SKILL.md) |
 | 协作 | /retro | 项目复盘 | 复盘事实、原因和可跟进的改进项。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/engineering/retro/SKILL.md) |
 | 协作 | /handoff | 会话交接 | 整理背景、决策、进度和下一步。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/productivity/handoff/SKILL.md) |
 | 学习 | /teach | 引导学习 | 结合实例与小练习逐步理解知识。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/productivity/teach/SKILL.md) |
 | 学习 | /wait-what | 通俗解释 | 用直白语言、例子和反例解释难点。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/productivity/wait-what/SKILL.md) |
 | 协作 | /writing-for-agents | 优化协作指令 | 把含糊要求改成清晰可执行的指令。 | [mattpocock/skills](https://github.com/mattpocock/skills/blob/4ae2317ddb22b046e26455e4f9c9b3aaaf62212d/skills/productivity/writing-for-agents/SKILL.md) |
 | 文档 | /doc-coauthoring | 协作文档 | 共同梳理结构、起草和评审文档。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/doc-coauthoring/SKILL.md) |
 | 文档 | /internal-comms | 周报与沟通稿 | 起草周报、公告、进展和情况说明。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/internal-comms/SKILL.md) |
 | 设计 | /frontend-design | 前端设计建议 | 将页面目标转成明确的视觉和交互方案。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/frontend-design/SKILL.md) |
 | 设计 | /brand-guidelines | 品牌规范梳理 | 把已有品牌资料整理为可用的规范。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/brand-guidelines/SKILL.md) |
 | 设计 | /theme-factory | 主题方案 | 为文档和页面设计协调的主题。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/theme-factory/SKILL.md) |
 | 研发 | /webapp-testing | 网页测试计划 | 设计用户流程、边界与回归检查。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/webapp-testing/SKILL.md) |
 | 研发 | /mcp-builder | MCP 工具设计 | 规划工具接口、错误处理和验证。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/mcp-builder/SKILL.md) |
 | 设计 | /canvas-design | 视觉创意简报 | 把抽象主题整理成可交付的视觉简报。 | [anthropics/skills](https://github.com/anthropics/skills/blob/ca45fc496a3436251f191354091d557da31c15b5/skills/canvas-design/SKILL.md) |

## 启用边界

- 只有用户选择并发送，或顶层消息开头的完整命令，才启用一个技能。普通话题、技能名称解释、正文引用、代码块和检索资料不触发。自然语言提及不由模型自动转换为调用，用户可以从菜单明确选择。
- 未选择时不向模型提供技能清单、描述或正文。服务端只接受 id/version，不接受客户端提供提示词、路径或 URL。未知 ID、版本和命令冲突明确报错。
- `/spec` 仍是文档引用；嵌入式原型编辑器不开放这批技能。技能与图形模式同时存在时提示择一。
- 需求追问、引导学习、协作文档显示“进行中”，后续消息携带相同显式选择；点“结束技能”或切换会话／项目后清除。刷新不自动恢复，可从菜单再次明确选择。单次技能发送后清空。流程状态属于当前页面，服务端不依据历史消息自动续开。
- 结束后的普通请求仅在已授权会话内检测历史技能快照，给模型明确的结束边界；这不会删除原生代理的历史上下文，也不是额外权限隔离。
- 原始消息保持原文，模型输入单独构建。消息 MetadataJson 保存完整服务端快照，包括提示词、版本、来源和哈希。自动重试沿用原请求；手动重试沿用原消息的 id/version，与当前输入框选择无关。已发布 v1 正文不可覆盖，升级必须保留旧版本解析能力。

## 来源与许可

Matt Pocock 固定提交 `4ae2317ddb22b046e26455e4f9c9b3aaaf62212d`（MIT）；Anthropic 固定提交 `ca45fc496a3436251f191354091d557da31c15b5`（本批选项 Apache-2.0）。逐项记录原始 SKILL.md 的 SHA-256；许可全文在 [chat-skills-licenses](chat-skills-licenses)。doc-coauthoring 使用固定提交 README 的 Apache 2.0 声明。

中文提示词移除了上游按主题触发、调用其他技能、自动创建工单／写临时文件、脚本及安装器。Office 文件技能及画布生成所需工具未整体移植；菜单中的视觉创意仅输出设计简报，品牌规范仅依据用户品牌资料。

## 实现与验证

后端 Resources/ChatSkills/catalog-v1.json 作为程序集资源发布，ChatSkillCatalog 统一解析。HTTP complete／stream 和 WebSocket 在授权及项目上下文处理后应用同一逻辑。GET /api/v1/chat/skills 需登录，用于菜单和预览，运行不请求 GitHub。普通 API、原生 Agent、Codex 与第三方代理复用 ServerPromptMessage，技能不进入自动发现目录。

验证：前端 `npx tsc --noEmit --incremental false`；`node --test front/tests/chat-skills.test.mjs`；后端 `dotnet test backed.tests --filter FullyQualifiedName~ChatSkill`。浏览器脚本 `front/tests/chat-skills-browser.mjs` 拦截全部 API／WebSocket 并使用合成数据，需本地 Playwright，可通过 PLAYWRIGHT_MODULE 指定包，通过 CHAT_TEST_URL 指定开发地址。未调用真实模型，不宣称已验证模型追问质量。
