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

## Phase 1 persistence slice

Implemented alongside Phase 0:

- owner- and session-scoped `ai_agent_run` snapshots;
- append-only `ai_agent_run_event` records with monotonic per-run sequence numbers;
- session run list, run detail, and owner-checked cancellation APIs;
- model, duration, token, tool-call, file-change, failure-code, runtime-version,
  and protocol-version projection in the chat diagnostics UI;
- redacted persistence: prompts, reasoning, attachment content, tool observations,
  credentials, and real file paths are not written to the run ledger.

Cancellation is intentionally process-local: an active run can be cancelled only
by its owner on the service instance executing it. Completed history is read-only.
The native runtime feature flag remains disabled by default until parity tests
cover stream output, attachments, tool behavior, timeout, and cancellation.

## Next slice

Move model/tool iteration from `AgentLoop` into native runtime components and add
cross-instance cancellation through a durable lease or queue before enabling the
feature flag by default.
