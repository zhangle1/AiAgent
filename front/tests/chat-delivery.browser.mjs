// All API/WebSocket responses are synthetic. No real model, account or project data is used.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFile, mkdir } from "node:fs/promises";
import path from "node:path";
import os from "node:os";
const require = process.env.GOAL_TEST_TOOLS ? createRequire(path.resolve(process.env.GOAL_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const skills = JSON.parse(await readFile(new URL("../../backed/Resources/ChatSkills/catalog-v1.json", import.meta.url), "utf8"));
const artifacts = process.env.CHAT_TEST_ARTIFACTS ?? path.join(os.tmpdir(), "chat-delivery-evidence");
await mkdir(artifacts, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const errors = []; const sent = []; let uploads = 0; let goalState = null;
const queue = []; const canceled = []; const sockets = new Map(); const history = []; let sequence = 0;
page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url()); const name = url.pathname; let body = [];
  if (name.endsWith("/auth/status")) body = { authenticated: true, user_id: "fixture", username: "fixture", role: "admin" };
  else if (name === "/api/v1/settings") body = { ui: { theme: "light", language: "zh-CN", preferred_agent: "codex" }, catalog: { version: 1, services: { llm: { active_profile_id: "fixture", active_model_id: "fixture", profiles: [{ id: "fixture", name: "Fixture", models: [{ id: "fixture", name: "Fixture", model: "fixture" }] }] } } } };
  else if (name === "/api/v1/settings/ui") body = { theme: "light", language: "zh-CN" };
  else if (name.endsWith("/chat/skills")) body = skills;
  else if (name.endsWith("/deliveries")) body = queue;
  else if (/\/chat\/commands\/[^/]+\/cancel$/.test(name)) {
    const command = decodeURIComponent(name.split("/").at(-2)); canceled.push(command);
    for (const row of queue) { row.queue_held = true; sockets.get(row.command_id)?.socket.send(JSON.stringify({ type: "delivery_held", content: "排队消息保留，等待恢复。" })); }
    body = { accepted: true };
  }
  else if (name.endsWith("/sessions/list")) body = { sessions: [] };
  else if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  else if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "name" };
  else if (name.endsWith("/agent-providers/environments")) body = [{ id: "codex", name: "Codex", installed: true, chat_supported: true, protocol: "app-server", command: "fixture", message: "" }];
  else if (name.endsWith("/codex-model-policy")) body = { models: [{ id: "fixture", name: "Fixture Codex", description: "", profile_name: null, supports_reasoning_effort: false, reasoning_efforts: [], is_builtin: true, image_input: "none" }], allowed_model_ids: ["fixture"], default_model_id: "fixture", allow_chat_model_override: true, allowed_reasoning_efforts: [], default_reasoning_effort: "", profile_models: [] };
  else if (name.endsWith("/image-ocr-policy")) body = { enabled: false, native_image_input_enabled: false };
  else if (name === "/api/v1/code-repositories/projects") body = [{ id: 11, name: "fixture", display_name: "测试项目", root_path: "fixture", repositories: [], repository_count: 0 }];
  else if (name.endsWith("/git/status")) body = { project_id: 11, repositories: [], state: "ready", message: "" };
  else if (name === "/api/v1/code-runtime/projects/11") body = { project_id: 11, profiles: [], runs: [] };
  else if (/\/sessions\/[^/]+\/goal\/end$/.test(name)) {
    assert.equal(route.request().postDataJSON().expected_version, goalState.version);
    goalState={...goalState,status:"ended",version:goalState.version+1}; body={goal:goalState};
  }
  else if (/\/sessions\/[^/]+\/goal$/.test(name)) body = { goal: goalState };
  else if (/\/sessions\/[^/]+$/.test(name)) body = { id: name.split("/").at(-1), title: "Fixture", messages: history, preferences: { agent: "codex", codex_model_id: "fixture" }, project_id: 11 };
  else if (name === "/api/v1/chat/attachments/files" && route.request().method() === "POST") { uploads++; body = { id: "fixture-document", file_name: "说明.md", content_type: "text/markdown", size_bytes: 12, kind: "document", extraction_status: "ready" }; }
  else if (name.startsWith("/api/v1/chat/attachments/files/")) body = { ok: true };
  await route.fulfill({ json: body });
});

