# KunBuddy desktop client

KunBuddy is the Electron + React + Rust desktop client in AiAgent/client,
imported from PI-Desktop 0.20.1. It includes the complete local agent runtime,
host core, plugin SDK/devkit, bundled plugins, examples, and product documentation.

## Develop (PowerShell)

```powershell
cd E:\项目\know-why\AiAgent\client
pnpm install --frozen-lockfile
pnpm dev
```

Use Node >=22.19, pnpm 12.8.1, Rust/Cargo and the Windows MSVC toolchain.
Development builds use http://127.0.0.1:4382, .kunbuddy-dev and KunBuddy Dev.
Pi Desktop profiles are not automatically imported or modified.

## Build and validate

```powershell
pnpm check:client
pnpm check:agent-policy
pnpm build:js
cargo build -p host-core
pnpm --filter @kunbuddy/desktop build
pnpm test:e2e:boot
```

`pnpm pack` produces a local desktop directory; `pnpm dist` creates installers.
The application ID is com.knowwhy.kunbuddy and executables/installers are KunBuddy.
The desktop checks installers published by the currently authenticated AiAgent
platform. Switching URL replaces the update source; download and installation are
explicit. See [packaging and publishing](../docs/kunbuddy-client-releases.md).
No upstream installer feed is used.

## Custom plugins

Start with examples/ and docs/plugin-development.md. Workspace imports use
@kunbuddy/plugin-sdk and @kunbuddy/plugin-devkit; the CLI is kunbuddy-plugin.
Use packages/agent-host and apps/kunbuddy-host for headless hosting.
Bundled pi.* IDs and .piplug packages retain compatibility with the original wire format.
The login homepage connects an existing AiAgent account and updates 坤伴订阅 automatically.
Unauthenticated and expired sessions return home; a valid session restores on restart.
Settings → Models → Add AI service → 坤伴订阅 uses the same login flow.
Enter the server URL and account credentials to import its authorized model profiles.
Provider keys stay on the server; the client stores only an encrypted eight-hour session.
See [model discovery and subscription](../docs/kunbuddy-subscription-and-model-discovery.md).

Subscription conversations sync their original Markdown to the account's workspace
`sessions/` directory, one file per conversation, with idempotent appends and retries.
Startup and sign-in backfill existing subscription sessions. Upload does not invoke a
model, enqueue parsing/compilation, or transfer attachment files. Ordinary API-key,
paired-remote and native-import sessions are not automatically uploaded.
See [conversation sync](../docs/kunbuddy-session-sync.md).

[Migration and isolation](docs/kunbuddy-migration.md) · [Source/license](UPSTREAM.md) · [中文](README.zh-CN.md)
