# AiAgent Remote for DeepSeek Harness

`aiagent-remote` keeps file access on the user's machine while using authenticated AiAgent-hosted LLM models and an optional remote Codex analysis tool.

## Connection flow

1. Start the linked Harness profile and open **Settings → Plugins → AiAgent Remote**.
2. Enter the AiAgent backend root URL, username, and password, then choose **登录并同步模型**.
3. The password is exchanged over the local Harness Remote channel for an 8-hour plugin token. The token is stored only in Harness's credential store; it is never written to `cordis.yml`, `settings.yaml`, process environment, or logs.
4. The plugin refreshes the Harness DeepSeek model catalog from `/api/v1/deepseek-plugin/capabilities`. Harness's own filesystem policy remains authoritative.

The launcher passes the choices only to the child process as `AIAGENT_MODEL_ID` and `AIAGENT_CODEX_MODEL_ID`. The plugin uses the latter as the default for `aiagent_codex_delegate`; an explicit tool argument may still select another server-allowed model.

The legacy launcher remains available for headless compatibility only. The normal web-profile flow is the Settings page above.

```powershell
node .\bin\launch.mjs -- pnpm dsh --profile <your-profile> "your task"
```

When prompted for the backend address, you can enter a development IP and port directly, such as `192.168.1.20:5000`; the launcher normalizes it to `http://192.168.1.20:5000`. You can also enter a complete address such as `https://aiagent.example.com`. Set `AIAGENT_BASE_URL` before launching to provide an editable default:

```powershell
$env:AIAGENT_BASE_URL="http://192.168.1.20:5000"
node .\bin\launch.mjs -- pnpm dsh --profile <your-profile> "your task"
```

Enter the backend root only, without `/api` or `/api/v1`. The selected address is passed to the child Harness process and is not written to the plugin configuration.

Never put a password or bearer token in `cordis.yml`, shell history, logs, or source control. Plain HTTP exposes login credentials in transit, so use it only on a trusted development network; production deployments must use HTTPS. The remote Codex tool accepts bounded text excerpts only: it never receives a local path and cannot directly edit client files. Harness reviews the response and performs any local change through its native local tools and approval policy.

## Local end-to-end test (before npm publishing)

This checkout can be installed as a linked DSH bundle first. The script builds the local `lib/` output, initializes an interactive `web` profile when needed, links this package into that profile, and prints the effective configuration. It changes only the selected DSH profile; it does not start AiAgent, publish a package, or store credentials.

```powershell
cd E:\项目\know-why\AiAgent\plugins\aiagent-remote
.\scripts\install-local.ps1
```

Start the local AiAgent backend separately, then launch the profile. In the Harness browser page, open **Settings → Plugins → AiAgent Remote** and sign in. The token is stored in the local Harness credential store rather than the launched process environment.

```powershell
.\scripts\start-local.ps1 -Workspace E:\项目\know-why\your-test-workspace
```

To start the local Harness explicitly on port 3080 without opening a browser:

```powershell
.\scripts\start-3080.ps1
```

Add `-OpenBrowser` when the script should open the DSH page automatically.

Use a disposable test folder first. A local `http://` backend is acceptable only on a trusted development network because login credentials travel over that connection.

## Publish to npm

The package is now an installable DSH bundle: `package.json` declares `dsh.bundle`, `cordis.patch.yml` is the bundle patch, and the package ships prebuilt `lib/` code through the `prepack` hook. The public name `@aiagent/deepseek-plugin-remote` is currently unclaimed on npm; publishing it requires that you own or create the `@aiagent` npm organization.

After reviewing the package contents, publish from this directory:

```powershell
npm login
npm run test
npm run typecheck
npm publish --access public
```

Do not publish until `license` is set to the license you intend to grant. It is currently `UNLICENSED` deliberately, so the release owner must make that policy decision. After publishing, a user installs it into a DSH profile with:

```powershell
dsh plugin --profile aiagent-remote add @aiagent/deepseek-plugin-remote
```

For a release rehearsal without uploading anything, run `npm pack --dry-run`; it should contain `bin/`, `lib/`, `cordis.patch.yml`, and `README.md`, but no source tests or credentials.
