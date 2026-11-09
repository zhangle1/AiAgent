import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
import { mkdir } from "node:fs/promises";
const require = createRequire(import.meta.url);
const dependencies = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : require;
const { chromium } = dependencies("playwright");
const browser = await chromium.launch({ headless: true, ...(process.env.KNOWLEDGE_TEST_BROWSER ? { executablePath: process.env.KNOWLEDGE_TEST_BROWSER } : { channel: "msedge" }) });
const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
let config = { remote_url: "", branch: "main" };
let pushed = false;
let failNextPush = false;
let refreshCalls = 0;
let expectedUris = [];
let expectedResourceUris;
let expectedForce = true;
const artifacts = process.env.KNOWLEDGE_TEST_ARTIFACTS;
const owner = "viking://user/fixture/";
const session = { uri: owner + "sessions/one.md", parent_uri: owner + "sessions/", name: "工序派工.md", kind: "file", document_id: 1, extension: ".md", can_publish: true };
const another = { ...session, uri: owner + "sessions/two.md", name: "另一份会话.md", document_id: 2 };
const nodes = [
  { uri: "viking://resources/", kind: "directory", name: "资料", can_publish: true },
  { uri: "viking://projects/", kind: "directory", name: "项目", can_publish: true },
  { uri: "viking://projects/project-1/", parent_uri: "viking://projects/", kind: "directory", name: "MES标准库", can_publish: true },
  { uri: "viking://projects/project-1/one.md", parent_uri: "viking://projects/project-1/", kind: "file", name: "项目文档一.md", can_publish: true },
  { uri: "viking://projects/project-1/two.md", parent_uri: "viking://projects/project-1/", kind: "file", name: "项目文档二.md", can_publish: true },
  { uri: "viking://projects/project-2/", parent_uri: "viking://projects/", kind: "directory", name: "财务平台", can_publish: true },
  { uri: "viking://user/", kind: "directory", name: "人员" },
  { uri: owner, parent_uri: "viking://user/", kind: "directory", name: "我的工作区" },
  { uri: owner + "sessions/", parent_uri: owner, kind: "directory", name: "会话", can_publish: true }, session, another,
];
const errors = [];
page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const request = route.request(); const url = new URL(request.url());
  let body = [];
  if (url.pathname.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture", role: "admin" };
  if (url.pathname.endsWith("/sessions/list")) body = { sessions: [] };
  if (url.pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (url.pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (url.pathname.endsWith("/knowledge-resources/tree")) body = nodes;
  if (url.pathname.endsWith("/knowledge-resources/read")) {
    const node = nodes.find(item => item.uri === url.searchParams.get("uri")) ?? nodes[0];
    body = { node, children: nodes.filter(item => item.parent_uri === node.uri), source_text: node.kind === "file" ? "# 工序派工\n\n## 用户\n请帮我检查\n\n## AI\n这是一份原始回答。" : null, semantic_status: "not_applicable" };
  }
  if (url.pathname.endsWith("/knowledge-resources/task")) body = null;
  if (url.pathname.endsWith("/knowledge-agent/options")) body = { llm_models: [], skills: [] };
  if (url.pathname === "/api/v1/settings") body = { ui: { language: "zh-CN", theme: "light" }, catalog: { version: 1, services: Object.fromEntries(["llm", "embedding", "search", "tts", "stt", "imagegen", "videogen"].map(name => [name, { profiles: [], active_profile_id: null, active_model_id: null }])) }, providers: {} };
  if (url.pathname.endsWith("/codex-model-policy") || url.pathname.endsWith("/compiler-settings"))
    return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "测试未加载模型配置" }) });
  if (url.pathname.endsWith("/knowledge/team-git")) {
    if (request.method() === "PUT") { config = request.postDataJSON(); body = config; }
    else body = { root_path: "D:\\KnowledgeCenter\\storage\\knowledge_bases", workspace_path: "D:\\KnowledgeCenter\\workspaces\\team-knowledge", config, session_options: [session, another], status: { branch: "main", changes: [], ahead: failNextPush ? 1 : 0, behind: 0 } };
  }
  if (url.pathname.endsWith("/team-git/refresh")) {
    refreshCalls++;
    assert.deepEqual(request.postDataJSON(), { session_uris: expectedUris, force: expectedForce, ...expectedResourceUris === undefined ? {} : { resource_uris: expectedResourceUris } });
    body = { snapshot_sha256: "fixture-snapshot", is_valid: expectedForce, files: ["?? projects/订单系统/需求.md", ...expectedUris.length ? ["?? sessions/fixture/one.md"] : []], checks: [{ name: "remote", passed: true, message: "远端检查成功" }, { name: "history", passed: expectedForce, message: expectedForce ? "本地优先：允许覆盖远端领先或分叉的分支" : "远端领先，请选择本地优先同步" }] };
  }
  if (url.pathname.endsWith("/team-git/push")) {
    assert.deepEqual(request.postDataJSON(), { message: "更新团队资料", snapshot: "fixture-snapshot", force: expectedForce });
    if (failNextPush) { failNextPush = false; body = { ok: false, output: "模拟网络失败，本地提交已保留，请刷新后重试。" }; }
    else { pushed = true; body = { ok: true, commit_sha: "abc1234", output: "main -> main" }; }
  }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});