function message(role, content) { history.push({ id: ++sequence, role, content }); }
function emit(socket, type, content) { socket.send(JSON.stringify({ type, content })); }
await page.routeWebSocket("**/api/v1/chat/ws", socket => socket.onMessage(raw => {
  const request = JSON.parse(String(raw)); sent.push(request);
  sockets.set(request.command_id, { request, socket });
  if (request.recover_only) { emit(socket, "delivery_held", "消息已保留，请明确恢复。"); return; }
  if (request.resume_queued) {
    const row = queue.find(row => row.command_id === request.command_id); assert.ok(row);
    message("user", row.message); queue.splice(queue.indexOf(row), 1);
    emit(socket, "session_ready"); emit(socket, "delivery_started");
    message("assistant", "恢复任务完成"); emit(socket, "content", "恢复任务完成"); emit(socket, "completed"); return;
  }
  if (request.delivery_intent === "steer" && request.message === "追加 JWT 要求") {
    message("user", request.message); emit(socket, "delivery_steered", "已追加，等待 Agent 处理。"); return;
  }
  const active = [...sockets.values()].find(item => item.request !== request && item.request.message === "执行 A");
  if (request.message === "排队 B" || request.message === "保留 E") {
    queue.push({ command_id: request.command_id, session_id: request.session_id, message: request.message, status: "queued", queue_held: false, position: queue.length + 1 });
    emit(socket, "delivery_queued", "已保存，等待上一轮完成。"); return;
  }
  message("user", request.message); emit(socket, "session_ready"); emit(socket, "delivery_started"); emit(socket, "content", `正在处理 ${request.message}`);
}));
const editor = () => page.locator('[contenteditable="true"]:visible').first();
async function send(text) { await editor().fill(text); await editor().press("Enter"); }
async function requestFor(text) {
  for (let i = 0; i < 100; i++) { const item = sent.find(request => request.message === text); if (item) return item; await new Promise(resolve => setTimeout(resolve, 20)); }
  throw new Error("No request for " + text);
}
try {
  await page.addInitScript(() => { window.__deliveryCompletions = 0; window.addEventListener("aiagent:chat-stream-complete", () => window.__deliveryCompletions++); });
  await page.goto(`${process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat"}?project=11&new=delivery`);
  await editor().waitFor({ timeout: 60000 }); await send("执行 A");
  const a = await requestFor("执行 A"); assert.equal(a.delivery_intent, "queue");
  const mode = page.getByRole("combobox", { name: "消息投递方式", exact: true }); await mode.waitFor();
  await send("排队 B"); const b = await requestFor("排队 B");
  await page.getByRole("region", { name: "消息投递队列", exact: true }).getByText("排队 B", { exact: true }).waitFor();
  await mode.selectOption("steer"); await send("追加 JWT 要求");
  const c = await requestFor("追加 JWT 要求"); assert.equal(c.delivery_intent, "steer");
  await page.getByRole("status").filter({ hasText: "追加指令已投递给当前任务" }).waitFor();
  assert.equal(await page.evaluate(() => window.__deliveryCompletions), 0);
  await page.screenshot({ path: path.join(artifacts, "queue-and-steer-desktop.png"), fullPage: true });
  message("assistant", "A 已完成"); emit(sockets.get(a.command_id).socket, "completed");
  queue.splice(0, queue.length); message("user", "排队 B"); message("assistant", "B 已完成");
  emit(sockets.get(b.command_id).socket, "session_ready"); emit(sockets.get(b.command_id).socket, "delivery_started"); emit(sockets.get(b.command_id).socket, "content", "B 已完成"); emit(sockets.get(b.command_id).socket, "completed");
  await page.getByText("B 已完成", { exact: true }).waitFor();
  await page.waitForFunction(() => window.__deliveryCompletions === 2);
  await send("执行 D"); const d = await requestFor("执行 D"); await mode.waitFor(); await mode.selectOption("queue");
  await send("保留 E"); const e = await requestFor("保留 E");
  await page.getByRole("button", { name: "Stop generating", exact: true }).click();
  await page.getByRole("button", { name: "恢复执行", exact: true }).waitFor();
  assert.deepEqual(canceled, [d.command_id]);
  assert.equal(queue[0].command_id, e.command_id); assert.equal(queue[0].queue_held, true);
  await page.reload(); await editor().waitFor();
  await page.getByRole("button", { name: "恢复执行", exact: true }).waitFor();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(() => document.querySelector("main")?.getBoundingClientRect().left === 0);
  await page.screenshot({ path: path.join(artifacts, "held-queue-mobile.png"), fullPage: true });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await page.getByRole("button", { name: "恢复执行", exact: true }).click();
  await page.getByText("恢复任务完成", { exact: true }).waitFor();
  const resumed = sent.find(request => request.resume_queued); assert.equal(resumed.command_id, e.command_id); assert.equal(resumed.message, "");
  assert.equal(queue.length, 0); assert.deepEqual(errors, []);
  console.log(`PASS: continuous delivery while active, queue/steer intent, acknowledgement vs completion, stop/hold, refresh persistence, explicit resume and 390px layout. Screenshots: ${artifacts}`);
} finally { await browser.close(); }
