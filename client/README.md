# AiAgent Terminal Desktop

An Electron desktop client for explicitly shared local text and human-operated terminals.

## What the first version does

- Signs in through the existing plugin login API and keeps the bearer token only in the main-process memory.
- Lets the user authorize one local folder, list allowed files, and read UTF-8 text files up to 200 KB.
- Sends only the currently selected file excerpt and the user's prompt to the remote Codex delegate endpoint.
- Opens local PTY and SSH human terminals. Terminal output is never automatically supplied to AI.
- Pins an SSH host key using an explicit `SHA256:` fingerprint; the password is used only for that connection and is not persisted.

## Run locally

```powershell
cd client
npm install
npm run start
```

For the development backend, retain the **Allow HTTP** checkbox. Production addresses should be HTTPS.

## Verification

```powershell
npm run build
npm run smoke
```

The desktop client is intentionally separate from the browser client. Device pairing, outbound WSS relay, agent-side file tools, write approval, and persistent credential storage remain later phases described in [the architecture plan](../docs/agent-terminal-and-remote-host-access-plan.md).
