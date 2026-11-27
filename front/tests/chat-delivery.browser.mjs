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
const executablePath = process.env.CHAT_TEST_BROWSER_EXECUTABLE;
const browser = await chromium.launch(executablePath ? { executablePath } : { channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, timezoneId: "America/Los_Angeles" });
const errors = []; const sent = []; let uploads = 0; let goalState = null;
let deliveriesUnavailable = true;
let signedIn = false;
const prematureRecovery = [];
const queue = []; const canceled = []; const sockets = new Map(); const history = []; let sequence = 0;
page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url()); const name = url.pathname; let body = [];
  if (name.endsWith("/auth/login")) { signedIn = true; body = { ok: true }; }
  else if (name.endsWith("/auth/status")) body = { authenticated: signedIn, user_id: "fixture", username: "fixture", role: "admin" };
  else if (name === "/api/v1/settings") body = { ui: { theme: "light", language: "zh-CN", preferred_agent: "codex" }, catalog: { version: 1, services: { llm: { active_profile_id: "fixture", active_model_id: "fixture", profiles: [{ id: "fixture", name: "Fixture", models: [{ id: "fixture", name: "Fixture", model: "fixture" }] }] } } } };
  else if (name === "/api/v1/settings/ui") body = { theme: "light", language: "zh-CN" };
  else if (name.endsWith("/chat/skills")) body = skills;
  else if (name.endsWith("/deliveries")) {
    if (deliveriesUnavailable) return route.fulfill({ status: 500, body: "Synthetic queue error" });
    body = queue;
  }
  else if (/\/chat\/commands\/[^/]+\/cancel$/.test(name)) {
    const command = decodeURIComponent(name.split("/").at(-2)); canceled.push(command);
    const running = queue.findIndex(row => row.command_id === command && row.status === "running");
    if (running !== -1) queue.splice(running, 1);
    for (const row of queue) { row.queue_held = true; sockets.get(row.command_id)?.socket.send(JSON.stringify({ type: "delivery_held", content: "排队消息保留，等待恢复。" })); }
    sockets.get(command)?.socket.send(JSON.stringify({ type: "cancelled", content: "任务已停止。" }));
    body = { accepted: true };
  }
  else if (name.endsWith("/sessions/list")) body = { sessions: [] };
  else if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  else if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "name" };
  else if (name.endsWith("/agent-providers/environments")) body = [{ id: "codex", name: "Codex", installed: true, chat_supported: true, protocol: "app-server", command: "fixture", message: "" }];
  else if (name.endsWith("/codex-model-policy")) body = { models: [{ id: "fixture", name: "Fixture Codex", description: "", profile_name: null, supports_reasoning_effort: false, reasoning_efforts: [], is_builtin: true, image_input: "native" }], allowed_model_ids: ["fixture"], default_model_id: "fixture", allow_chat_model_override: true, allowed_reasoning_efforts: [], default_reasoning_effort: "", profile_models: [] };
  else if (name.endsWith("/image-ocr-policy")) body = { enabled: false, native_image_input_enabled: true };
  else if (name === "/api/v1/code-repositories/projects") body = [{ id: 11, name: "fixture", display_name: "测试项目", root_path: "fixture", repositories: [], repository_count: 0 }];
  else if (name.endsWith("/git/status")) body = { project_id: 11, repositories: [], state: "ready", message: "" };
  else if (name === "/api/v1/code-runtime/projects/11") body = { project_id: 11, profiles: [], runs: [] };
  else if (/\/sessions\/[^/]+\/goal\/end$/.test(name)) {
    assert.equal(route.request().postDataJSON().expected_version, goalState.version);
    goalState={...goalState,status:"ended",version:goalState.version+1}; body={goal:goalState};
  }
  else if (/\/sessions\/[^/]+\/goal$/.test(name)) body = { goal: goalState };
  else if (/\/sessions\/[^/]+$/.test(name)) body = { id: name.split("/").at(-1), title: "Fixture", messages: history, preferences: { agent: "codex", codex_model_id: "fixture" }, project_id: 11 };
  else if (name === "/api/v1/chat/attachments/files" && route.request().method() === "POST") { uploads++; body = { id: "fixture-document", file_name: "追加.txt", content_type: "text/plain", size_bytes: 12, kind: "document", extraction_status: "ready" }; }
  else if (name === "/api/v1/chat/attachments/images" && route.request().method() === "POST") body = { id: "fixture-image", file_name: "追加.png", content_type: "image/png", size_bytes: 8 };
  else if (name.startsWith("/api/v1/chat/attachments/files/")) body = { ok: true };
  await route.fulfill({ json: body });
});

