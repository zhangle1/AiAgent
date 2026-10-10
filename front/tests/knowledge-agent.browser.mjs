// Existing frontend + synthetic APIs + a local chunked SSE fixture; no live model or user data.
import assert from "node:assert/strict";
import http from "node:http";
import path from "node:path";
import { createRequire } from "node:module";
const require = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const files = ["a", "b"].map(name => ({ uri: root.uri + name + ".md", name: name + ".md", parent_uri: root.uri, kind: "file", status: "processed", size: 400 }));
const options = {
  llm_models: [{ id: "api-one", name: "API One", profile: "已配置 API", context_window: 64000, native_tools: true, is_default: true }, { id: "api-two", name: "API Two", profile: "另一个 API", context_window: 32000, native_tools: true }],
  cli_policy: { models: [{ id: "profile-team", name: "Team CLI", profile_name: "team", reasoning_efforts: ["low", "medium"] }], allowed_model_ids: ["profile-team"], default_model_id: "profile-team", default_reasoning_effort: "medium" },
  compiler: { generator: "llm_api", retrieval_mode: "wiki", model_id: "api-one", max_steps: 48, timeout_minutes: 20 },
};
const context = { estimated_input_tokens: 7200, input_limit: 61376, context_window: 64000, output_reserve: 1600, reserve: 1024, estimation: "utf8_upper_bound", compression_before: 24000, compression_after: 7200, compression_ratio: .7, compactions: 1, model_calls: 3, model_call_limit: 16, native_tools: true };
const streamRequests = []; const compileRequests = []; const commandRequests = [];
const server = http.createServer(async (request, response) => {
  response.setHeader("Access-Control-Allow-Origin", "*");
  response.setHeader("Access-Control-Allow-Headers", "*");
  if (request.method === "OPTIONS") { response.end(); return; }
  let body = ""; for await (const chunk of request) body += chunk;
  const input = JSON.parse(body); streamRequests.push(input);
  response.writeHead(200, { "Content-Type": "text/event-stream; charset=utf-8", "Cache-Control": "no-cache" });
  const send = (type, content = "", metadata = {}) => response.write(`data: ${JSON.stringify({ type, content, metadata })}\r\n\r\n`);
  send("started", root.uri); send("context_compacted", "", context);
  send("tool", "ls", { call_id: "tool1", name: "ls", arguments: "{}" });
  send("tool_result", JSON.stringify({ nodes: files, total: 2 }), { call_id: "tool1", name: "ls", success: true });
  send("content", "流式");
  if (input.message === "中断测试") { response.end(); return; }
  const timer = setTimeout(() => { if (!response.destroyed) { send("content", "答案"); send("done"); response.end(); } }, input.message === "停止测试" ? 10000 : 900);
  response.on("close", () => clearTimeout(timer));
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1800, height: 1000 } });
const errors = []; page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url()); const pathname = url.pathname;
  if (pathname.endsWith("/knowledge-agent/stream")) return route.continue({ url: `http://127.0.0.1:${server.address().port}/stream` });
  let body = [];
  if (pathname.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture" };
  if (pathname.endsWith("/sessions/list")) body = { sessions: [] };
  if (pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (pathname.endsWith("/knowledge-resources/tree")) body = [root, ...files];
  if (pathname.endsWith("/knowledge-resources/read")) body = { node: [root, ...files].find(node => node.uri === url.searchParams.get("uri")) ?? root, children: files, source_text: "原文", semantic_status: "missing_or_stale" };
  if (pathname.endsWith("/knowledge-resources/task")) body = null;
  if (pathname.endsWith("/knowledge-agent/options")) body = options;
  if (pathname.endsWith("/knowledge-agent/command")) { commandRequests.push(route.request().postDataJSON()); body = { uri: root.uri, nodes: files, total: 2, next_offset: null }; }
  if (pathname.endsWith("/knowledge-agent/compile")) { compileRequests.push(route.request().postDataJSON()); body = { items: [], warnings: [], tasks: [{ id: 42, status: "queued" }] }; }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});

