// Existing frontend + synthetic APIs + a local chunked SSE fixture; no live model or user data.
import assert from "node:assert/strict";
import http from "node:http";
import path from "node:path";
import { createRequire } from "node:module";
const require = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const project = { uri: "viking://projects/", name: "项目", kind: "directory" };
const destination = { uri: project.uri + "BOS/", parent_uri: project.uri, name: "BOS 项目", kind: "directory" };
const files = ["a", "b"].map(name => ({ uri: root.uri + name + ".md", name: name + ".md", parent_uri: root.uri, kind: "file", status: "processed", size: 400 }));
const options = {
  llm_models: [{ id: "api-one", name: "API One", profile: "已配置 API", context_window: 64000, native_tools: true, is_default: true }, { id: "api-two", name: "API Two", profile: "另一个 API", context_window: 32000, native_tools: true }],
  cli_policy: { models: [{ id: "profile-team", name: "Team CLI", profile_name: "team", reasoning_efforts: ["low", "medium"] }], allowed_model_ids: ["profile-team"], default_model_id: "profile-team", default_reasoning_effort: "medium" },
  compiler: { generator: "llm_api", retrieval_mode: "wiki", model_id: "api-one", max_steps: 48, timeout_minutes: 20 },
  skills: [{ id: "llm-wiki", name: "LLM Wiki · L0/L1", description: "保留来源的文件摘要和目录概览", instructions: "保留重要实体与不确定性", builtin: true }],
};
const context = { estimated_input_tokens: 7200, input_limit: 61376, context_window: 64000, output_reserve: 1600, reserve: 1024, estimation: "utf8_upper_bound", compression_before: 24000, compression_after: 7200, compression_ratio: .7, compactions: 1, model_calls: 3, model_call_limit: 16, native_tools: true };
const streamRequests = []; const compileRequests = []; const commandRequests = []; const skillDraftRequests = []; const uploadRequests = [];
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
const withoutRandomUUID = process.env.KNOWLEDGE_TEST_NO_RANDOM_UUID === "1";
if (withoutRandomUUID) await page.addInitScript(() => Object.defineProperty(crypto, "randomUUID", { value: undefined, configurable: true }));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url()); const pathname = url.pathname;
  if (pathname.endsWith("/knowledge-agent/stream")) return route.continue({ url: `http://127.0.0.1:${server.address().port}/stream` });
  let body = [];
  if (pathname.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture" };
  if (pathname.endsWith("/sessions/list")) body = { sessions: [] };
  if (pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (pathname.endsWith("/knowledge-resources/tree")) body = [root, project, destination, ...files];
  if (pathname.endsWith("/knowledge-resources/read")) body = { node: [root, ...files].find(node => node.uri === url.searchParams.get("uri")) ?? root, children: files, source_text: "原文", semantic_status: "missing_or_stale" };
  if (pathname.endsWith("/knowledge-resources/task")) body = null;
  if (pathname.endsWith("/knowledge-agent/options")) body = options;
  if (pathname.endsWith("/knowledge-agent/command")) { commandRequests.push(route.request().postDataJSON()); body = { uri: root.uri, nodes: files, total: 2, next_offset: null }; }
  if (pathname.endsWith("/knowledge-agent/compile")) { compileRequests.push(route.request().postDataJSON()); body = { items: [], warnings: [], tasks: [{ id: 42, status: "queued" }] }; }
  if (pathname.endsWith("/knowledge-agent/skills/draft")) { skillDraftRequests.push(route.request().postDataJSON()); body = { reply: "已根据需求生成技能草稿。", skill: { id: "", name: "BOS 资料编译", description: "保留 BOS 参数和生命周期", instructions: "保留插件类型、生命周期、参数、版本约束和来源。", builtin: false } }; }
  if (pathname.endsWith("/knowledge-agent/skills")) { body = { ...route.request().postDataJSON(), id: "skill-bos" }; options.skills.push(body); }
  if (pathname.endsWith("/knowledge-resources/upload")) { uploadRequests.push(route.request().postData()); body = { items: [], warnings: [], tasks: [] }; }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});