function message(role, content, created_at, metadata) { history.push({ id: ++sequence, role, content, created_at, metadata }); }
function emit(socket, type, content) { socket.send(JSON.stringify({ type, content })); }
await page.routeWebSocket("**/api/v1/chat/ws", socket => socket.onMessage(raw => {
  const request = JSON.parse(String(raw)); sent.push(request);
  if (request.command_id === "pending-before-login") {
    if (!signedIn) prematureRecovery.push(request);
    emit(socket, "completed");
    return;
  }
  sockets.set(request.command_id, { request, socket });
  emit(socket, "delivery_accepted");
  if (request.message === "执行 A") {
    message("user", request.message);
    return; // The test releases the worker after the visible receipt.
  }
  if (request.recover_only) {
    if (queue.some(row => row.command_id === request.command_id && row.status === "running")) {
      emit(socket, "delivery_started");
      emit(socket, "content", "重登后恢复的实时输出");
    } else emit(socket, "delivery_held", "消息已保留，请明确恢复。");
    return;
  }
  if (request.resume_queued) {
    const row = queue.find(row => row.command_id === request.command_id); assert.ok(row);
    message("user", row.message); queue.splice(queue.indexOf(row), 1);
    emit(socket, "session_ready"); emit(socket, "delivery_started");
    message("assistant", "恢复任务完成"); emit(socket, "content", "恢复任务完成"); emit(socket, "completed"); return;
  }
  if (request.delivery_intent === "steer" && ["追加 JWT 要求", "请分析我附上的文件。"].includes(request.message)) {
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
  const loginUrl = new URL("/login", process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat");
  loginUrl.searchParams.set("next", "/chat?project=11&new=delivery");
  await page.addInitScript(() => {
    if (location.pathname === "/login")
      sessionStorage.setItem("aiagent:pending-chat-commands:v1", JSON.stringify([{
        command_id: "pending-before-login", session_id: "owned-session", saved_at: Date.now()
      }]));
  });
  await page.goto(loginUrl.href, { waitUntil: "networkidle" });
  await page.locator('input[autocomplete="username"]').fill("fixture");
  await page.locator('input[type="password"]').fill("fixture-password");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await editor().waitFor({ timeout: 60000 });
  await page.waitForFunction(() => {
    const pointers = JSON.parse(sessionStorage.getItem("aiagent:pending-chat-commands:v1") ?? "[]");
    return !pointers.some(pointer => pointer.command_id === "pending-before-login");
  });
  assert.deepEqual(prematureRecovery, [], "Recovery must wait for authenticated navigation");
  assert.equal(sent.filter(request => request.command_id === "pending-before-login" && request.recover_only).length, 1);
  // The following assertions count completions for the new delivery scenario.
  await page.evaluate(() => { window.__deliveryCompletions = 0; });
  const queuePanel = page.getByRole("region", { name: "消息投递队列", exact: true });
  const failedPoll = () => page.waitForResponse(response => response.url().endsWith("/deliveries") && response.status() === 500);
  const firstPoll = failedPoll();
  await page.evaluate(() => {
    document.addEventListener("keydown", event => {
      if (event.key !== "Enter") return;
      const start = performance.now();
      const timings = window.__chatTimings = {};
      const observer = new MutationObserver(() => {
        const text = Array.from(document.querySelectorAll("article"), item => item.textContent);
        if (timings.userMs === undefined && text.some(item => item.includes("执行 A")))
          timings.userMs = Math.round(performance.now() - start);
        if (text.some(item => item.includes("正在处理 执行 A"))) {
          timings.firstTextMs = Math.round(performance.now() - start);
          observer.disconnect();
        }
      });
      observer.observe(document.body, { childList: true, characterData: true, subtree: true });
    }, { once: true, capture: true });
  });
  await send("执行 A");
  const a = await requestFor("执行 A"); assert.equal(a.delivery_intent, "queue");
  await page.getByText("已接收 · 等待执行", { exact: true }).waitFor();
  emit(sockets.get(a.command_id).socket, "delivery_started");
  await page.getByText("已开始 · 正在准备上下文", { exact: true }).waitFor();
  emit(sockets.get(a.command_id).socket, "session_ready");
  emit(sockets.get(a.command_id).socket, "content", "正在处理 执行 A");
  await page.waitForFunction(() => window.__chatTimings?.firstTextMs !== undefined);
  console.log("Local mocked WebSocket / Enter to DOM incl. test-controlled claim (ms)", await page.evaluate(() => window.__chatTimings));
  await firstPoll;
  await failedPoll();
  assert.equal(await queuePanel.count(), 0, "An empty queue must stay hidden during polling errors");
  deliveriesUnavailable = false;
  const mode = page.getByRole("combobox", { name: "消息投递方式", exact: true }); await mode.waitFor();
  await send("排队 B"); const b = await requestFor("排队 B");
  await queuePanel.getByText("排队 B", { exact: true }).waitFor();
  const assertQueueAboveComposer = async () => {
    assert.equal(await queuePanel.evaluate(element => element.closest("form")), null);
    const panel = await queuePanel.boundingBox();
    const composer = await editor().locator("xpath=ancestor::form").boundingBox();
    assert.ok(panel && composer && panel.y + panel.height <= composer.y, "Queue should sit above the composer");
  };
  await assertQueueAboveComposer();
  const navigation = page.getByRole("navigation", { name: "会话导航", exact: true });
  const firstQuestion = navigation.getByRole("button", { name: /^跳转到第 1 条提问/ });
  const assertNavigationOnLeft = async () => {
    const rail = await navigation.boundingBox();
    const conversation = await page.locator("[data-chat-question]").first().boundingBox();
    const minimumGap = (await page.viewportSize()).width >= 640 ? 20 : 12;
    assert.ok(rail && conversation && rail.x >= 0 && rail.x + rail.width + minimumGap <= conversation.x + 1, "Message navigation should sit clear of the conversation");
    await firstQuestion.focus();
    const preview = navigation.getByText("第 1 条提问", { exact: true });
    await preview.waitFor();
    const tooltip = await preview.boundingBox();
    assert.ok(tooltip && tooltip.x >= rail.x + rail.width, "Preview should open toward the conversation");
    assert.ok(tooltip.x + tooltip.width <= (await page.evaluate(() => innerWidth)), "Preview should fit inside the viewport");
    await firstQuestion.press("Escape");
    await preview.waitFor({ state: "hidden" });
  };
  await assertNavigationOnLeft();
  await mode.selectOption("steer"); await send("追加 JWT 要求");
  const c = await requestFor("追加 JWT 要求"); assert.equal(c.delivery_intent, "steer");
  await page.getByRole("status").filter({ hasText: "追加指令已投递给当前任务" }).waitFor();
  await editor().evaluate(element => {
    const data = new DataTransfer();
    data.items.add(new File(["追加 TXT 正文"], "追加.txt", { type: "text/plain" }));
    data.items.add(new File([Uint8Array.of(137, 80, 78, 71, 13, 10, 26, 10)], "追加.png", { type: "image/png" }));
    element.dispatchEvent(new ClipboardEvent("paste", { clipboardData: data, bubbles: true, cancelable: true }));
  });
  await page.getByRole("button", { name: "移除 追加.png", exact: true }).waitFor();
  await page.getByRole("button", { name: "移除 追加.txt", exact: true }).waitFor();
  await page.getByRole("button", { name: "追加指令", exact: true }).click();
  const withAttachments = await requestFor("请分析我附上的文件。");
  assert.equal(withAttachments.delivery_intent, "steer");
  assert.deepEqual(withAttachments.attachment_ids, ["fixture-image"]);
  assert.deepEqual(withAttachments.document_attachment_ids, ["fixture-document"]);
  assert.equal(await page.evaluate(() => window.__deliveryCompletions), 0);
  await page.screenshot({ path: path.join(artifacts, "queue-and-steer-desktop.png"), fullPage: true });
  message("assistant", "A 已完成"); emit(sockets.get(a.command_id).socket, "completed");
  queue.splice(0, queue.length); message("user", "排队 B");
  const bSocket = sockets.get(b.command_id).socket;
  emit(bSocket, "delivery_started"); emit(bSocket, "session_ready"); emit(bSocket, "content", "B 首段");
  const answer = page.locator("article").filter({ hasText: "B 首段" });
  await answer.getByText("B 首段", { exact: true }).waitFor();
  for (let i = 0; i < 128; i++) emit(bSocket, "content", "片段");
  bSocket.send(JSON.stringify({ type: "done", content: "B 已完成", metadata: { agent: "codex" } }));
  const finalAnswer = page.locator("article").filter({ hasText: "B 已完成" });
  await finalAnswer.getByText("B 已完成", { exact: true }).waitFor();
  assert.equal(await finalAnswer.getByText("正常完成", { exact: true }).count(), 0);
  message("assistant", "B 已完成"); emit(bSocket, "completed");
  await finalAnswer.getByText("正常完成", { exact: true }).waitFor();
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
  await assertQueueAboveComposer();
  await assertNavigationOnLeft();
  await page.screenshot({ path: path.join(artifacts, "held-queue-mobile.png"), fullPage: true });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await page.getByRole("button", { name: "恢复执行", exact: true }).click();
  await page.getByText("恢复任务完成", { exact: true }).waitFor();
  const resumed = sent.find(request => request.resume_queued); assert.equal(resumed.command_id, e.command_id); assert.equal(resumed.message, "");
  await queuePanel.waitFor({ state: "detached" });
  assert.equal(queue.length, 0);

  // Reopening with no browser recovery pointers still exposes the server's running turn.
  queue.push(
    { command_id: "previous-browser-run", session_id: d.session_id, message: "另一个浏览器的任务", status: "running", queue_held: false, position: 1 },
    { command_id: "previous-browser-queued", session_id: d.session_id, message: "上一轮后的待处理消息", status: "queued", queue_held: false, position: 2 },
  );
  await page.evaluate(() => sessionStorage.clear());
  await page.goto(`${process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat"}?session=${d.session_id}`);
  await page.getByText("重登后恢复的实时输出", { exact: true }).waitFor();
  const recovered = sent.filter(request => request.command_id === "previous-browser-run");
  assert.equal(recovered.length, 1);
  assert.equal(recovered[0].recover_only, true);
  await page.getByRole("button", { name: /^(停止生成|Stop generating)$/ }).click();
  await queuePanel.getByRole("button", { name: "恢复执行", exact: true }).waitFor();
  assert.deepEqual(canceled, [d.command_id, "previous-browser-run"]);
  assert.equal(queue[0].command_id, "previous-browser-queued");
  assert.equal(queue[0].queue_held, true);
  queue.length = 0;

  history.length = 0;
  const historyTimes = ["2026-11-23T16:15:00.1234567", "2026-11-24T01:45:00+09:00"];
  for (let index = 1; index <= 22; index++) {
    message("user", `导航测试提问 ${index}`);
    message("assistant", `导航测试回复 ${index}\n\n` + "回复正文。\n\n".repeat(30), historyTimes[index - 1], { model: "Codex" });
  }
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(`${process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat"}?session=long-navigation`);
  const markers = navigation.getByRole("button");
  const lastQuestion = navigation.getByRole("button", { name: /^跳转到第 22 条提问/ });
  await lastQuestion.waitFor();
  const timestamps = page.locator('time[title="消息时间（北京时间 UTC+8）"]');
  assert.deepEqual(await timestamps.allTextContents(), ["2026/11/24 00:15:00", "2026/11/24 00:45:00"]);
  assert.deepEqual(await timestamps.evaluateAll(elements => elements.map(element => element.dateTime)), [
    "2026-11-23T16:15:00.123Z",
    "2026-11-23T16:45:00.000Z",
  ]);
  const firstTimestamp = timestamps.first();
  const model = firstTimestamp.locator("..").getByText("Codex", { exact: true });
  const modelBox = await model.boundingBox();
  const timeBox = await firstTimestamp.boundingBox();
  assert.ok(modelBox && timeBox, "The model name and timestamp should have a layout box");
  const gap = timeBox.x - (modelBox.x + modelBox.width);
  assert.ok(Math.abs(modelBox.y - timeBox.y) <= 1 && gap >= 0 && gap <= 12,
    "The timestamp should sit beside the model name on a narrow screen");
  const railWindow = navigation.locator(":scope > div");
  assert.equal(await markers.count(), 22);
  const markerPositions = await markers.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().top));
  const step = markerPositions[1] - markerPositions[0];
  assert.ok(step >= 32 && markerPositions.slice(1).every((top, index) => Math.abs(top - markerPositions[index] - step) < 1));
  assert.equal(await railWindow.evaluate(element => element.clientHeight), 8 * step);
  await railWindow.evaluate(element => { element.scrollTop = 0; });
  await railWindow.hover();
  const chatScrollTop = () => page.locator("[data-chat-question]").first().evaluate(question => question.parentElement.scrollTop);
  const beforeWheel = await chatScrollTop();
  await page.mouse.wheel(0, step);
  await page.waitForFunction(() => {
    const rail = document.querySelector('nav[aria-label="会话导航"] > div');
    return rail.scrollTop > 0 && rail.scrollTop % rail.firstElementChild.clientHeight === 0;
  });
  assert.equal(await chatScrollTop(), beforeWheel);
  await firstQuestion.click();
  await navigation.locator('button[aria-current="step"]').and(firstQuestion).waitFor();
  await page.locator("[data-chat-question]").nth(4).evaluate(question => {
    const container = question.parentElement;
    container.scrollTop += question.getBoundingClientRect().top - container.getBoundingClientRect().top - 16;
  });
  const fifthQuestion = navigation.getByRole("button", { name: /^跳转到第 5 条提问/ });
  await navigation.locator('button[aria-current="step"]').and(fifthQuestion).waitFor();
  await page.locator("[data-chat-question]").nth(8).evaluate(question => {
    const container = question.parentElement;
    container.scrollTop += question.getBoundingClientRect().top - container.getBoundingClientRect().top - 16;
  });
  await navigation.getByRole("button", { name: /^跳转到第 9 条提问/, current: "step" }).waitFor();
  await page.waitForFunction(() => {
    const rail = document.querySelector('nav[aria-label="会话导航"] > div');
    return rail.scrollTop === rail.firstElementChild.clientHeight;
  });
  await lastQuestion.click();
  await navigation.locator('button[aria-current="step"]').and(lastQuestion).waitFor();
  await page.waitForFunction(() => {
    const question = Array.from(document.querySelectorAll("[data-chat-question]")).at(-1);
    return question && Math.abs(question.getBoundingClientRect().top - question.parentElement.getBoundingClientRect().top - 16) < 2;
  });
  assert.deepEqual(errors, []);
  console.log(`PASS: continuous delivery, queue/steer intent, refresh and resume, stopping across browser sessions, 390px layout, long-session navigation, and Beijing timestamps. Screenshots: ${artifacts}`);
} finally { await browser.close(); }
