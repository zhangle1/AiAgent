# KunBuddy client migration

## Scope and provenance

The complete tracked PI-Desktop 0.20.1 source at `872ad335f4580c223738a8ab5538e7b00c17c9ed`
was imported into AiAgent/client: 3,405 source/assets/documentation files from 3,454
tracked entries, excluding 49 upstream GitHub workflow/template files.
The original checkout remains unchanged. There is no nested .git, Git remote,
upstream Actions publisher, dependency directory or prebuilt target in the import.
Local ignored dependencies and compiled outputs are generated separately for verification.

LICENSE, embedded third-party licenses and UPSTREAM notices remain byte-for-byte intact.
Original project entry documents and standalone repository-specific tests/release scripts
are retained under docs/upstream. They are historical reference, not AiAgent delivery policy.
Git operations implemented by the application remain available; only the imported repository
metadata and upstream publication automation are removed.

## Independent identity

| Surface | KunBuddy |
| --- | --- |
| Application title / installer / executable | KunBuddy |
| Application ID / Windows AUMID / macOS ID | com.knowwhy.kunbuddy |
| Linux executable / desktop entry | kunbuddy / kunbuddy.desktop |
| Development application identity | KunBuddy Dev / com.knowwhy.kunbuddy.dev |
| Durable data | ~/.kunbuddy |
| Development data | ~/.kunbuddy-dev |
| Chromium development profile and single-instance lock | appData/KunBuddy Dev |
| Explicit data override | KUNBUDDY_DATA_DIR |
| Workspace packages | @kunbuddy/* |
| Rust executable | kunbuddy-host-core(.exe) |
| Headless CLI | kunbuddy-host |
| Plugin development CLI | kunbuddy-plugin |
| Internal session URI and IPC namespace | kunbuddy:// / kunbuddy/ |
| Renderer development listener | 127.0.0.1:4382, strict port |
| Global launcher / window toggle defaults | Mod+Alt+K / Mod+Alt+Shift+K |
| Agent resources / project plan artifacts | active dataDir/agent / workspace/.kunbuddy |

Electron identity is set before readiness and the single-instance lock; normal production
launches retain single-instance protection. Explicit test/profile data overrides retain the
upstream multi-profile behavior. Old PI_DESKTOP_* and PI_HOST_* variables are not read by the
KunBuddy launcher. The control server and headless host choose free ports by default.
An explicit KUNBUDDY_DATA_DIR also relocates Chromium userData to dataDir/electron;
an explicit --user-data-dir command-line flag still wins. Managed storage restarts clear
the inherited child-data variable before selecting the Chromium profile.

Existing Pi Desktop profiles are not copied or automatically migrated. Explicit importers
for external Pi/Codex/etc. configurations remain external import features. Native Pi SDK
dependency names, the plugin global `pi`, bundled pi.* plugin IDs and the .piplug container
remain upstream interfaces. Workspace SDK imports and newly generated manifest engine keys
use KunBuddy; do not assume an arbitrary precompiled upstream plugin is compatible without
checking its SDK requirements.

The application and platform icons derive from the existing AiAgent KunBuddy mark, copied
unchanged to apps/desktop/src/assets/brand/kunbuddy.png. The icon generation script writes
Windows ICO, macOS ICNS and Linux PNG derivatives; the source mark is preserved.

## Update and repository boundary

The app cannot discover or install Pi Desktop releases. Build publish is null and
the upstream app-update.yml is not packaged. The desktop now checks installers from
the authenticated AiAgent platform. Changing the URL/account cancels old requests and
replaces update authority. Download and installation require explicit user actions.
See ../../docs/kunbuddy-client-releases.md for packaging and the release contract.
The optional generic HTTPS feed remains only for standalone updater callers; the
platform channel takes precedence over KUNBUDDY_UPDATE_FEED_URL in the desktop.
Release notes/feedback links belong to AiAgent. Local pnpm dist uses --publish never.
The former standalone release script refuses to tag/publish the parent repository.

The original migration did not include a server login or domain plugin. The subsequent
AiAgent account channel is documented in ../../docs/kunbuddy-subscription-and-model-discovery.md;
it builds on the retained host credential store, sandbox and native provider transports.

## Development and plugins

Use Node >=22.19, pnpm 12.8.1, Rust/Cargo and MSVC build tools on Windows.

```powershell
cd E:\项目\know-why\AiAgent\client
pnpm install --frozen-lockfile
.\Run-KunBuddy.ps1
```

The launcher also accepts -Mode preview for an existing build and -DataDir for an explicit
profile. The latter is restored in the calling PowerShell environment on exit.
Core build/test commands are in README.md; pnpm check:client requires no dependencies.

```powershell
pnpm kunbuddy-plugin init panel-basic .\examples\plugins\kunbuddy-demo --id kunbuddy.demo --name 'KunBuddy Demo'
pnpm kunbuddy-plugin check .\examples\plugins\kunbuddy-demo
pnpm kunbuddy-plugin pack .\examples\plugins\kunbuddy-demo --out .\artifacts\plugins
```

Load the development folder from the Plugins page; install its .piplug for distribution.
Use examples/plugins/ui-slots-lab for UI extension points and packages/plugin-sdk for
capabilities. Host/RPC/schema ownership and permission checks remain intact.

## Verification record

Verification uses a temporary E-drive profile, fixtures, local model stubs and controlled
plugin examples. No real model or user database is used. Exact results and source hashes are
recorded in the parent repository's docs/kunbuddy-client-migration.changeset.json.

The existing pnpm store is reused with no intentional dependency upgrade; upstream patches
are preserved exactly because their bytes participate in the lockfile hashes. Offline frozen
installation passes. Electron is reused from the provisioned checkout at the same version,
not copied into tracked source.

The Windows Node 22.23.1 executable on this host terminated natively during fs.cpSync in the
optional UI-slots harness; the verification rerun uses the available Node 24.19.0 toolchain.
This does not change the user's globally installed Node version. Development-folder plugin
reload is explicitly awaited before UI-slot tool checks; permission hit tests scroll the
new card into view. These are fixture readiness conditions, not additional plugin privileges.

Windows desktop boot, IPC, isolated paths, plugin scaffold/check/pack, and native host are
verified locally. macOS/Linux installer execution and release signing require their own
platform runners; configuration and generated icon formats are checked here.

Final Windows verification on 2026-10-20:

| Check | Result |
| --- | --- |
| Workspace/headless JS, Rust host, Electron renderer builds and TypeScript checks | Passed |
| Shared / runtime paths / plugin SDK / plugin devkit tests | 636 / 76 / 191 / 52 passed |
| Native host plan tests | 19 passed |
| Desktop identity, data paths and updater tests | 42 passed, 1 macOS-only skip |
| Real Electron boot, sandbox IPC, 800-session responsiveness | Passed |
| Plugin UI integration with local model stub | 64/64 passed |
| Plugin scaffold, check and .piplug packaging | Passed |
| Run-KunBuddy.ps1 with isolated profile | HTTP 200 on 4382; host and sidecar ready |
| Real home screen title, bridge and brand mark in light/dark | Passed |
| Frozen dependency lock, identity and policy checks | Passed |

The review snapshot includes SHA-256 fingerprints and the eight upstream executable script
modes. Source imports are staged only for local review; no commit, tag or push is performed.
Dependency directories, runtime profiles, verification outputs and plugin artifacts remain
outside the source changeset. Existing unrelated AiAgent edits are left untouched.
The staged whitespace check reports 22 inherited upstream warnings (Markdown hard breaks,
existing trailing whitespace and final blank lines); comparison with the source finds no
additional whitespace warnings. These bytes are retained rather than reformatted during import.
