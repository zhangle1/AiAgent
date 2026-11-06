// Uses the running frontend with synthetic APIs; no real knowledge records are accessed.
// KNOWLEDGE_TEST_TOOLS=<directory with playwright> node tests/knowledge-cleanup.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";

const require = createRequire(import.meta.url);
const tools = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const browser = await chromium.launch({ headless: true, channel: process.env.KNOWLEDGE_TEST_BROWSER_CHANNEL ?? "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const owner = "viking://user/ui-fixture/";
const folder = owner + "sessions/";
const old = { uri: folder + "old.md", parent_uri: folder, name: "old.md", kind: "file", document_id: 11, extension: ".md", source_status: "outside_root", source_message: "历史记录指向当前存储目录之外，可清理后重新导入。" };
const valid = { uri: folder + "valid.md", parent_uri: folder, name: "valid.md", kind: "file", document_id: 12, extension: ".md", source_status: "available" };
const directory = uri => ({ uri, name: uri === owner ? "我的工作区" : "会话", parent_uri: uri === owner ? "viking://user/" : owner, kind: "directory" });
let removed = false;
let cleanupFailureStatus = 404;
let failScan = false;
let empty = false;
let cleanupCalls = 0;
let treeCalls = 0;
const errors = [];
page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const request = route.request();
  const url = new URL(request.url());
  let body = [];
  if (url.pathname.endsWith("/auth/status")) body = { authenticated: true, username: "ui-fixture", user_id: "fixture", role: "user" };
  if (url.pathname.endsWith("/sessions/list")) body = { sessions: [] };
  if (url.pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (url.pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (url.pathname.endsWith("/knowledge-agent/options")) body = { llm_models: [], skills: [] };
  const nodes = [{ uri: "viking://resources/", kind: "directory", name: "资料" }, directory(owner), directory(folder), valid, ...removed ? [] : [old]];
  if (url.pathname.endsWith("/knowledge-resources/tree")) {
    treeCalls++;
    // Source availability is checked by the read endpoint, not by the metadata-only tree.
    body = nodes.map(({ source_status, source_message, ...node }) => node);
  }
  if (url.pathname.endsWith("/knowledge-resources/read")) body = { node: nodes.find(node => node.uri === url.searchParams.get("uri")) ?? nodes[0], children: [], semantic_status: "not_applicable" };
  if (url.pathname.endsWith("/knowledge-resources/task")) body = null;
  if (url.pathname.endsWith("/cleanup/preview")) {
    if (failScan) return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "模拟扫描失败" }) });
    body = { snapshot_sha256: "fixture-snapshot", includes_shared: false, items: empty ? [] : [
      ...removed ? [] : [{ document_id: 11, name: "old.md", uri: old.uri, reason: "outside_root", message: old.source_message, can_cleanup: true }],
      { document_id: 13, name: "busy.md", uri: folder + "busy.md", reason: "missing", message: "原文件已不存在", can_cleanup: false, blocked_reason: "存在活动处理任务，请先取消任务并等待结束。" },
    ] };
  }
  if (url.pathname.endsWith("/knowledge-resources/cleanup") && request.method() === "POST") {
    cleanupCalls++;
    assert.deepEqual(request.postDataJSON(), { document_ids: [11], snapshot_sha256: "fixture-snapshot" });
    if (cleanupFailureStatus === 404) return route.fulfill({ status: 404, body: "" });
    if (cleanupFailureStatus === 409) return route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify({ message: "资料或任务状态已变化，请重新扫描并确认清理列表。" }) });
    removed = true; body = { removed_count: 1 };
  }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});

