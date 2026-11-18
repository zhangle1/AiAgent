// Run against an isolated frontend. All API responses use synthetic fixtures.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
const require = createRequire(import.meta.url);
const browserTools = process.env.AIAGENT_BROWSER_TOOLS ? createRequire(path.resolve(process.env.AIAGENT_BROWSER_TOOLS, "package.json")) : require;
const { chromium } = browserTools("playwright");
const base = process.env.ATLAS_TEST_URL || "http://127.0.0.1:4393";
const projects = [1, 2].map(id => ({ id, name: `project-${id}`, display_name: `项目 ${id}`, repositories: [{ id, name: `repo-${id}`, display_name: `仓库 ${id}` }] }));
const snapshot = id => ({ id: `snapshot-${id}`, project_id: id, created_at: "2026-11-18T12:00:00Z", start_at: "2026-11-12T00:00:00Z", repositories: [{ id, name: `repo-${id}` }], modules: [], warnings: [], files: [{ id: "f1", repository_id: id, path: "src/库存.ts" }], commits: [
  { id: "c1", repository_id: id, sha: "1".repeat(40), author_id: "a", author: "甲", committed_at: "2026-11-17T12:00:00Z", subject: `修复库存 ${id}`, files: [{ file_id: "f1", added: 2, deleted: 1 }] },
  { id: "c2", repository_id: id, sha: "2".repeat(40), author_id: "b", author: "乙", committed_at: "2026-11-18T12:00:00Z", subject: "保留校验", files: [{ file_id: "f1", added: 1, deleted: 0 }] },
] });
const browser = await chromium.launch({ headless: true, channel: process.env.ATLAS_TEST_BROWSER_CHANNEL || "chrome" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: base });
const errors = []; page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
let failSnapshot = false;
await page.route("**/api/**", async route => {
  const pathname = new URL(route.request().url()).pathname;
  let value = [];
  if (pathname.endsWith("/auth/status")) value = { authenticated: true, username: "fixture", user_id: "fixture", is_admin: false };
  else if (pathname.endsWith("/code-repositories/projects")) value = projects;
  else if (pathname.endsWith("/sessions/list")) value = { sessions: [] };
  else if (pathname.endsWith("/project-preferences")) value = { preferences: [] };
  else if (pathname.endsWith("/sidebar-preference")) value = { project_sort_mode: "recent" };
  else if (pathname.endsWith("/settings")) value = { ui: { preferred_agent: "none" }, catalog: { services: { llm: { profiles: [] } } } };
  else if (pathname.endsWith("/codex-model-policy")) value = { models: [], allowed_model_ids: [], allowed_reasoning_efforts: [] };
  else if (pathname.includes("/change-atlas/snapshots")) {
    if (failSnapshot) { await route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ message: "无权访问该项目或地图。" }) }); return; }
    value = pathname.endsWith("/evidence") ? { path: "src/库存.ts", patch: "+safe <img src=x onerror=alert(1)>", truncated: true, suppressed: false } : snapshot(Number(pathname.split("/")[4]));
  }
  await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) });
});
try {
  await page.goto(base + "/chat");
  const composer = page.locator('[contenteditable="true"]').filter({ visible: true });
  await composer.fill("保留原有草稿");
  await page.getByRole("button", { name: "添加到聊天", exact: true }).filter({ visible: true }).click();
  await page.getByRole("menuitem", { name: /^协作/ }).click();
  const dialog = page.getByRole("dialog", { name: "协作 · Git 历史树" });
  await dialog.getByText("repo-1 · 2 条提交", { exact: true }).waitFor();
  await dialog.getByLabel("人员（Git 署名）").selectOption("a");
  assert.equal(await dialog.locator("summary").count(), 2);
  await dialog.getByLabel("开始日期（UTC）").fill("2026-11-18");
  await dialog.getByText("当前筛选没有提交，可调整日期、人员或搜索条件。").waitFor();
  await dialog.getByLabel("开始日期（UTC）").fill("2026-11-17");
  await dialog.getByLabel("Git 改动", { exact: true }).fill("missing");
  await dialog.getByText("当前筛选没有提交，可调整日期、人员或搜索条件。").waitFor();
  await dialog.getByLabel("Git 改动", { exact: true }).fill("库存");
  await dialog.locator("summary").nth(1).click();
  await dialog.getByLabel("选择提交", { exact: true }).check();
  await dialog.getByRole("button", { name: "复制所选", exact: true }).click();
  assert.ok((await page.evaluate(() => navigator.clipboard.readText())).includes("/change-atlas/review?"));
  await dialog.getByRole("button", { name: "插入输入框" }).click();
  await page.getByRole("button", { name: "添加到聊天", exact: true }).filter({ visible: true }).click();
  await page.getByRole("menuitem", { name: /^协作/ }).click();
  await dialog.getByLabel(/^项目/).selectOption("2");
  await dialog.getByText("repo-2 · 2 条提交", { exact: true }).waitFor();
  await page.keyboard.press("Escape");
  assert.equal(await dialog.count(), 0);
  assert.ok((await page.locator('[contenteditable="true"]').allTextContents()).some(text => text.startsWith("保留原有草稿") && text.includes("保留已有有效行为") && text.includes("/change-atlas/review?")));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "添加到聊天", exact: true }).filter({ visible: true }).click();
  await page.getByRole("menuitem", { name: /^协作/ }).click();
  await dialog.getByText("repo-1 · 2 条提交", { exact: true }).waitFor();
  const bounds = await dialog.boundingBox();
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390 && bounds.height <= 844);
  await page.keyboard.press("Escape");
  await page.goto(base + "/change-atlas/review?project=1&snapshot=snapshot-1&commit=c1&file=f1");
  await page.locator("pre").getByText("+safe <img src=x onerror=alert(1)>", { exact: true }).waitFor();
  assert.equal(await page.locator("pre img").count(), 0);
  await page.getByText("差异已截断，不能视为完整审阅。").waitFor();
  failSnapshot = true;
  await page.reload(); await page.getByRole("alert").getByText("无权访问该项目或地图。").waitFor();
  assert.deepEqual(errors, []);
  console.log("PASS: menu, history tree, filters, insertion, project switch, Escape, safe diff and denied access");
} finally { await browser.close(); }
