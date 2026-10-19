// Existing frontend, synthetic resource/agent APIs; no real documents, credentials or model calls.
import assert from "node:assert/strict";
import path from "node:path";
import { createRequire } from "node:module";
const require = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1680, height: 1000 } });
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const source = { uri: root.uri + "manual.md", parent_uri: root.uri, name: "manual.md", kind: "file", document_id: 1, status: "processed" };
const parsedRoot = { uri: root.uri + "_parsed/", parent_uri: root.uri, name: "解析正文", kind: "directory" };
const documentRoot = { uri: parsedRoot.uri + "manual-1/", parent_uri: parsedRoot.uri, name: "manual", kind: "directory", source_uri: source.uri };
const section = { uri: documentRoot.uri + "Linux.md", parent_uri: documentRoot.uri, name: "Linux.md", kind: "file", status: "processed", document_id: 1, parsed_document_id: 2, source_uri: source.uri };
const nodes = [root, source, parsedRoot, documentRoot, section];
const options = { compiler: { generator: "llm_api", retrieval_mode: "wiki", max_steps: 0, timeout_minutes: 20, parsing: { split_content: true, max_section_tokens: 2048, max_section_chars: 6000, min_section_tokens: 512 } }, llm_models: [{ id: "fixture", name: "测试模型", context_window: 1000000, native_tools: true, is_default: true }], cli_policy: { models: [], allowed_model_ids: [] }, skills: [{ id: "llm-wiki", name: "内置", builtin: true }] };
const errors = []; const compileRequests = []; const tasksRead = [];
page.on("pageerror", e => errors.push(e.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url()); const name = url.pathname; let body = [];
  if (name.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture" };
  if (name.endsWith("/sessions/list")) body = { sessions: [] };
  if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (name.endsWith("/knowledge-resources/tree")) body = nodes;
  if (name.endsWith("/knowledge-resources/read")) {
    const node = nodes.find(n => n.uri === url.searchParams.get("uri")) ?? root;
    body = { node, children: nodes.filter(n => n.parent_uri === node.uri), semantic_status: node.uri.includes("/_parsed/") ? "not_applicable" : "missing_or_stale", source_text: node === section ? "## Linux 安装\n\n| 参数 | 值 |\n| --- | --- |\n| 模式 | 本地 |" : "完整原文", parsed_content: node === source ? "完整解析正文" : null, parsed_outputs: node === source ? [section] : [] };
  }
  if (name.endsWith("/knowledge-resources/task")) { tasksRead.push(url.searchParams.get("uri")); body = null; }
  if (name.endsWith("/knowledge-agent/options")) body = options;
  if (name.endsWith("/knowledge-agent/compile")) { compileRequests.push(route.request().postDataJSON()); body = { items: [], warnings: [], tasks: [{ id: 3, status: "queued" }] }; }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});

try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  await page.getByRole("button", { name: "manual.md", exact: true }).first().click();
  await page.getByRole("button", { name: "解析正文", exact: true }).last().click();
  await page.getByText("解析章节 · 1 个文件", { exact: true }).click();
  await page.getByRole("button", { name: "manual-1/Linux.md", exact: true }).click();
  await page.getByRole("heading", { name: "Linux.md", exact: true }).waitFor();
  assert.equal(await page.getByRole("button", { name: "重新解析", exact: true }).isDisabled(), true);
  await page.getByRole("table").waitFor();
  assert.equal(await page.getByRole("button", { name: "解析章节", exact: true }).count(), 1);
  assert.equal(await page.getByRole("link", { name: "下载", exact: true }).getAttribute("href"), `/api/v1/knowledge-resources/file?uri=${encodeURIComponent(section.uri)}&download=true`);
  assert(!tasksRead.includes(section.uri), "Parsed sections must not poll source compilation jobs");
  await page.screenshot({ path: process.env.KNOWLEDGE_PARSING_SCREENSHOT ?? "C:/Users/zhang/AppData/Local/Temp/aiagent-parsing-desktop.png", fullPage: true });
  await page.getByRole("button", { name: "返回原始文件", exact: true }).click();
  await page.getByRole("heading", { name: "manual.md", exact: true }).waitFor();
  await page.getByRole("button", { name: /编译资料.*\/compiler/ }).click();
  const dialog = page.getByRole("dialog", { name: /编译原始资料/ }); await dialog.waitFor();
  assert.equal(await dialog.getByRole("spinbutton", { name: "章节 token 估值" }).inputValue(), "2048");
  await dialog.getByRole("spinbutton", { name: "章节字符上限" }).fill("9000");
  await dialog.getByRole("checkbox", { name: "按标题和段落拆分章节" }).uncheck();
  assert.equal(await dialog.getByRole("spinbutton", { name: "章节字符上限" }).count(), 0);
  assert.equal(await dialog.getByRole("treeitem", { name: "Linux.md" }).count(), 0);
  await dialog.getByRole("button", { name: "提交编译任务", exact: true }).click();
  await page.getByText(/1 个任务已入队/).first().waitFor();
  assert.equal(compileRequests[0].configuration.parsing.split_content, false);
  assert.equal(compileRequests[0].configuration.parsing.max_section_chars, 9000);
  await dialog.getByRole("button", { name: "关闭命令配置" }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(() => document.querySelector("main")?.getBoundingClientRect().left === 0);
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
  await page.screenshot({ path: "C:/Users/zhang/AppData/Local/Temp/aiagent-parsing-mobile.png", fullPage: true });
  assert.deepEqual(errors, []);
  console.log("PASS: parsed layout links, readonly preview/download, GFM tables, no task polling, parser options snapshot, source filtering and mobile width");
} finally { await browser.close(); }
