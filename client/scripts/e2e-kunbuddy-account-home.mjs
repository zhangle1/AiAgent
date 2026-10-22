import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { connectRenderer } from "../apps/desktop/test/e2e/plugin-ui-slots/clients.mjs";

const client = fileURLToPath(new URL("../", import.meta.url));
const scratch = process.env.KUNBUDDY_ACCOUNT_E2E_DIR;
if (!scratch || !path.isAbsolute(scratch)) throw Error("Set KUNBUDDY_ACCOUNT_E2E_DIR to an isolated absolute directory.");
fs.mkdirSync(scratch, { recursive: true });
const home = fs.mkdtempSync(path.join(scratch, "profile-"));
const require = createRequire(path.join(client, "apps/desktop/package.json"));
const electron = path.join(path.dirname(require.resolve("electron/package.json")), "dist/electron.exe");
const hostBinary = process.env.KUNBUDDY_HOST_BIN || path.join(client, "target/debug/kunbuddy-host-core.exe");
let serial = 0, valid = new Set(), catalogStatus = 200, seconds = 3600, catalogRequests = 0, generationRequests = 0;
const server = createServer(async (req, res) => {
  let body = ""; for await (const chunk of req) body += chunk;
  const send = (status, data) => { res.writeHead(status, { "Content-Type": "application/json" }); res.end(JSON.stringify(data)); };
  if (req.url === "/api/v1/auth/kunbuddy-login") {
    const input = JSON.parse(body);
    if (input.username !== "fixture-user" || input.password !== "fixture-password") return send(401, {});
    const token = "fixture-token-" + ++serial; valid.add(token);
    return send(200, { username: input.username, access_token: token, expires_at: new Date(Date.now() + seconds * 1000).toISOString() });
  }
  if (req.url === "/api/v1/kunbuddy/models") {
    catalogRequests++;
    if (!valid.has(req.headers.authorization?.replace("Bearer ", ""))) return send(401, {});
    return send(catalogStatus, { profiles: [{ id: "profile", name: "隔离验证", api_style: "chat_completions", models: [{ id: "model", name: "验证模型", context_window: 1000000, max_output_tokens: 65536, is_default: true }] }] });
  }
  if (req.url === "/api/v1/kunbuddy/logout") { valid.delete(req.headers.authorization?.replace("Bearer ", "")); return send(200, {}); }
  if (req.method === "POST" && req.url.startsWith("/api/v1/kunbuddy/profiles/")) generationRequests++;
  return send(404, {});
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const url = `http://127.0.0.1:${server.address().port}`;
let child, ui;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function stop() {
  ui?.close(); ui = undefined;
  if (child?.exitCode === null) {
    const kill = spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
    await new Promise(resolve => kill.once("close", resolve));
    await sleep(300);
  }
}
async function start() {
  const portServer = createServer(); await new Promise(resolve => portServer.listen(0, "127.0.0.1", resolve));
  const port = portServer.address().port; await new Promise(resolve => portServer.close(resolve));
  const env = { ...process.env, KUNBUDDY_DATA_DIR: path.join(home, "data"), KUNBUDDY_HOST_BIN: hostBinary, ELECTRON_RENDERER_URL: "" };
  delete env.ELECTRON_RUN_AS_NODE;
  child = spawn(electron, ["--disable-gpu", `--user-data-dir=${home}/chromium`, `--remote-debugging-port=${port}`, "."], { cwd: path.join(client, "apps/desktop"), env, stdio: ["ignore", "pipe", "pipe"] });
  const log = fs.createWriteStream(path.join(scratch, "electron.log"), { flags: "a" }); child.stdout.pipe(log, { end: false }); child.stderr.pipe(log, { end: false }); child.once("close", () => log.end());
  const deadline = Date.now() + 40000;
  while (!ui) { if (child.exitCode !== null) throw Error("Isolated Electron exited"); try { ui = await connectRenderer(port); } catch (error) { if (Date.now() > deadline) throw error; await sleep(150); } }
  await ui.run(() => { window.__invoke = async (channel, ...args) => { const result = await window.kunBuddy.invoke(channel, ...args); if (!result.ok) throw Error(result.error?.message); return result.data; }; });
}
async function screenshot(name) {
  await ui.until(() => !document.querySelector('.startup-splash'), null, "startup splash dismissed");
  fs.writeFileSync(path.join(scratch, name + ".png"), Buffer.from(await ui.screenshot(), "base64"));
}
async function fill(name, value) { await ui.click(`input[name="${name}"]`); await ui.run(name => document.querySelector(`input[name="${name}"]`).select(), name); await ui.type(value); }
async function submit() { await ui.click('.kunbuddy-home-login button[type="submit"]'); }
try {
  await start();
  await ui.until(() => !!document.querySelector('.kunbuddy-home input[name="baseUrl"]:not(:disabled)'), null, "fresh install login homepage");
  assert(!await ui.run(() => !!document.querySelector('.app-chat-shell')));
  await ui.viewport(1160, 800); await screenshot("homepage-desktop");
  await ui.viewport(420, 840); await screenshot("homepage-narrow");
  assert(await ui.run(() => document.querySelector('.kunbuddy-home').scrollWidth <= innerWidth));
  await ui.viewport(1160, 800);
  await fill("baseUrl", url); await fill("username", "fixture-user"); await fill("password", "bad-password"); await submit();
  await ui.until(() => document.querySelector('.kunbuddy-home-error')?.textContent, null, "invalid credentials inline error");
  assert.equal((await ui.run(() => window.__invoke('kunbuddy/providers/list'))).providers.length, 0);
  await fill("password", "fixture-password"); await submit();
  await ui.until(() => document.querySelector('.app-chat-shell') && !document.querySelector('.kunbuddy-home'), null, "login opens workspace", 30000);
  let listed = await ui.run(() => window.__invoke('kunbuddy/providers/list'));
  const provider = listed.providers.find(p => p.authKind === "kunbuddy_subscription"); assert(provider); assert.equal(provider.models[0].contextWindow, 1000000);
  const retained = await ui.run(p => window.__invoke('kunbuddy/session/create', { title: '保留的本地会话', providerId: p.id, modelId: p.models[0].id, mode: 'agent' }), provider);
  assert(!JSON.stringify(listed).includes("fixture-token")); assert(!JSON.stringify(listed).includes("fixture-password"));
  await ui.click('button[data-nav="settings"]');
  await ui.until(() => !!document.querySelector('.settings-shell-full'), null, "settings");
  await ui.run(() => [...document.querySelectorAll('.settings-nav-item')].find(n => /Models|模型/.test(n.textContent)).click());
  await ui.until(() => document.body.innerText.includes('fixture-user') && document.body.innerText.includes('坤伴订阅'), null, "homepage login updates subscription entry");
  await screenshot("subscription-updated");
  const before = catalogRequests; catalogStatus = 500;
  await ui.run(() => window.dispatchEvent(new Event('focus')));
  const checkDeadline = Date.now() + 5000; while (catalogRequests === before && Date.now() < checkDeadline) await sleep(50);
  await sleep(300); assert(catalogRequests > before); assert(!await ui.run(() => !!document.querySelector('.kunbuddy-home')), "temporary server failure must not be treated as expiry"); catalogStatus = 200;
  await stop(); await start();
  await ui.until(() => document.querySelector('.app-chat-shell') && !document.querySelector('.kunbuddy-home'), null, "valid saved login survives restart", 30000);
  valid.clear(); await ui.run(() => window.dispatchEvent(new Event('focus')));
  await ui.until(() => document.querySelector('.kunbuddy-home')?.textContent.includes('登录已过期'), null, "server revocation returns to homepage");
  assert.equal(await ui.run(() => document.querySelector('input[name="baseUrl"]').value), url);
  assert.equal(await ui.run(() => document.querySelector('input[name="username"]').value), "fixture-user");
  assert.equal(await ui.run(() => document.querySelector('input[name="password"]').value), "");
  await screenshot("homepage-expired");
  seconds = 5; await fill("password", "fixture-password"); await submit();
  await ui.until(() => document.querySelector('.app-chat-shell') && !document.querySelector('.kunbuddy-home'), null, "relogin returns to workspace", 30000);
  listed = await ui.run(() => window.__invoke('kunbuddy/providers/list')); assert.equal(listed.providers.length, 1); assert.equal(listed.providers[0].id, provider.id);
  assert.equal((await ui.run(id => window.__invoke('kunbuddy/session/get', { id }), retained.session.id)).session.id, retained.session.id);
  await ui.until(() => document.querySelector('.kunbuddy-home')?.textContent.includes('登录已过期'), null, "local deadline returns to homepage without waiting for next poll", 10000);
  seconds = 3600; await fill("password", "fixture-password"); await submit();
  await ui.until(() => !document.querySelector('.kunbuddy-home'), null, "second relogin");
  await ui.run(id => window.__invoke('kunbuddy/subscription/logout', id), provider.id); await ui.run(() => window.dispatchEvent(new Event('focus')));
  await ui.until(() => !!document.querySelector('.kunbuddy-home input[name="baseUrl"]:not(:disabled)'), null, "logout returns home");
  assert.equal((await ui.run(() => window.__invoke('kunbuddy/providers/list'))).providers.length, 0);
  const hint = fs.readFileSync(path.join(home, "data/kunbuddy-account.json"), "utf8"); assert(!hint.includes("fixture-password")); assert(!hint.includes("fixture-token"));
  assert.equal(generationRequests, 0);
  const result = { status: "passed", freshInstallGate: true, loginAndSubscriptionSync: true, wrongPasswordStaysHome: true, savedLoginRestart: true, serverRevocationRedirect: true, localDeadlineRedirect: true, reloginUpdatesSameProvider: true, localSessionRetained: true, temporaryFailureKeepsWorkspace: true, logoutReturnsHome: true, narrowWidth: 420, noRawCredentialHints: true, modelGenerationRequests: generationRequests };
  fs.writeFileSync(path.join(scratch, "result.json"), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result));
} catch (error) {
  if (ui) { await screenshot("failure").catch(() => undefined); console.error(await ui.run(() => document.body.innerText.slice(0, 2400)).catch(() => "renderer unavailable")); }
  throw error;
} finally { await stop(); await new Promise(resolve => server.close(resolve)); }
