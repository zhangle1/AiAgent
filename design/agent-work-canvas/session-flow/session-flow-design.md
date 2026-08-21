# 无限画布：会话信息流、Skill 与职责契约设计

> 状态：概念验证 / 可交互原型  
> 原型：[session-flow-prototype.html](./session-flow-prototype.html)  
> 依赖：现有多会话工作画布、聊天会话权限、Codex 运行时与并发控制

## 1. 设计结论

可以在无限画布中提供信息流转，但不应把连线实现成“两个 Agent 自动互读全部历史”。推荐引入一个显式、可预览、可审计的中间对象：**上下文包（Context Packet）**。

每条信息流边描述四件事：

1. **何时传**：上游完成、人工确认、每次更新，或仅手动传递；
2. **传什么**：结构化摘要、关键结论、交付物引用、未决问题；
3. **如何进入下游**：作为启动输入、补充上下文，或仅供参考；
4. **失败怎么办**：阻塞下游、使用上一版本，或转人工确认。

会话节点则增加一份**运行契约（Session Contract）**：角色与职责、绑定 Skill、允许工具、输入要求、输出格式、边界约束和完成标准。这样画布从“多会话监控台”进一步成为可理解、可控制的轻量工作流，但仍不等同于无人值守的通用 Agent 编排器。

## 2. 核心对象

### 2.1 会话节点

节点仍引用真实聊天会话，并增加以下可选配置：

| 配置 | 示例 | 作用 |
| --- | --- | --- |
| 角色 | 需求分析师 | 帮助用户理解该节点为什么存在 |
| 职责 | 澄清范围、产出验收标准 | 限定本会话必须完成的工作 |
| Skills | PRD 拆解、需求澄清 | 注入受控能力说明与执行规范 |
| 输入契约 | 必须收到需求摘要、风险清单 | 运行前校验上游信息是否齐备 |
| 输出契约 | `prd-summary@v1` | 让下游稳定识别结果，而非猜测自然语言 |
| 约束 | 不修改代码；信息不足必须提问 | 限制自主行动边界 |
| 完成标准 | 验收条件无歧义且用户确认 | 决定何时可发出“完成”上下文包 |

节点头部只展示角色和最多 2 个 Skill，完整契约在右侧检查器编辑。节点底部展示输入/输出端口与状态，避免卡片过载。

### 2.2 信息流边

信息流边是有方向的：`source → target`。建议与现有仅表达关系的边并存：

| 类型 | 是否携带内容 | 用途 |
| --- | --- | --- |
| `related_to` | 否 | 仅表示相关 |
| `depends_on` | 否 | 表示顺序或依赖 |
| `context_flow` | 是 | 传递一个版本化上下文包 |
| `artifact_flow` | 是 | 传递已授权的交付物引用 |
| `approval_gate` | 是 | 人工确认后才可继续 |

边标签应显示传递策略与最近状态，例如“完成后 · 4 项”“等待确认”“v3 已送达”。动态粒子只表示本次传递事件，不持续播放，避免误导用户认为 Agent 正在实时互聊。

### 2.3 上下文包

```json
{
  "packet_id": "pkt_...",
  "schema": "context-packet@v1",
  "source_session_id": "...",
  "source_run_id": "...",
  "target_session_id": "...",
  "version": 3,
  "summary": "已确认导出范围与权限模型",
  "decisions": ["仅管理员可导出全部数据"],
  "open_questions": ["导出文件保留多久？"],
  "artifacts": [{ "kind": "markdown", "resource_id": "...", "label": "需求草案" }],
  "redactions": ["attachment_physical_path", "raw_tool_output"],
  "created_at": "2026-08-20T09:30:00Z"
}
```

上下文包是不可变版本；重新生成会产生 `v2/v3`。目标会话保存“实际消费的版本”，这样可以回答“本次结果基于哪个上游结论”。它只保存摘要和资源引用，不保存物理路径、密钥、完整附件、完整聊天历史或未经裁剪的工具输出。

