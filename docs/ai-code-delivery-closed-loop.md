# AI 代码交付闭环设计与验收

## 目标

把 Agent 对受控代码仓库的文件修改提升为可审计的交付对象：创建变更集 → 自动校验 → 生成摘要 → 人工审批 → Git 提交/推送 → 回写任务。任何提交都必须来自已审批且未漂移的工作区快照。

## 状态机

`draft` → `validating` → `pending_approval` → `approved` → `delivering` → `delivered`

失败状态为 `validation_failed`、`delivery_failed`，人工拒绝为 `rejected`。失败的变更集保留校验和交付日志，可重新创建快照，不允许绕过审批直接交付。

## 核心契约

- 变更集绑定当前用户、一个 AiAgent 项目和一个可选任务。
- 创建时枚举项目内所有存在本地修改或待推送提交的受控仓库，记录分支、文件清单、Diff 摘要与工作区 SHA-256 指纹。
- 自动校验至少执行 Git 仓库检查、上游/落后检查和 `git diff --check`。校验命令由服务端固定参数调用，不接受浏览器或 Agent 提供任意 shell。
- 所有仓库校验通过后生成确定性摘要并进入 `pending_approval`；关联任务同步进入 `in_review`。
- 审批人必须具备代码提交权限。审批记录审批人、时间和意见。
- 交付前重新计算每个仓库指纹；与审批快照不一致则拒绝交付，要求创建新变更集重新校验审批。
- Git 提交信息来自变更集，限制长度；提交/推送仍通过共享 Git 服务及其受控凭据通道执行。
- 全部仓库推送成功后状态为 `delivered`，记录 commit SHA；关联任务更新为 `done`。部分失败保持 `delivery_failed`，任务不置为完成。
- API 不返回真实仓库绝对路径、凭据或未经清洗的敏感输出。

## 数据模型

### `ai_code_change_set`

保存项目、任务、标题、提交信息、摘要、状态、创建人、审批人/意见/时间、创建/校验/交付时间和错误摘要。新增列均允许为空以兼容现网数据库。

### `ai_code_change_set_repository`

保存变更集下每个仓库的仓库 ID/名称、分支、快照 SHA-256、文件清单 JSON、校验结果 JSON、校验状态、交付状态、commit SHA 与安全输出摘要。

## API

- `GET /api/v1/code-deliveries?projectId=&taskId=`：查询当前用户可见变更集。
- `GET /api/v1/code-deliveries/{id}`：获取详情。
- `POST /api/v1/code-deliveries`：创建快照并自动校验、生成摘要。
- `POST /api/v1/code-deliveries/{id}/validate`：在未审批状态重新快照和校验。
- `POST /api/v1/code-deliveries/{id}/approve`：批准或拒绝。
- `POST /api/v1/code-deliveries/{id}/deliver`：校验快照后提交并推送，成功后回写任务。

## 前端

任务板提供“交付”入口和交付抽屉：创建变更集、查看文件/校验/摘要、填写审批意见并批准或拒绝、执行提交推送、查看每仓库结果。所有请求集中在 `front/lib/code-delivery-api.ts`，类型集中在 `front/lib/code-delivery-types.ts`。

## 安全与并发

- 项目访问继续复用 `IProjectAccessService`；对象查询同时限制创建用户，管理员/代码提交权限仅扩大审批和交付动作，不扩大项目可见范围。
- 每个项目使用服务端互斥通道，防止校验、审批和交付并发穿透。
- 指纹覆盖 HEAD、分支、Git porcelain 状态和完整工作区 Diff（含未跟踪文件内容哈希），避免只比较时间戳。
- 禁止空变更集、远端领先、无上游或远端刷新失败的仓库进入待审批状态。

## 验收用例

1. 有修改的项目创建变更集后得到文件列表、摘要和通过的校验项，任务进入 `in_review`。
2. `git diff --check` 失败时状态为 `validation_failed`，不能审批或交付。
3. 无 `CanCommitCode` 的用户可创建和查看自己的变更集，但不能审批或交付。
4. 审批后再次修改任一文件，交付返回快照漂移错误且不执行 `git add/commit/push`。
5. 推送全部成功后保存 commit SHA，变更集为 `delivered`，任务为 `done`。
6. 任一仓库推送失败时保留安全日志，状态为 `delivery_failed`，任务不变为 `done`。
7. 后端构建和前端 lint/typecheck 通过，API DTO 与 TypeScript 类型字段一致。