try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  const model = page.getByRole("combobox", { name: "知识 Agent LLM API 模型" });
  await model.getByRole("option", { name: /API Two/ }).waitFor({ state: "attached" });
  await model.selectOption("api-two");
  const input = page.getByRole("textbox", { name: "知识 Agent 输入" });
  await input.fill("分析资料"); await page.getByRole("button", { name: "发送知识问题" }).click();
  // ID creation must not throw before the stream starts on an HTTP LAN origin.
  await page.getByText("分析资料", { exact: true }).waitFor({ timeout: 5000 }).catch(reason => { assert.deepEqual(errors, [], "Sending a question must work without crypto.randomUUID"); throw reason; });
  assert.deepEqual(errors, [], "Sending a question must work without crypto.randomUUID");
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
  await page.getByRole("button", { name: "浏览目录 /ls", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "浏览目录 · /ls" });
  await dialog.getByRole("button", { name: "选择资源", exact: true }).click();
  let picker = page.getByRole("dialog", { name: "选择命令目标", exact: true });
  await picker.getByRole("textbox", { name: "选择命令目标筛选" }).fill("资料");
  await picker.getByRole("treeitem", { name: "资料", exact: true }).click();
  await picker.getByRole("button", { name: "使用此位置" }).click();
  await dialog.getByRole("button", { name: "执行命令", exact: true }).click();
  await dialog.getByRole("button", { name: "文件 · a.md", exact: true }).waitFor();
  assert.equal(commandRequests[0].uri, root.uri);
  assert.equal(await dialog.getByRole("alert").count(), 0, "Command results must not fail while creating message IDs");
  await dialog.getByRole("button", { name: "关闭", exact: true }).click();
  await page.getByRole("button", { name: "读取正文 /read", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "读取正文 · /read", exact: true });
  await dialog.getByRole("button", { name: "选择资源", exact: true }).click();
  picker = page.getByRole("dialog", { name: "选择命令目标", exact: true });
  await picker.getByRole("textbox", { name: "选择命令目标筛选" }).fill("a.md");
  await picker.getByRole("treeitem", { name: "a.md", exact: true }).click();
  await picker.getByRole("button", { name: "使用此位置" }).click();
  assert.equal(await dialog.getByRole("textbox", { name: "命令目标 URI" }).inputValue(), files[0].uri);
  await dialog.getByRole("button", { name: "执行命令", exact: true }).click();
  await dialog.getByRole("button", { name: "文件 · a.md", exact: true }).waitFor();
  assert.equal(commandRequests[1].target_uri, files[0].uri);
  await dialog.getByRole("button", { name: "关闭", exact: true }).click();
  await page.getByRole("button", { name: "编译资料 /compiler", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "编译原始资料 · L0 / L1" });
  assert.equal(await dialog.getByRole("spinbutton", { name: "编译调用上限" }).inputValue(), "0", "New tasks default to automatic calls even if legacy global settings contain 48");
  await dialog.getByRole("spinbutton", { name: "编译调用上限" }).fill("200");
  assert.equal(await dialog.getByRole("spinbutton", { name: "编译调用上限" }).inputValue(), "200");
  await dialog.getByRole("spinbutton", { name: "编译调用上限" }).fill("0");
  await dialog.getByRole("textbox", { name: "编译来源筛选" }).fill("a.md");
  assert.equal(await dialog.getByRole("treeitem", { name: "b.md", exact: true }).count(), 0);
  await dialog.getByRole("textbox", { name: "编译来源筛选" }).fill("");
  await dialog.getByRole("button", { name: "选择产物目录", exact: true }).click();
  picker = page.getByRole("dialog", { name: "选择产物目录", exact: true });
  await picker.getByRole("textbox", { name: "选择产物目录筛选" }).fill("BOS");
  await picker.getByRole("treeitem", { name: "BOS 项目", exact: true }).click();
  await picker.getByRole("button", { name: "使用此位置" }).click();
  await dialog.getByRole("button", { name: "生成 / 编辑技能" }).click();
  const skillDialog = page.getByRole("dialog", { name: "编译技能对话", exact: true });
  await skillDialog.getByRole("textbox", { name: "技能需求" }).fill("为 BOS 项目生成技能，保留参数与生命周期");
  await skillDialog.getByRole("button", { name: "生成 / 调整技能" }).click();
  await skillDialog.getByRole("textbox", { name: "技能名称" }).waitFor();
  await page.waitForFunction(() => document.querySelector('input[aria-label="技能名称"]')?.value === "BOS 资料编译");
  await skillDialog.getByRole("textbox", { name: "技能需求" }).fill("还要保留版本约束");
  await skillDialog.getByRole("button", { name: "生成 / 调整技能" }).click();
  await page.waitForFunction(() => !document.querySelector('textarea[aria-label="技能需求"]')?.disabled);
  assert.equal(skillDraftRequests[1].history.length, 2);
  assert.match(skillDraftRequests[1].draft.instructions, /生命周期/);
  if (process.env.KNOWLEDGE_TEST_SKILL_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_SKILL_SCREENSHOT });
  await skillDialog.getByRole("button", { name: "保存并用于编译" }).click();
  await skillDialog.waitFor({ state: "detached" });
  assert.equal(await dialog.getByRole("combobox", { name: "编译技能" }).inputValue(), "skill-bos");
  await dialog.getByRole("combobox", { name: "编译生成通道" }).selectOption("codex");
  await dialog.getByRole("combobox", { name: "编译模型" }).selectOption("profile-team");
  if (process.env.KNOWLEDGE_TEST_COMPILER_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_COMPILER_SCREENSHOT });
  await dialog.getByRole("button", { name: "提交编译任务" }).click();
  await dialog.getByRole("status").waitFor();
  assert.deepEqual(compileRequests[0].uris, files.map(file => file.uri));
  assert.equal(compileRequests[0].configuration.generator, "codex");
  assert.equal(compileRequests[0].configuration.model_id, "profile-team");
  assert.equal(compileRequests[0].configuration.target_uri, destination.uri);
  assert.equal(compileRequests[0].configuration.skill_id, "skill-bos");
  assert.equal(compileRequests[0].configuration.max_steps, 0);
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
  await page.getByRole("button", { name: "添加资源", exact: true }).click();
  const addDialog = page.getByRole("dialog", { name: "添加资源", exact: true });
  await addDialog.getByRole("button", { name: "浏览目录", exact: true }).click();
  picker = page.getByRole("dialog", { name: "选择添加资源目录", exact: true });
  await picker.getByRole("textbox", { name: "选择添加资源目录筛选" }).fill("BOS");
  await picker.getByRole("treeitem", { name: "BOS 项目", exact: true }).click();
  await picker.getByRole("button", { name: "使用此位置" }).click();
  await addDialog.locator('input[type="file"]').setInputFiles({ name: "fixture.md", mimeType: "text/markdown", buffer: Buffer.from("# synthetic fixture") });
  await addDialog.getByRole("button", { name: "开始处理", exact: true }).click();
  await addDialog.waitFor({ state: "detached" });
  assert.match(uploadRequests[0], /viking:\/\/projects\/BOS\//, "Upload uses the chosen target, not the selected preview's directory");
  if (process.env.KNOWLEDGE_TEST_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_SCREENSHOT });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(() => document.querySelector("main")?.getBoundingClientRect().left === 0);
  assert.equal(await page.locator("body").evaluate(node => node.scrollWidth > innerWidth), false);
  await page.getByRole("button", { name: "编译资料 /compiler", exact: true }).click();
  await page.getByRole("dialog", { name: "编译原始资料 · L0 / L1" }).waitFor();
  assert.equal(await page.getByRole("dialog").evaluate(node => node.getBoundingClientRect().right <= innerWidth), true);
  if (process.env.KNOWLEDGE_TEST_MOBILE_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_MOBILE_SCREENSHOT });
  await page.keyboard.press("Escape");
  assert.deepEqual(errors, []);
  console.log(`PASS: SSE, Chinese commands, filtered target/source trees, skill conversation/save/select, CLI target+skill snapshot, interruption/cancellation/mobile; randomUUID ${withoutRandomUUID ? "unavailable" : "native"}`);
} catch (error) {
  console.log("Browser errors:", errors); console.log((await page.locator("body").innerText()).slice(-2500));
  throw error;
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