## 3. 关键交互

### 3.1 创建信息流

1. 从源节点的输出端口拖到目标节点的输入端口；
2. 弹出“配置信息流”面板，选择触发方式和内容范围；
3. 系统检查两端权限、项目边界和输入/输出契约；
4. 用户预览首个上下文包并确认；
5. 边进入“已就绪”，目标节点显示预计收到的输入。

默认值为“上游完成后生成草稿，人工确认再送达”。只有相同用户、相同授权范围且风险较低的流程，才允许用户显式开启自动送达。

### 3.2 目标会话消费信息

- **未启动**：上下文包进入启动输入区，用户可删减后启动；
- **运行中**：进入待处理队列，不中断当前轮次；用户选择“下一轮带入”；
- **等待输入**：可作为补充信息继续当前会话；
- **已完成**：默认创建后续轮次，不静默改写已完成结果。

目标会话的聊天记录中插入一条系统可视消息：“收到来自「需求分析」的上下文包 v3”，可展开查看来源、内容、删减记录和消费时间。

### 3.3 配置 Skill 与职责

右侧检查器分为四个页签：

- **概览**：状态、上下游、最近传递和运行按钮；
- **信息流**：输入包、输出映射、触发策略和失败策略；
- **Skills**：Skill 搜索、版本、参数、工具授权和冲突提示；
- **职责约束**：角色、职责、禁止事项、输出格式和完成标准。

Skill 绑定采用“引用 + 版本快照”，不把 Skill 全文复制到节点。运行时服务端解析可用版本、权限和依赖；Skill 不存在或版本不兼容时节点进入 `configuration_error`，不能偷偷降级运行。

## 4. 状态模型

### 4.1 节点状态

```text
draft → ready → queued → running → waiting_input → completed
                  ↘ configuration_error / failed / stopped
```

新增的 `ready` 表示输入契约已满足；`configuration_error` 表示 Skill、权限或约束配置不可执行。节点运行状态仍以真实会话/Run 为事实来源。

### 4.2 信息流状态

```text
idle → collecting → draft_ready → awaiting_approval → delivered → consumed
          ↘ generation_failed       ↘ rejected       ↘ stale
```

- `delivered` 不等于 `consumed`；只有目标运行真正引用该版本后才算消费。
- 上游产生新版本时，旧包可标记 `stale`，但不能修改已经完成的目标 Run。
- 自动传递失败时不得无限重试；应遵守次数、退避和人工兜底策略。

## 5. 运行语义与调度边界

首期建议只做**信息传递 + 人工启动**，不做完整 DAG 自动调度。第二期可开放受控的“输入齐备后排队”，但必须满足：

- 服务端持久化 Run、上下文包和消费记录；
- 并发槽位由后端统一分配，浏览器不自旋重试；
- 每个节点声明最大运行次数、超时和失败策略；
- 循环依赖在保存连线时阻止，或要求显式循环策略和次数上限；
- 多个上游到达时按输入端口聚合，不以消息到达先后决定 Prompt 顺序。

推荐的 Prompt 组装顺序固定为：系统安全策略 → 节点职责契约 → Skill 指令 → 用户当前指令 → 已确认上下文包 → 资源引用。各层边界明确，外部上下文始终标记为不可信数据，不能覆盖系统与权限策略。

## 6. 数据模型提案

在现有 `AiWorkCanvasNode/Edge` 外增加独立实体，避免把复杂运行状态塞入 `metadata_json`：

| 实体 | 关键字段 |
| --- | --- |
| `AiCanvasSessionContract` | node, role, responsibilities, constraints, input_schema, output_schema, completion_criteria, version |
| `AiCanvasNodeSkill` | node, skill_key, requested_version, resolved_version, config_json, order, enabled |
| `AiCanvasFlowPolicy` | edge, trigger, delivery_mode, content_selector, failure_policy, require_approval, version |
| `AiContextPacket` | edge, source_run, version, schema, summary_json, status, content_hash, created_by |
| `AiContextPacketDelivery` | packet, target_session, target_run, delivered_at, consumed_at, decision, redaction_json |

