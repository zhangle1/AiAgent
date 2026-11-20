// Synthetic responses only: no real user files, models or databases.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir } from "node:fs/promises";
import path from "node:path";
const dependencies = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = dependencies("playwright");
const artifacts = process.env.CHAT_TEST_ARTIFACTS;
if (artifacts) await mkdir(artifacts, { recursive: true });
const bookName = "Design and Optimization of Manufacturing Systems, Second Edition.pdf";
const nodes = [
  { uri: "viking://resources/", name: "资料", kind: "directory" },
  { uri: "viking://resources/books/", parent_uri: "viking://resources/", name: "书籍", kind: "directory" },
  { uri: "viking://resources/books/design/", parent_uri: "viking://resources/books/", name: "设计", kind: "directory" },
  { uri: "viking://resources/books/design/deep/", parent_uri: "viking://resources/books/design/", name: "深层原则", kind: "directory" },
  { uri: "viking://resources/standards/", parent_uri: "viking://resources/", name: "规范", kind: "directory" },
  { uri: "viking://resources/books/design/one.md", parent_uri: "viking://resources/books/design/", name: "one.md", kind: "file", document_id: 1, knowledge_base_name: "fixture-books" },
  { uri: "viking://resources/standards/two.md", parent_uri: "viking://resources/standards/", name: "two.md", kind: "file", document_id: 2, knowledge_base_name: "fixture-standards" },
  { uri: "viking://resources/books/design/examples/", parent_uri: "viking://resources/books/design/", name: "设计示例", kind: "directory" },
  { uri: "viking://resources/Manufacturing%20Systems.pdf", parent_uri: "viking://resources/", name: bookName, kind: "file", document_id: 10, knowledge_base_name: "resource-store" },
  { uri: "viking://resources/Manufacturing%20Systems.pdf-other.md", parent_uri: "viking://resources/", name: "同名前缀的另一份资料.md", kind: "file", document_id: 11, knowledge_base_name: "prefix-base" },
];
const legacySession = "fixture-legacy-book";
const legacyPath = "Part II/Chapter 12/12.3.2 Statistical Process Control_1.md";
const encodedLegacyPath = legacyPath.split("/").map(encodeURIComponent).join("/");
const legacyChapter = {
  uri: `viking://resources/_parsed/Manufacturing%20Systems-10/${encodedLegacyPath}`,
  name: legacyPath.split("/").at(-1), kind: "file", extension: ".md", document_id: 10,
  source_uri: nodes[8].uri, parsed_document_id: 31,
};
const projectMarkdown = { repository_name: "fixture", path: "docs/project-guide.md", name: "project-guide.md", preview_kind: "markdown", extension: ".md" };
nodes.push(legacyChapter);
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
page.setDefaultTimeout(15000);
const errors = [], sent = [], legacyFileReads = [];
let releaseLegacyTree;
const legacyTree = new Promise(resolve => { releaseLegacyTree = resolve; });
page.on("pageerror", error => errors.push(error.message));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url());
  const name = url.pathname;
  let body = [];
  if (name.endsWith("/auth/status")) body = { authenticated: true, user_id: "fixture", username: "fixture", role: "admin" };
  else if (name === "/api/v1/settings") body = { ui: { theme: "light", language: "zh-CN", preferred_agent: "codex" }, catalog: { version: 1, services: { llm: { active_profile_id: "fixture", active_model_id: "fixture", profiles: [{ id: "fixture", name: "Fixture", models: [{ id: "fixture", name: "Fixture", model: "fixture" }] }] } } } };
  else if (name === "/api/v1/settings/ui") body = { theme: "light", language: "zh-CN" };
  else if (name === "/api/v1/knowledge-resources/tree") { await legacyTree; body = nodes; }
  else if (name === "/api/v1/knowledge-resources/read") {
    legacyFileReads.push(Object.fromEntries(url.searchParams));
    body = { node: legacyChapter, source_text: "# 可打开的历史章节\n" };
  }
  else if (name.endsWith("/sessions/list")) body = { sessions: [] };
  else if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  else if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "name" };
  else if (name.endsWith("/agent-providers/environments")) body = [{ id: "codex", name: "Codex", installed: true, chat_supported: true, protocol: "app-server", command: "fixture", message: "" }];
  else if (name.endsWith("/codex-model-policy")) body = { models: [{ id: "fixture", name: "Fixture Codex", description: "", profile_name: null, supports_reasoning_effort: false, reasoning_efforts: [], is_builtin: true, image_input: "none" }], allowed_model_ids: ["fixture"], default_model_id: "fixture", allow_chat_model_override: true, allowed_reasoning_efforts: [], default_reasoning_effort: "", profile_models: [] };
  else if (name.endsWith("/image-ocr-policy")) body = { enabled: false, native_image_input_enabled: false };
  else if (name === "/api/v1/code-repositories/projects") body = [{ id: 11, name: "fixture", display_name: "测试项目", root_path: "fixture", repositories: [], repository_count: 0 }];
  else if (name === "/api/v1/code-repositories/projects/11/markdown-documents") body = [projectMarkdown];
  else if (name === "/api/v1/code-repositories/projects/11/documents/preview") {
    assert.equal(url.searchParams.get("path"), projectMarkdown.path);
    body = { content: "# 项目 Markdown 预览\n\n**原有项目文档。**" };
  }
  else if (name.endsWith("/git/status")) body = { project_id: 11, repositories: [], state: "ready", message: "" };
  else if (name === "/api/v1/code-runtime/projects/11") body = { project_id: 11, profiles: [], runs: [] };
  else if (/\/sessions\/[^/]+\/goal$/.test(name)) body = { goal: null };
  else if (name.endsWith(`/sessions/${legacySession}`)) body = {
    id: legacySession, title: "历史书籍会话", project_id: 11, preferences: { knowledge_scope_uris: [legacyChapter.source_uri], agent: "codex" },
    messages: [{ id: 1, role: "user", content: "阅读所选书籍" }, { id: 2, role: "assistant", content: `实际阅读：[章节](${encodedLegacyPath})\n\n[项目文档](${projectMarkdown.path})` }],
  };
  else if (/\/sessions\/[^/]+$/.test(name)) body = { id: name.split("/").at(-1), title: "Fixture", messages: [], preferences: {}, project_id: 11 };
  await route.fulfill({ json: body });
});
await page.routeWebSocket("**/api/v1/chat/ws", socket => socket.onMessage(message => {
  sent.push(JSON.parse(String(message)));
  socket.send(JSON.stringify({ type: "content", content: "合成响应" }));
  socket.send(JSON.stringify({ type: "completed" }));
}));
const trigger = () => page.getByRole("button", { name: /^知识目录/ }).filter({ visible: true });
const checkbox = name => page.getByRole("checkbox", { name, exact: true });
const open = async () => { await trigger().click(); await page.getByPlaceholder("搜索目录").waitFor(); };
const retained = async name => {
  assert.equal(await page.getByPlaceholder("搜索目录").count(), 1, "clicking inside the scope picker must not close it");
  assert.equal(await checkbox(name).isChecked(), true, "checkbox draft must remain checked");
};
try {
  const historyUrl = new URL(process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat");
  historyUrl.searchParams.set("session", legacySession);
  await page.goto(historyUrl.toString());
  await page.getByTitle(`在右侧项目文档中打开 ${legacyChapter.name}`).waitFor({ timeout: 30000 });
  releaseLegacyTree();
  await page.getByTitle(`预览资料库章节 ${legacyChapter.name}`).click();
  await page.getByRole("region", { name: "资料库章节预览" }).getByRole("heading", { name: "可打开的历史章节" }).waitFor();
  assert.equal(await page.getByRole("dialog").count(), 0);
  assert.ok(legacyFileReads.length > 0);
  for (const read of legacyFileReads) assert.deepEqual(read, { uri: legacyChapter.uri });
  await page.getByTitle(`在右侧项目文档中打开 ${projectMarkdown.name}`).click();
  await page.getByRole("heading", { name: "项目 Markdown 预览" }).waitFor();
  assert.equal(await page.getByRole("region", { name: "资料库章节预览" }).count(), 0);
  await page.getByRole("button", { name: "关闭右侧面板", exact: true }).click();
  await page.goto(process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat?project=11");
  await page.locator('[contenteditable="true"]:visible').first().waitFor({ timeout: 60000 });
  await open();
  await checkbox(bookName).waitFor();
  await checkbox("设计").click();
  await retained("设计");
  await checkbox("规范").click();
  await retained("设计"); await retained("规范");
  const designRow = checkbox("设计").locator("..").locator("..");
  await designRow.getByRole("button", { name: "展开目录", exact: true }).click();
  await checkbox("深层原则").waitFor();
  await retained("深层原则"); await retained("设计示例");
  await designRow.getByRole("button", { name: "收起目录", exact: true }).click();
  assert.equal(await checkbox("深层原则").count(), 0);
  await retained("设计");
  // Ancestor rerenders (typing in the composer) must not reset the local draft.
  await page.locator('[contenteditable="true"]:visible').first().evaluate(element => { element.textContent = "合成草稿"; element.dispatchEvent(new InputEvent("input", { bubbles: true })); });
  await retained("设计");
  await page.getByPlaceholder("搜索目录").fill("深层原则");
  await checkbox("深层原则").waitFor();
  await page.getByPlaceholder("搜索目录").press("Enter");
  assert.equal(sent.length, 0, "search Enter must not submit the enclosing chat form");
  await page.getByPlaceholder("搜索目录").fill("");
  await page.getByRole("button", { name: "确定", exact: true }).click();
  assert.match(await trigger().innerText(), /2/);
  await open(); await checkbox("规范").click(); await page.keyboard.press("Escape");
  await open(); await retained("规范");
  await checkbox("规范").click();
  await page.getByRole("heading", { name: "你想了解什么？" }).click();
  await open(); await retained("规范");
  await page.getByRole("button", { name: "清空", exact: true }).click();
  assert.equal(await checkbox("规范").isChecked(), false);
  await page.getByRole("button", { name: "取消", exact: true }).click();
  await open(); await retained("设计"); await retained("规范");
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "scope-picker-desktop.png") });
  await page.getByRole("button", { name: "取消", exact: true }).click();
  const editor = page.locator('[contenteditable="true"]:visible').first();
  await editor.fill("按选定目录检查代码");
  await editor.press("Enter");
  await page.waitForFunction(() => document.body.innerText.includes("合成响应"));
  assert.deepEqual(sent.at(-1).knowledge_scope_uris, [nodes[2].uri, nodes[4].uri]);
  assert.equal(sent.at(-1).agent, "codex");
  // Confirmed parent selections restore their descendants, including collapsed rows.
  await open();
  await checkbox("设计").locator("..").locator("..").getByRole("button", { name: "展开目录", exact: true }).click();
  await retained("深层原则"); await retained("设计示例");
  await checkbox("深层原则").click();
  assert.equal(await checkbox("设计").evaluate(element => element.indeterminate), true);
  assert.equal(await checkbox("书籍").evaluate(element => element.indeterminate), true);
  await retained("设计示例"); await retained("规范");
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "scope-picker-cascade-desktop.png") });
  await page.getByRole("button", { name: "确定", exact: true }).click();
  await editor.fill("只检查仍勾选的目录"); await editor.press("Enter");
  await page.waitForFunction(() => document.body.innerText.includes("只检查仍勾选的目录"));
  await page.waitForTimeout(250);
  assert.deepEqual([...sent.at(-1).knowledge_scope_uris].sort(), [nodes[5].uri, nodes[7].uri, nodes[4].uri].sort(), "unchecked descendants must not remain covered by a submitted ancestor; direct files stay selected");
  assert.equal(sent.at(-1).agent, "codex");
  await open();
  assert.equal(await checkbox("深层原则").isChecked(), false);
  assert.equal(await checkbox("设计").evaluate(element => element.indeterminate), true);
  // A filtered parent still selects/deselects every hidden descendant.
  await page.getByPlaceholder("搜索目录").fill("书籍");
  await checkbox("书籍").click();
  await page.getByPlaceholder("搜索目录").fill("");
  await retained("设计"); await retained("深层原则"); await retained("设计示例");
  await page.getByPlaceholder("搜索目录").fill("书籍");
  await checkbox("书籍").click();
  await page.getByPlaceholder("搜索目录").fill("");
  for (const name of ["书籍", "设计", "深层原则", "设计示例"]) {
    assert.equal(await checkbox(name).isChecked(), false);
    assert.equal(await checkbox(name).evaluate(element => element.indeterminate), false);
  }
  await retained("规范");
  await page.keyboard.press("Escape");
  await open();
  await page.getByRole("button", { name: "清空", exact: true }).click();
  await checkbox(bookName).click();
  await page.getByRole("button", { name: "确定", exact: true }).click();
  await open(); await retained(bookName);
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "scope-picker-book-desktop.png") });
  await page.getByRole("button", { name: "取消", exact: true }).click();
  await editor.fill("只按勾选的一本书检查代码"); await editor.press("Enter");
  await page.waitForTimeout(250);
  assert.deepEqual(sent.at(-1).knowledge_scope_uris, [nodes[8].uri]);
  assert.deepEqual(sent.at(-1).knowledge_base_names, ["resource-store"]);
  assert.equal(sent.at(-1).agent, "codex");
  await page.setViewportSize({ width: 390, height: 844 });
  // Open a fresh page so mobile controls start in their initial state.
  await page.goto(process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat?project=11");
  await page.locator('[contenteditable="true"]:visible').first().waitFor();
  // Next dev's floating badge overlaps bottom-sheet buttons at 390px. It is not product UI;
  // pageerror assertions below still report runtime errors.
  await page.addStyleTag({ content: "nextjs-portal { display: none; }" });
  await page.locator('[contenteditable="true"]:visible').first().click();
  await page.getByRole("button", { name: "选择知识目录", exact: true }).click();
  await checkbox("设计").click(); await retained("设计");
  await page.getByPlaceholder("搜索目录").fill("深层原则");
  await retained("深层原则");
  await page.getByPlaceholder("搜索目录").fill("");
  await checkbox("规范").click(); await retained("规范");
  await page.keyboard.press("Escape");
  assert.equal(await page.getByPlaceholder("搜索目录").count(), 0, "mobile Escape cancels draft");
  await page.getByRole("button", { name: "选择知识目录", exact: true }).click();
  assert.equal(await checkbox("设计").isChecked(), false);
  await checkbox("设计").click();
  await page.mouse.click(180, 200);
  await page.getByRole("button", { name: "选择知识目录", exact: true }).click();
  assert.equal(await checkbox("设计").isChecked(), false, "mobile outside click discards draft");
  await checkbox("规范").click();
  await checkbox(bookName).click(); await retained(bookName);
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "scope-picker-mobile.png") });
  await page.getByRole("button", { name: "确定", exact: true }).click();
  assert.equal(errors.length, 0, errors.join("\n"));
  console.log("PASS: late catalog updates historical chapter cards; exact file scopes, parent cascade, draft and selection on desktop/mobile.");
} finally { releaseLegacyTree(); await browser.close(); }