try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/settings/knowledge#team-git`);
  const section = page.locator("#team-git");
  await section.getByText("团队知识 Git", { exact: true }).waitFor();
  assert.equal(await section.getByRole("checkbox", { name: "本地优先，强制同步远端", exact: true }).isChecked(), true);
  const refresh = section.getByRole("button", { name: "刷新资料并检查远端" });
  assert.equal(await refresh.isDisabled(), true);
  await section.getByLabel("远程仓库 HTTPS 地址").fill("https://example.invalid/team.git");
  await section.getByRole("button", { name: "保存远程配置" }).click();
  await section.getByText("团队仓库配置已保存。", { exact: true }).waitFor();
  await refresh.click();
  await section.getByText("?? projects/订单系统/需求.md", { exact: true }).waitFor();
  const push = section.getByRole("button", { name: "确认提交并推送" });
  assert.equal(await push.isDisabled(), true);
  await section.getByLabel("提交说明").fill("更新团队资料");
  await section.getByRole("checkbox", { name: "已核对文件列表，确认提交到团队仓库" }).check();
  await push.click();
  await section.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.equal(pushed, true);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(200);
  const overflow = await page.evaluate(() => [...document.querySelectorAll("#team-git, #team-git *")].filter(element => element.getBoundingClientRect().right > window.innerWidth + 1).map(element => ({ tag: element.tagName, class: element.className })));
  assert.deepEqual(overflow, []);
  await page.setViewportSize({ width: 1400, height: 1000 });
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  const tree = page.locator("main nav");
  await tree.getByRole("button", { name: "MES标准库", exact: true }).waitFor();
  await tree.getByRole("button", { name: "财务平台", exact: true }).waitFor();
  await tree.getByRole("checkbox", { name: "选择推送 MES标准库", exact: true }).check();
  await tree.getByRole("button", { name: "MES标准库", exact: true }).click();
  assert.equal(await tree.getByRole("checkbox", { name: "选择推送 项目文档一.md", exact: true }).isChecked(), true);
  await tree.getByRole("checkbox", { name: "选择推送 项目文档二.md", exact: true }).uncheck();
  assert.equal(await tree.getByRole("checkbox", { name: "选择推送 MES标准库", exact: true }).evaluate(element => element.indeterminate), true);
  await tree.getByRole("checkbox", { name: "选择推送 MES标准库", exact: true }).check();
  if (artifacts) { await mkdir(artifacts, { recursive: true }); await page.screenshot({ path: path.join(artifacts, "knowledge-git-tree-selection.png") }); }
  await page.getByRole("button", { name: "推送所选", exact: true }).click();
  let selectionDialog = page.getByRole("dialog", { name: "知识 Git 提交推送" });
  await selectionDialog.getByRole("checkbox", { name: "本地优先，强制同步远端", exact: true }).waitFor();
  expectedResourceUris = ["viking://projects/project-1/"];
  await selectionDialog.getByRole("button", { name: "刷新资料并检查远端" }).click();
  await selectionDialog.getByText("本地优先：允许覆盖远端领先或分叉的分支", { exact: false }).waitFor();
  await selectionDialog.getByRole("checkbox", { name: "本地优先，强制同步远端", exact: true }).uncheck();
  assert.equal(await selectionDialog.getByRole("button", { name: "确认提交并推送" }).count(), 0);
  expectedForce = false;
  await selectionDialog.getByRole("button", { name: "刷新资料并检查远端" }).click();
  await selectionDialog.getByRole("button", { name: "确认提交并推送" }).waitFor();
  assert.equal(await selectionDialog.getByRole("button", { name: "确认提交并推送" }).isDisabled(), true);
  expectedForce = true;
  await page.keyboard.press("Escape");
  await selectionDialog.waitFor({ state: "detached" });
  await page.getByRole("button", { name: "清空选择", exact: true }).click();
  await tree.getByRole("button", { name: "我的工作区", exact: true }).click();
  await tree.getByRole("button", { name: "会话", exact: true }).click();
  await tree.getByRole("button", { name: session.name, exact: true }).click();
  await page.getByText("这是一份原始回答。", { exact: true }).waitFor();
  const beforeOpen = refreshCalls;
  await page.getByRole("button", { name: "提交推送", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "知识 Git 提交推送" });
  await dialog.getByRole("checkbox", { name: `发布 ${session.name}`, exact: true }).waitFor();
  assert.equal(await dialog.getByRole("checkbox", { name: `发布 ${session.name}`, exact: true }).isChecked(), true);
  assert.equal(await dialog.getByRole("checkbox", { name: `发布 ${another.name}`, exact: true }).isChecked(), false);
  assert.equal(refreshCalls, beforeOpen, "opening the dialog does not export or push");
  expectedUris = [session.uri];
  expectedResourceUris = [];
  await dialog.getByRole("button", { name: "刷新资料并检查远端" }).click();
  await dialog.getByRole("checkbox", { name: "已核对文件列表，确认提交到团队仓库" }).waitFor();
  await dialog.getByRole("checkbox", { name: `发布 ${another.name}`, exact: true }).check();
  assert.equal(await dialog.getByRole("button", { name: "确认提交并推送" }).count(), 0, "changing selections invalidates the reviewed snapshot");
  await dialog.getByRole("checkbox", { name: `发布 ${another.name}`, exact: true }).uncheck();
  await dialog.getByRole("button", { name: "刷新资料并检查远端" }).click();
  await dialog.getByLabel("提交说明", { exact: true }).fill("更新团队资料");
  await dialog.getByRole("checkbox", { name: "已核对文件列表，确认提交到团队仓库" }).check();
  failNextPush = true;
  await dialog.getByRole("button", { name: "确认提交并推送" }).click();
  await dialog.getByRole("alert").filter({ hasText: "模拟网络失败" }).waitFor();
  assert.equal(await dialog.getByText("已推送到团队仓库 · abc1234", { exact: true }).count(), 0);
  await dialog.getByRole("button", { name: "刷新资料并检查远端" }).click();
  await dialog.getByRole("checkbox", { name: "已核对文件列表，确认提交到团队仓库" }).check();
  await dialog.getByRole("button", { name: "确认提交并推送" }).click();
  await dialog.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  await dialog.getByLabel("Git 命令输出").filter({ hasText: "main -> main" }).waitFor();
  if (artifacts) { await mkdir(artifacts, { recursive: true }); await page.screenshot({ path: path.join(artifacts, "knowledge-git-desktop.png") }); }
  await page.keyboard.press("Escape");
  await dialog.waitFor({ state: "detached" });
  assert.equal(await page.getByRole("button", { name: "提交推送", exact: true }).evaluate(element => element === document.activeElement), true);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Git 提交推送", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "知识 Git 提交推送" });
  await dialog.getByRole("checkbox", { name: `发布 ${session.name}`, exact: true }).waitFor();
  const bounds = await dialog.evaluate(element => ({ left: element.getBoundingClientRect().left, right: element.getBoundingClientRect().right, width: window.innerWidth, overflow: element.scrollWidth > element.clientWidth + 1 }));
  assert.equal(bounds.left >= 0 && bounds.right <= bounds.width && !bounds.overflow, true, JSON.stringify(bounds));
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "knowledge-git-mobile.png") });
  assert.deepEqual(errors, []);
  console.log("Team Git: page tree selection/partial deselection, default local-first force, normal-mode blocking, mode/selection invalidation, session selection, failure/retry, output, Escape/focus and desktop/mobile layout passed.");
} finally { await browser.close(); }
