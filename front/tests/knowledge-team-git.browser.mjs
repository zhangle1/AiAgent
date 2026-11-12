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
let pushFailure = "";
let refreshMode = "valid";
let holdRefresh = false;
let releaseRefresh;
let failState = false;
const calls = [];
let refreshCalls = 0;

let gitFiles = [" M manifest.json", "?? projects/MES标准库/新增文档.md", "R  resources/新名称.md", "resources/旧名称.md", " D resources/已删除.md"];
let committedOnly = false;

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
    else if (failState) return route.fulfill({ status: 503, json: { message: "模拟配置读取失败" } });
    else body = { root_path: "D:\\KnowledgeCenter\\storage\\knowledge_bases", workspace_path: "D:\\KnowledgeCenter\\workspaces\\team-knowledge", config, session_options: [session, another], status: { branch: "main", changes: [], ahead: 0, behind: 3 } };
  }
  if (url.pathname.endsWith("/team-git/refresh")) {
    refreshCalls++; calls.push("refresh");
    assert.deepEqual(request.postDataJSON(), { session_uris: [], include_own_sessions: true, force: true });
    if (holdRefresh) await new Promise(resolve => { releaseRefresh = resolve; });
    if (refreshMode === "failed") return route.fulfill({ status: 503, json: { message: "模拟资料刷新失败" } });
    body = { snapshot_sha256: `snapshot-${refreshCalls}`, is_valid: refreshMode === "valid", files: refreshMode === "unchanged" || committedOnly ? [] : gitFiles, checks: [
      { name: "remote", passed: refreshMode !== "blocked", message: refreshMode === "blocked" ? "模拟远端检查失败" : "远端检查成功" },
      { name: "changes", passed: refreshMode !== "unchanged", message: "资料是否有更新" },
    ] };
  }
  if (url.pathname.endsWith("/team-git/push")) {
    calls.push("push");
    assert.deepEqual(request.postDataJSON(), { message: "同步服务器知识资料", snapshot: `snapshot-${refreshCalls}`, force: true });
    if (pushFailure === "stale") return route.fulfill({ status: 409, json: { message: "远端版本已变化，请重新检查并确认推送。" } });
    if (pushFailure) body = { ok: false, output: "模拟推送失败，服务器提交已保留。" };
    else { pushed = true; body = { ok: true, commit_sha: "abc1234", output: "main -> main", status: { changes: [], ahead: 0, behind: 0 } }; }
  }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});