try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  const trigger = page.getByRole("button", { name: "清理失效记录", exact: true });
  const tree = page.locator("main nav");
  await trigger.waitFor();
  await tree.getByRole("button", { name: "我的工作区", exact: true }).click();
  await tree.getByRole("button", { name: "会话", exact: true }).click();
  await tree.getByRole("button", { name: "old.md", exact: true }).click();
  await page.getByText("原文件不可用", { exact: true }).waitFor();
  assert.equal(await page.getByRole("button", { name: "重新解析", exact: true }).isDisabled(), true);
  assert.equal(await page.getByRole("link", { name: "下载", exact: true }).count(), 0);
  await trigger.click();
  let dialog = page.getByRole("dialog", { name: "清理失效记录", exact: true });
  await dialog.getByText("old.md", { exact: true }).waitFor();
  const submit = () => dialog.getByRole("button", { name: "确认清理", exact: true });
  assert.equal(await submit().isDisabled(), true);
  assert.equal(await dialog.getByRole("checkbox", { name: "清理 busy.md", exact: true }).isDisabled(), true);
  assert.equal(cleanupCalls, 0);
  await dialog.getByRole("checkbox", { name: "清理 old.md", exact: true }).check();
  assert.equal(await submit().isDisabled(), true);
  await dialog.getByRole("checkbox", { name: "确认移除所选失效记录", exact: true }).check();
  await submit().click();
  await dialog.getByRole("alert").filter({ hasText: "清理接口不可用（HTTP 404）" }).waitFor({ timeout: 3000 });
  assert.equal(removed, false);
  assert.equal(await submit().isDisabled(), true);
  assert.equal(await dialog.getByRole("button", { name: "重新扫描" }).isDisabled(), false);
  cleanupFailureStatus = 409;
  await dialog.getByRole("button", { name: "重新扫描" }).click();
  await page.waitForFunction(() => {
    const checkbox = document.querySelector('dialog input[aria-label="清理 old.md"]');
    return checkbox && !checkbox.checked && !checkbox.disabled;
  });
  await dialog.getByRole("checkbox", { name: "清理 old.md", exact: true }).check();
  await dialog.getByRole("checkbox", { name: "确认移除所选失效记录", exact: true }).check();
  await submit().click();
  await dialog.getByRole("alert").filter({ hasText: "状态已变化" }).waitFor();
  assert.equal(removed, false);
  assert.equal(await submit().isDisabled(), true);
  cleanupFailureStatus = 0;
  await dialog.getByRole("button", { name: "重新扫描" }).click();
  await page.waitForFunction(() => {
    const checkbox = document.querySelector('dialog input[aria-label="清理 old.md"]');
    return checkbox && !checkbox.checked && !checkbox.disabled;
  });
  assert.equal(await dialog.getByRole("checkbox", { name: "清理 old.md", exact: true }).isChecked(), false);
  await page.keyboard.press("Escape");
  await dialog.waitFor({ state: "hidden" });
  assert.equal(cleanupCalls, 2);
  assert.equal(await trigger.evaluate(element => document.activeElement === element), true);

  await page.setViewportSize({ width: 390, height: 844 });
  await trigger.click();
  dialog = page.getByRole("dialog", { name: "清理失效记录", exact: true });
  await dialog.getByText("old.md", { exact: true }).waitFor();
  const overflow = await dialog.evaluate(element => ({ width: element.clientWidth, scroll: element.scrollWidth, right: element.getBoundingClientRect().right, viewport: innerWidth }));
  assert.ok(overflow.scroll <= overflow.width + 1 && overflow.right <= overflow.viewport);
  if (process.env.KNOWLEDGE_CLEANUP_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_CLEANUP_SCREENSHOT });
  await dialog.getByRole("checkbox", { name: "选择全部可清理记录" }).check();
  assert.equal(await dialog.getByRole("checkbox", { name: "清理 busy.md", exact: true }).isChecked(), false);
  await dialog.getByRole("checkbox", { name: "确认移除所选失效记录" }).check();
  const beforeRefresh = treeCalls;
  await submit().click();
  await dialog.waitFor({ state: "hidden" });
  await page.getByText("已清理 1 条失效记录，磁盘文件和聊天历史已保留。", { exact: true }).waitFor();
  await tree.getByRole("button", { name: "old.md", exact: true }).waitFor({ state: "hidden" });
  assert.equal(removed, true);
  assert.ok(treeCalls > beforeRefresh);
  assert.equal(await tree.getByRole("button", { name: "valid.md", exact: true }).count(), 1);

  empty = true;
  await trigger.click();
  dialog = page.getByRole("dialog", { name: "清理失效记录", exact: true });
  await dialog.getByText("没有需要清理的失效记录").waitFor();
  assert.equal(await submit().isDisabled(), true);
  failScan = true;
  await dialog.getByRole("button", { name: "重新扫描" }).click();
  await dialog.getByRole("alert").filter({ hasText: "模拟扫描失败" }).waitFor();
  assert.equal(await submit().isDisabled(), true);
  await page.keyboard.press("Escape");
  assert.deepEqual(errors, []);
  assert.equal(cleanupCalls, 3);
  console.log("PASS: unavailable source, confirmation, busy-file protection, HTTP 404 recovery, stale snapshot rejection, retry, Escape/focus, cleanup refresh, empty/error states and 390px layout.");
} finally { await browser.close(); }
