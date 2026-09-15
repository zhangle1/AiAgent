# AiAgent Remote for DeepSeek Harness

`aiagent-remote` keeps file access on the user's machine while using authenticated AiAgent-hosted LLM models and an optional remote Codex analysis tool.

## Connection flow

1. POST the user's AiAgent username/password to `/api/v1/auth/plugin-login` over HTTPS. Keep the returned bearer token only in the process environment as `AIAGENT_ACCESS_TOKEN`.
2. GET `/api/v1/deepseek-plugin/capabilities` and let the user choose an allowed LLM model and, when available, whether to enable remote Codex and which allowed Codex model to use.
3. Set `AIAGENT_BASE_URL`, `AIAGENT_LLM_BASE_URL=$AIAGENT_BASE_URL/api/v1/deepseek-plugin`, and load `aiagent-remote/cordis.example.yml` from a supported `dsh --profile` composition.
4. Start Harness with its working directory set to the folder the user selected. Harness's own filesystem policy remains authoritative.

The launcher passes the choices only to the child process as `AIAGENT_MODEL_ID` and `AIAGENT_CODEX_MODEL_ID`. The plugin uses the latter as the default for `aiagent_codex_delegate`; an explicit tool argument may still select another server-allowed model.

The included launcher performs those steps without persisting the password or token:

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
