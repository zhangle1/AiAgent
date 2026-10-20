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
Automatic updates are disabled unless a KunBuddy-owned HTTPS generic feed is
explicitly configured with KUNBUDDY_UPDATE_FEED_URL; no upstream installer feed is used.

## Custom plugins

Start with examples/ and docs/plugin-development.md. Workspace imports use
@kunbuddy/plugin-sdk and @kunbuddy/plugin-devkit; the CLI is kunbuddy-plugin.
Use packages/agent-host and apps/kunbuddy-host for headless hosting.
Bundled pi.* IDs and .piplug packages retain compatibility with the original wire format.
No AiAgent server authentication or custom server plugin has been added in this migration.

[Migration and isolation](docs/kunbuddy-migration.md) · [Source/license](UPSTREAM.md) · [中文](README.zh-CN.md)
