# KunBuddy client agent rules

Policy-Sync: 2026-10-20.kunbuddy.1

KunBuddy is the imported desktop client inside AiAgent. Follow the authoritative
../AGENTS.md repository delivery rules.
This directory has no independent Git repository or upstream publishing workflow.
Preserve unrelated AiAgent changes. CLAUDE.md mirrors these non-negotiables.

Renderer -> Preload IPC -> Electron Main -> Rust Host Core / Node Agent Runtime.
SQLite is owned exclusively by Rust host-core. Electron Main stays a thin orchestrator.
Preserve plugin permissions, sandbox boundaries, schemas, and RPC contracts.
Use the shared renderer primitives and the nearest scoped AGENTS.md.

KunBuddy owns com.knowwhy.kunbuddy, .kunbuddy / .kunbuddy-dev, KUNBUDDY_* env,
@kunbuddy/* workspace packages, kunbuddy:// references, and dev port 4382.
Keep Pi SDK dependency names, pi.* bundled plugin IDs and .piplug format as upstream contracts.
No automatic use of Pi Desktop update installers or private user profiles.

Before identity, packaging or plugin changes, read docs/kunbuddy-migration.md.
Validate isolated temporary data; real credentials and user databases are not test fixtures.
Use existing toolchains/caches; dependencies are local ignored state, not source artifacts.
Keep file encoding and line endings. Run build commands only with explicit user authorization.
Create review snapshots under ../docs/; commit/push only when the user authorizes delivery.
Run pnpm check:client and pnpm check:agent-policy after identity/policy changes.