try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/settings/knowledge#team-git`);
  const section = page.locator("#team-git");
  await section.getByText("团队知识 Git", { exact: true }).waitFor();
  const settingsPush = section.getByRole("button", { name: "强制推送", exact: true });
  await settingsPush.waitFor(); assert.equal(await settingsPush.isDisabled(), true);
  assert.equal(calls.length, 0);
  await section.getByLabel("远程仓库 HTTPS 地址").fill("https://example.invalid/team.git");
  await section.getByRole("button", { name: "保存远程配置" }).click();
  await section.getByText("团队仓库配置已保存。", { exact: true }).waitFor();
  assert.equal(await section.getByRole("checkbox").count(), 0);
  assert.equal(await section.getByLabel("提交说明", { exact: true }).count(), 0);
  await settingsPush.click();
  await section.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.deepEqual(calls, ["refresh", "push"]); assert.equal(pushed, true);
  await section.getByLabel("分支", { exact: true }).fill("unsaved");
  assert.equal(await section.getByRole("button", { name: "强制推送", exact: true }).isDisabled(), true);
  await section.getByLabel("分支", { exact: true }).fill("main");
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(200);
  const settingsOverflow = await section.evaluate(element => element.scrollWidth > element.clientWidth + 1);
  if (settingsOverflow && artifacts) { await mkdir(artifacts, { recursive: true }); await page.screenshot({ path: path.join(artifacts, "settings-overflow.png") }); }
  assert.equal(settingsOverflow, false);
  await page.setViewportSize({ width: 1400, height: 1000 });
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  const tree = page.locator("main nav");
  await tree.getByRole("button", { name: "MES标准库", exact: true }).waitFor();
  await tree.getByRole("button", { name: "财务平台", exact: true }).waitFor();
  assert.equal(await tree.getByRole("checkbox").count(), 0, "directory has no publishing checkboxes");
  assert.equal(await page.getByRole("button", { name: "推送所选", exact: true }).count(), 0);
  const trigger = page.getByRole("button", { name: "知识 Git 提交推送", exact: true });
  let dialog = page.getByRole("dialog", { name: "知识 Git 提交推送" });
  const push = () => dialog.getByRole("button", { name: /^(强制推送|重试强制推送)$/ });
  const detect = () => dialog.getByRole("button", { name: "重新检测 Git 变更" });
  const ready = () => dialog.getByText("待提交 4 个文件", { exact: true }).waitFor();
  let before = calls.length;
  await trigger.click(); await ready();
  assert.deepEqual(calls.slice(before), ["refresh"], "open automatically exports and detects, never pushes");
  assert.equal(await trigger.getAttribute("aria-expanded"), "true");
  assert.equal(await dialog.getByRole("checkbox").count(), 0);
  assert.equal(await dialog.getByRole("textbox").count(), 0);
  assert.equal(await dialog.evaluate(element => element.tagName), "DIV", "dropdown replaces modal dialog");
  const desktop = await dialog.boundingBox(); assert.equal(desktop.x > 900 && desktop.y < 100, true);
  await dialog.getByText("查看变更文件", { exact: true }).click();
  const list = dialog.getByRole("list", { name: "待提交文件" });
  assert.equal(await list.getByRole("listitem").count(), 4, "rename source counts once");
  await list.getByText("resources/旧名称.md → resources/新名称.md", { exact: true }).waitFor();
  if (artifacts) { await mkdir(artifacts, { recursive: true }); await page.screenshot({ path: path.join(artifacts, "knowledge-git-dropdown-desktop.png") }); }
  await page.keyboard.press("Escape"); await dialog.waitFor({ state: "detached" });
  assert.equal(await trigger.evaluate(element => element === document.activeElement), true);
  // Reopening must perform exactly one new detection, without reusing the old panel state.
  before = calls.length; await trigger.click(); await ready();
  assert.deepEqual(calls.slice(before), ["refresh"]);
  before = calls.length; holdRefresh = true;
  await push().evaluate(element => { element.click(); element.click(); });
  await dialog.getByRole("button", { name: "正在检测变更…", exact: true }).waitFor();
  assert.equal(calls.length, before + 1, "double click starts one detection");
  await page.keyboard.press("Escape"); assert.equal(await dialog.isVisible(), true);
  assert.equal(await dialog.getByRole("button", { name: "关闭知识 Git 面板", exact: true }).isDisabled(), true);
  await tree.getByRole("button", { name: "MES标准库", exact: true }).click();
  assert.equal(await dialog.isVisible(), true, "outside click cannot close an active operation");
  for (let i = 0; i < 50 && !releaseRefresh; i++) await page.waitForTimeout(20);
  assert.equal(typeof releaseRefresh, "function"); holdRefresh = false; releaseRefresh();
  await dialog.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh", "push"]);
  await dialog.getByText("待提交 0 个文件", { exact: true }).waitFor();
  assert.equal(await push().isDisabled(), true);
  await dialog.getByText("查看推送日志", { exact: true }).click();
  await dialog.getByLabel("Git 命令输出").filter({ hasText: "main -> main" }).waitFor();
  assert.match(await trigger.innerText(), /Git\s*0/);
  // New sources can be detected without reopening the dropdown.
  before = calls.length; await detect().click(); await ready();
  assert.deepEqual(calls.slice(before), ["refresh"]);
  committedOnly = true; await detect().click();
  await dialog.getByText("有待推送提交或远端差异，可强制同步。", { exact: true }).waitFor();
  assert.equal(await push().isEnabled(), true, "zero uncommitted files may still require a push");
  before = calls.length; await push().click();
  await dialog.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh", "push"]); committedOnly = false;
  refreshMode = "unchanged"; before = calls.length; await detect().click();
  await dialog.getByText("服务器内容已同步，无需推送。", { exact: true }).waitFor();
  assert.equal(await push().isDisabled(), true); assert.deepEqual(calls.slice(before), ["refresh"]);
  refreshMode = "blocked"; before = calls.length; await detect().click();
  await dialog.getByRole("alert").filter({ hasText: "模拟远端检查失败" }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh"]);
  before = calls.length; await push().click();
  await dialog.getByRole("alert").filter({ hasText: "模拟远端检查失败" }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh"], "failed validation cannot push");
  refreshMode = "failed"; before = calls.length; await push().click();
  await dialog.getByRole("alert").filter({ hasText: "模拟资料刷新失败" }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh"]);
  assert.equal(await dialog.getByText("待提交 4 个文件", { exact: true }).count(), 0, "failed refresh clears stale counts");
  refreshMode = "valid"; pushFailure = "failed"; before = calls.length; await push().click();
  await dialog.getByRole("alert").filter({ hasText: "服务器提交已保留" }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh", "push"]);
  pushFailure = "stale"; before = calls.length; await push().click();
  await dialog.getByRole("alert").filter({ hasText: "远端版本已变化" }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh", "push"]);
  pushFailure = ""; before = calls.length; await push().click();
  await dialog.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh", "push"], "retry refreshes the snapshot before pushing");
  await page.keyboard.press("Escape"); await dialog.waitFor({ state: "detached" });
  await trigger.click(); await ready();
  await tree.getByRole("button", { name: "财务平台", exact: true }).click();
  await dialog.waitFor({ state: "detached" });
  // Both own sessions are always part of automatic detection, without a per-file publish action.
  await tree.getByRole("button", { name: "我的工作区", exact: true }).click();
  await tree.getByRole("button", { name: "会话", exact: true }).click();
  await tree.getByRole("button", { name: session.name, exact: true }).click();
  await page.getByText("这是一份原始回答。", { exact: true }).waitFor();
  gitFiles = ["?? sessions/fixture/MES标准库-1/one.md", "?? sessions/fixture/财务平台-2/two.md", " M manifest.json"];
  before = calls.length; await trigger.click();
  await dialog.getByText("待提交 3 个文件", { exact: true }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh"]);
  assert.equal(await dialog.getByRole("button", {name:/发布当前会话/}).count(), 0);
  await dialog.getByText("查看变更文件", {exact:true}).click();
  await dialog.getByText("sessions/fixture/MES标准库-1/one.md", {exact:true}).waitFor();
  await dialog.getByText("sessions/fixture/财务平台-2/two.md", {exact:true}).waitFor();
  if(artifacts) await dialog.screenshot({path:path.join(artifacts,"knowledge-git-session-projects.png")});
  before = calls.length; await push().click();
  await dialog.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.deepEqual(calls.slice(before), ["refresh", "push"]);
  await page.keyboard.press("Escape"); await dialog.waitFor({ state: "detached" });
  gitFiles = [" M manifest.json", "?? projects/MES标准库/新增文档.md", "R  resources/新名称.md", "resources/旧名称.md", " D resources/已删除.md"];
  await page.setViewportSize({ width: 390, height: 844 });
  await trigger.click(); await ready();
  await dialog.getByText("查看变更文件", { exact: true }).click();
  await page.waitForTimeout(200);
  const bounds = await dialog.evaluate(element => ({ left: element.getBoundingClientRect().left, right: element.getBoundingClientRect().right, width: window.innerWidth, bottom: element.getBoundingClientRect().bottom, height: window.innerHeight, overflow: element.scrollWidth > element.clientWidth + 1 }));
  assert.equal(bounds.left >= 0 && bounds.right <= bounds.width && bounds.bottom <= bounds.height && !bounds.overflow, true, JSON.stringify(bounds));
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "knowledge-git-dropdown-mobile.png") });
  await dialog.getByRole("button", { name: "关闭知识 Git 面板", exact: true }).click(); await dialog.waitFor({ state: "detached" });
  // Errors can be recovered from within the dropdown.
  failState = true; await trigger.click();
  await dialog.getByRole("alert").filter({ hasText: "模拟配置读取失败" }).waitFor();
  assert.equal(await push().count(), 0);
  failState = false; await dialog.getByRole("button", { name: "重新加载" }).click(); await ready();
  assert.deepEqual(errors, []);
  console.log("Team Git passed: automatic Git detection in dropdown, full shared scope without tree checkboxes, pending counts and rename/delete/new paths, no push on open, reopen/manual recheck, post-push reset, zero-file pending commits, no-change guard, double-click and busy-close guard, validation/refresh/push failures and fresh snapshot retry, automatic own-session scope and two-session pending-file regression, logs, focus and desktop/mobile layout.");
} finally { await browser.close(); }