所有 ID 均由服务端验证归属；契约和策略使用乐观版本。上下文包写入审计日志，但审计中只保留 ID、hash、状态和操作人，不重复保存正文。

## 7. API 草案

| 方法 | 路径 | 作用 |
| --- | --- | --- |
| `GET/PUT` | `/api/v1/work-canvas/nodes/{nodeId}/contract` | 获取/更新职责契约 |
| `GET/PUT` | `/api/v1/work-canvas/nodes/{nodeId}/skills` | 解析并保存 Skill 绑定 |
| `POST` | `/api/v1/work-canvas/edges/context-flow` | 创建信息流并验证环路/权限 |
| `POST` | `/api/v1/context-packets/preview` | 生成脱敏预览，不送达 |
| `POST` | `/api/v1/context-packets/{id}/deliver` | 审批并送达 |
| `POST` | `/api/v1/context-packets/{id}/consume` | 启动/继续会话并记录消费版本 |
| `GET` | `/api/v1/work-canvas/{id}/flow-runs` | 获取画布级运行和传递摘要 |

接口返回服务端计算的 `capabilities`，前端仅据此显示操作入口。生成上下文包应是后端能力，不能由浏览器拼接完整历史。

## 8. 安全与治理

1. 两端会话和引用资源在**生成、送达、消费**三个时点都重新鉴权；失权后不可凭旧边继续读取。
2. 跨项目默认只传结构化摘要；交付物必须是受控 ResourceRef，并在目标打开时再次校验。
3. Skill 不能扩大用户权限。工具白名单取“用户权限 ∩ 会话权限 ∩ Skill 所需权限 ∩ 节点约束”。
4. 上游输出属于不可信输入，必须防止其中的 Prompt Injection 覆盖节点职责或系统策略。
5. 自动送达、自动排队、约束修改、Skill 变更和人工覆盖都写入审计。
6. 提供“为什么运行/为什么阻塞/使用了什么输入”的可解释视图；任何自动化必须可暂停和单节点跳过。

## 9. 原型说明

原型以“研究需求 → 设计方案 → 工程实现 → 安全审查”为示例：

- 点击任意节点查看其职责、Skill 和输入输出；
- 点击连线标签查看上下文包内容与传递策略；
- 切换检查器页签查看信息流和 Skill 配置；
- 点击“模拟运行”观察节点和边的状态变化；
- 点击“编辑契约”打开职责配置抽屉。

## 10. 分期建议

### Phase 1：可解释的手动流转

- 创建 `context_flow` 边；手动生成、预览、删减、送达上下文包；
- 节点配置角色、职责、约束和 Skill 引用；
- 目标聊天展示来源与消费记录，不自动启动。

### Phase 2：契约校验与受控排队

- 输入/输出 schema、Skill 版本解析和配置错误；
- 上游完成后自动生成草稿；用户确认后目标进入服务端队列；
- 画布级 Run/Packet 时间线与重放定位。

### Phase 3：模板与有限自动化

- 将节点契约和信息流保存为画布模板；
- 低风险流程允许自动送达，支持超时、重试和人工闸门；
- 成本预算、循环保护、批量暂停和审计导出。

## 11. 验收重点

- 用户能在 10 秒内看懂每个节点“负责什么、需要什么、会产出什么”；
- 任一目标结果都能追溯到实际消费的上下文包版本与来源 Run；
- 未确认的草稿包不会进入目标 Prompt；
- Skill 缺失、版本冲突、权限不足和输入不完整均明确阻塞，不静默忽略；
- 删除画布节点或连线不删除真实聊天历史和既有审计记录；
- 信息流不能绕过项目、会话、附件和工具权限。
