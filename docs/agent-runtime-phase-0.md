# Agent Runtime Phase 0 implementation

This slice establishes the vendor-neutral runtime boundary described in
`design/agent-platform-runtime/agent-platform-runtime-design.md` without replacing
the production chat loop.

## Delivered

- Versioned `IRuntimeEngine` boundary with normalized run, input, capability,
  result, and event contracts.
- `RunCoordinator` with explicit queued/starting/running/terminal transitions,
  duplicate run rejection, monotonic event sequencing, cancellation, failure,
  and runtime/protocol version snapshots.
- `NativeAgentRuntime`, a transitional adapter around the existing `IAgentLoop`.
  The adapter is intentionally the only new-runtime component that references
  legacy loop types.
- Event projection between stable runtime events and the existing chat stream.
- `AgentRuntime:NativeEnabled` feature flag. It defaults to `false`.

## Compatibility boundary

`ChatOrchestrator` still dispatches `codex` and `deepseek-harness` before it
considers the native feature flag. Those external agents therefore retain their
existing services and protocols. With the flag disabled, ordinary chat also
continues to call `IAgentLoop` directly.

## Next slice

Persist `TurnRunSnapshot` and append-only runtime events, add run read/cancel
APIs, then move model/tool iteration from `AgentLoop` into native runtime
components. Do not enable the feature flag by default until parity tests cover
stream output, attachments, tool behavior, timeout, and cancellation.
