# DeepSeek Harness remote plugin

The companion plugin source lives at `../deepseek-plugins/aiagent-remote`. It adds an opt-in seam and does not replace the existing browser chat, server-side DeepSeek Harness runtime, LLM client, or Codex runtime.

The client exchanges an AiAgent username and password for an opaque eight-hour bearer session, discovers the allowed model catalog, and points Harness's native DeepSeek LLM adapter at the authenticated AiAgent chat-completions gateway. Passwords and provider API keys are never returned to the plugin.

Local files remain under the local Harness filesystem provider and its approval policy. Remote Codex delegation accepts at most 200,000 characters of explicit text context, runs against an isolated server-owned read-only workspace, and returns analysis only. Local paths are not accepted, and the remote process cannot directly edit client files.

Production deployments must use HTTPS. Launchers should keep the bearer token in process memory/environment only, terminate it with the process, and reauthenticate after expiration or password reset.
