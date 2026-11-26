# AiAgent 的 Agent Provider 接口设计

**建议先在 AiAgent 的 .NET 后端引入 `Driver → Instance`，首版只接入和验收 Codex。** Driver 负责把一份配置装配成可用实例，Instance 向聊天编排提供状态和执行入口。首版实例复用现有 Codex 执行模块，等续接、追加或切换确实需要共享语义，再把 Session 提升为公共接口。

## T3 Code 的设计为什么值得借鉴

| 层 | 调用方要知道什么 | 这一层消化的复杂性 |
| --- | --- | --- |
| `ProviderDriver` | 驱动类型、配置规则、默认值和创建方法 | 每种驱动如何把配置、环境变量和依赖装配起来 |
| `ProviderInstance` | 实例 ID、状态、续接身份和执行 Adapter | 某份具体配置对应的模型、登录状态、认证与可选能力 |
| `ProviderAdapterV2` / Session | 如何打开会话、开始一轮、读取事件或中断 | 原生协议、会话状态和进程如何工作 |

最值得借的是**实例的身份与原生会话的续接身份分开**。`instanceId` 选中一份配置，`continuationIdentity` 表示原生会话可能在哪个范围内复用。T3 Code 默认按实例隔离，Codex 可以按共享 Home 为不同实例生成相同的续接分组。切换时，编排仍会检查驱动、可用性、工作区和模型切换策略，再决定原地切换、重启并续接或新建会话并交接上下文。[Driver 与 Instance](../../t3code/packages/provider-core/src/server/driver.ts) · [Codex Home 布局](../../t3code/apps/server/src/provider/Drivers/CodexHomeLayout.ts) · [会话切换策略](../../t3code/apps/server/src/orchestration-v2/ProviderSessionTransitionPolicy.ts)

另一处值得借的是**注册表负责创建和回收**。它先解码配置、解析环境中的密钥引用，再在自己的作用域内创建实例，创建失败时关闭该作用域并记录不可用状态。运行一轮时，编排通过实例拿到 Adapter，由 Session 执行 `startTurn`。这样设置页和聊天入口都能使用同一个实例身份，各驱动仍掌握自己的原生协议。[实例注册表](../../t3code/apps/server/src/provider/ProviderInstanceRegistry.ts) · [执行接口](../../t3code/packages/provider-core/src/server/ProviderAdapter.ts) · [实际执行入口](../../t3code/apps/server/src/orchestration-v2/RunExecutionService.ts)

## AiAgent 适合从哪里接入

| 当前模块 | 对应的改造切口 |
| --- | --- |
| `ChatOrchestrator` 在流式和非流式入口分别识别 Codex、DeepSeek Harness。原生聊天按开关进入 `RunCoordinator` 或现有 `AgentLoop`。 | 把 Codex 的两处判断收拢为同一个实例解析入口。 |
| `AgentProviderEnvironmentService` 集中探测 CLI，现有 Codex 执行模块封装配置解析和进程生命周期。 | 由 Codex 实例复用探测逻辑提供状态，设置页通过现有环境列表展示 Codex 快照。 |
| `AiModelProvider` 管理模型连接，`IRuntimeEngine` 和 `RunCoordinator` 已提供原生运行与事件契约。 | Agent Provider 选择由谁执行，模型 Provider 定义模型请求接到哪里。保留两者的身份，避免在 `AiModelProvider` 塞入 CLI 进程、会话和工作区配置。 |
| `ChatDeliveryService` 已保存队列与原生绑定，`ChatCommandStore` 已处理命令登记与事件回放。 | Agent Provider 接在实际执行一侧。排队、去重、历史落库和回放继续由聊天层负责。 |

[聊天编排](../backed/Services/Chat/Orchestration/ChatOrchestrator.cs) · [CLI 环境探测](../backed/Services/Chat/AgentProviderEnvironmentService.cs) · [模型 Provider](../backed/Entities/Settings/AiModelProvider.cs) · [运行契约](../backed/Services/AgentRuntime/RuntimeContracts.cs) · [聊天投递与恢复](chat-message-delivery.md) · [命令恢复](chat-command-recovery.md)

## 首版 C# 接口草案

```csharp
public interface IAgentProviderDriver
{
    string Kind { get; }

    ValueTask<IAgentProviderInstance> CreateAsync(
        AgentProviderCreateContext context,
        CancellationToken cancellationToken);
}

public interface IAgentProviderInstance
{
    string Id { get; }

    ValueTask<AgentProviderSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken);

    Task<ChatCompleteResponse> ExecuteAsync(
        ChatCompleteRequest request,
        AgentStreamEventHandler? onEvent,
        CancellationToken cancellationToken);
}
```