try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  const model = page.getByRole("combobox", { name: "知识 Agent LLM API 模型" });
  await model.getByRole("option", { name: /API Two/ }).waitFor({ state: "attached" });
  await model.selectOption("api-two");
  const input = page.getByRole("textbox", { name: "知识 Agent 输入" });
  await input.fill("分析资料"); await page.getByRole("button", { name: "发送知识问题" }).click();
  await page.getByText("流式", { exact: true }).waitFor();
  assert.equal(await page.getByText("流式答案", { exact: true }).count(), 0, "The first chunk renders before completion");
  await page.getByText("流式答案", { exact: true }).waitFor();
  await page.getByRole("status").filter({ hasText: "本轮完成" }).waitFor();
  assert.equal(streamRequests[0].model_id, "api-two");
  assert.match(await page.locator("main").innerText(), /压缩 70%/);
  assert.match(await page.locator("main").innerText(), /7,200 \/ 61,376/);
  await page.getByRole("button", { name: "a.md", exact: true }).first().click();
  await page.getByText("原文", { exact: true }).last().waitFor();
  assert.equal(await page.getByText("流式答案", { exact: true }).count(), 1, "Previewing a source within the same folder keeps the Agent conversation");
  await page.getByText("ls 已完成", { exact: false }).click();
  assert.match(await page.locator("main").innerText(), /call|nodes/);
  await page.getByRole("button", { name: "/ls", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "/ls · 目录命令" });
  await dialog.getByRole("button", { name: "执行命令", exact: true }).click();
  await dialog.getByRole("button", { name: "文件 · a.md", exact: true }).waitFor();
  assert.equal(commandRequests[0].uri, root.uri);
  await dialog.getByRole("button", { name: "关闭", exact: true }).click();
  await page.getByRole("button", { name: "/compiler", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "编译原始资料 · L0 / L1" });
  await dialog.getByRole("combobox", { name: "编译生成通道" }).selectOption("codex");
  await dialog.getByRole("combobox", { name: "编译模型" }).selectOption("profile-team");
  await dialog.getByRole("button", { name: "提交编译任务" }).click();
  await dialog.getByRole("status").waitFor();
  assert.deepEqual(compileRequests[0].uris, files.map(file => file.uri));
  assert.equal(compileRequests[0].configuration.generator, "codex");
  assert.equal(compileRequests[0].configuration.model_id, "profile-team");
  assert.equal(options.compiler.generator, "llm_api", "Global compiler settings are not mutated");
  await dialog.getByRole("button", { name: "关闭", exact: true }).click();
  await input.fill("中断测试"); await page.getByRole("button", { name: "发送知识问题" }).click();
  await page.getByRole("alert").filter({ hasText: "连接在任务完成前中断" }).waitFor();
  assert.equal(await page.getByText("流式", { exact: true }).count(), 1);
  await input.fill("停止测试"); await page.getByRole("button", { name: "发送知识问题" }).click();
  await page.getByRole("button", { name: "停止知识 Agent" }).waitFor();
  await page.getByRole("button", { name: "停止知识 Agent" }).click();
  await page.getByRole("alert").filter({ hasText: "已停止" }).waitFor();
  assert.equal(await page.getByRole("alert").filter({ hasText: "连接在任务完成前中断" }).count(), 1, "Stopping a new turn preserves the earlier failure detail");
  assert.equal(await page.getByRole("button", { name: "停止知识 Agent" }).count(), 0);
  if (process.env.KNOWLEDGE_TEST_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_SCREENSHOT });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(() => document.querySelector("main")?.getBoundingClientRect().left === 0);
  assert.equal(await page.locator("body").evaluate(node => node.scrollWidth > innerWidth), false);
  await page.getByRole("button", { name: "/compiler", exact: true }).click();
  await page.getByRole("dialog", { name: "编译原始资料 · L0 / L1" }).waitFor();
  assert.equal(await page.getByRole("dialog").evaluate(node => node.getBoundingClientRect().right <= innerWidth), true);
  await page.keyboard.press("Escape");
  assert.deepEqual(errors, []);
  console.log("PASS: real SSE chunks, selected API, tools, context ratio, ls dialog, CLI compile snapshot, interrupted stream and cancellation");
} catch (error) {
  console.log("Browser errors:", errors); console.log((await page.locator("body").innerText()).slice(-2500));
  throw error;
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