- **Driver** 通过 .NET DI 注册。`AgentProviderCreateContext` 由服务端构造，包含实例 ID 和该驱动需要的配置。首版复用现有 `Codex` 配置来源和解析逻辑。注册表负责驱动查找、实例解析和创建失败时的状态投影。
- **Instance** 代表一份服务端配置，可跨多轮使用。`ExecuteAsync` 首版沿用已有请求、事件和响应类型。流式调用传事件处理器，非流式调用传 `null`。Codex Adapter 委托给 `ICodexChatService`，由后者管理进程与租约。
- **Snapshot** 提供给设置页和 Agent 选择器，首版可以有实例 ID、驱动类型、安装状态、当前可用性、版本及可公开的原因。只有已经被调用方消费的能力才加到快照。涉及模型、工作区或权限的能力，执行时仍按当前请求判断。状态探测只做只读检查，打开会话和启动执行由 `ExecuteAsync` 触发。

```text
设置页 ────────────────→ Registry → Instance.GetSnapshotAsync

Codex 请求 → 聊天准入与访问校验 → ChatOrchestrator
                                  → Registry → Instance.ExecuteAsync
                                             → ICodexChatService
```

首版将现有 `agent = "codex"` 映射到服务端配置的默认实例。等设置页支持多实例后，再引入显式选择，并把准入时解析出的实例身份纳入排队命令绑定，避免执行时因更改默认值而跑到另一个实例。实例被禁用、配置失效或没有访问权限时，按该实例的失败原因结束处理。

## 什么时候值得抽出 Session

当编排确实需要跨驱动统一处理续接或会话切换时，再引入 `OpenSessionAsync(...) → IAgentProviderSession`，把 `StartTurnAsync`、事件和中断放进 Session。追加、目标、上游认证和用量等能力，等有相应调用方时以专用接口提供。这样常规发送只需要了解启动、事件和终态，原生协议细节留在 Adapter 内。

以未来的 `codex-work` 和 `codex-personal` 为例，它们共享 `DriverKind = codex`，各有一个 `InstanceId`。即使选了相同模型，也默认属于不同的续接范围。只有 Driver 能确认共享原生会话存储，并且服务端核对用户、聊天会话归属、项目、规范化工作区及凭据与权限绑定后，才允许尝试原生恢复。若配置或权限变化，按当前模式明确提示、拒绝续接，或经用户确认创建新会话。

| 需要区分的身份 | 对编排的用途 |
| --- | --- |
| `DriverKind` / `InstanceId` | 选择执行实现和服务端配置。 |
| AiAgent 会话 ID / Provider 原生 thread ID | 前者关联平台历史与权限，后者仅在服务端绑定到具体 Provider 会话。 |
| `ContinuationIdentity` | 后续多实例阶段用来判断两份配置是否具备原生续接条件，默认按实例隔离。 |

Codex 普通排队已经通过投递记录保存原生绑定和 thread ID。追加仍对当前绑定的活动轮使用 `turn/steer`，追加确认与整轮完成各有独立状态。目标模式继续走 `GoalJson`、版本检查和 Codex 原生续跑，恢复失败时按现有语义报错。若未来支持实例配置在线更新，注册表负责其资源生命周期，活动轮继续使用已取得的实例或由现有停止流程中止，新轮次再进入更新后的实例。[普通聊天与原生绑定](../backed/Services/Chat/Codex/CodexChatService.cs) · [绑定存储](../backed/Services/Chat/Orchestration/ChatDeliveryService.cs) · [目标模式](chat-goal-mode.md)

## 实施顺序和验收

1. **先接入 Codex 默认实例。** 用同一处解析替换 `ChatOrchestrator` 中两处 Codex 路由判断。验收现有 `agent = "codex"` 在流式和非流式入口都能调用 `ICodexChatService`，并按各自的契约返回结果、处理取消及报告错误。
2. **再接入 Codex 状态快照。** 将实例快照映射回现有环境列表，验收 CLI 缺失和已安装时各有对应状态，探测失败时能给出可公开的原因。按实际聊天入口回归同命令回放、同会话排队、Codex 追加和目标暂停／继续。
3. **按实际需求增加多实例或公共 Session。** 出现第二份配置时再设计实例权限、持久化和配置变更；出现共享续接行为时再抽会话契约。验收两个同驱动实例可以独立选择，排队期间切换默认配置不会改写已接受的命令，跨用户或跨工作区恢复会被拒绝。

首版的判断标准是，Codex 的配置装配、状态读取和执行分发都有清晰的归属，流式与非流式入口能共用同一个实例解析。等第二个驱动有明确接入需求时，再检验新增实现是否能集中在 Driver 和 Adapter。

## 相关阅读

- [T3 Code 原理与 AiAgent 的整体对照](t3code-principles-and-aiagent-reference.md)
- [第三方 Agent 与 CLI 检测规范](third-party-agent-provider-spec.md)
