// Run against an isolated frontend. All API responses use synthetic fixtures.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
const require = createRequire(import.meta.url);
const browserTools = process.env.AIAGENT_BROWSER_TOOLS ? createRequire(path.resolve(process.env.AIAGENT_BROWSER_TOOLS, "package.json")) : require;
const { chromium } = browserTools("playwright");
const base = process.env.ATLAS_TEST_URL || "http://127.0.0.1:4393";
const projects = [1, 2].map(id => ({ id, name: `project-${id}`, display_name: `项目 ${id}`, repositories: [{ id, name: `repo-${id}`, display_name: `仓库 ${id}` }] }));
const snapshot = id => ({ id: `snapshot-${id}`, project_id: id, created_at: "2026-11-19T12:00:00Z", start_at: "2026-11-05T16:00:00Z", repositories: [{ id, name: id === 1 ? "cps-web" : "repo-2" }, { id: id + 20, name: "cps-api" }], modules: [], warnings: [], files: [{ id: "f1", repository_id: id, path: "src/库存.ts" }], commits: [
  { id: "c1", repository_id: id, sha: "1".repeat(40), author_id: "a", author: "甲", committed_at: "2026-11-17T17:00:00Z", subject: `修复库存 ${id}`, files: [{ file_id: "f1", added: 2, deleted: 1 }] },
  { id: "c2", repository_id: id, sha: "2".repeat(40), author_id: "a", author: "甲", committed_at: "2026-11-18T12:00:00Z", subject: "保留校验", files: [{ file_id: "f1", added: 1, deleted: 0 }] },
  { id: "c3", repository_id: id + 20, sha: "3".repeat(40), author_id: "a", author: "甲", committed_at: "2026-11-18T17:00:00Z", subject: "补充接口", files: [] },
  { id: "c4", repository_id: id, sha: "4".repeat(40), author_id: "b", author: "乙", committed_at: "2026-11-16T08:00:00Z", subject: "更新说明", files: [] },
] });
const browser = await chromium.launch({ headless: true, channel: process.env.ATLAS_TEST_BROWSER_CHANNEL || "chrome" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: base });
const errors = []; page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
let failSnapshot = false;
let historyRequests = 0;
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
    if (route.request().method() === "POST") {
      historyRequests++;
      assert.deepEqual(route.request().postDataJSON(), { days: 14, ref: "HEAD", refresh_remote: false, history_only: true });
    }
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
  const dialog = page.getByRole("dialog", { name: "协作 · 提交时间轴" });
  const timeline = dialog.getByRole("table", { name: "人员与仓库提交时间轴" });
  await timeline.waitFor();
  const initialHistoryRequests = historyRequests;
  assert.ok((await dialog.boundingBox()).width > 1200);
  assert.equal(await timeline.getByRole("columnheader").count(), 15);
  assert.equal(await timeline.getByRole("rowheader").count(), 3);
  assert.equal(await dialog.getByLabel("历史范围").inputValue(), "14");
  assert.ok(!(await dialog.innerText()).includes("UTC"));
  if (process.env.ATLAS_DESKTOP_SCREENSHOT) await dialog.screenshot({ path: process.env.ATLAS_DESKTOP_SCREENSHOT });
  await dialog.getByLabel("人员（Git 署名）").selectOption("a");
  assert.equal(await timeline.getByRole("rowheader").count(), 2);
  await dialog.getByLabel("开始日期").fill("2026-11-18");
  await dialog.getByLabel("结束日期").fill("2026-11-18");
  await timeline.getByRole("button", { name: "甲 · cps-web · 2026-11-18 · 2 次提交" }).click();
  assert.equal(await dialog.getByRole("checkbox").count(), 2);
  await dialog.getByRole("checkbox", { name: /^修复库存 1/ }).check();
  await dialog.getByLabel("结束日期").fill("2026-11-19");
  await timeline.getByRole("button", { name: "甲 · cps-api · 2026-11-19 · 1 次提交" }).click();
  await dialog.getByRole("checkbox", { name: /^补充接口/ }).check();
  await dialog.getByLabel("搜索提交").fill("missing");
  await dialog.getByText("当前筛选没有提交，可调整日期、人员或搜索条件。").waitFor();
  await dialog.getByText("已选 2/20（2 项已隐藏）").waitFor();
  await dialog.getByLabel("搜索提交").fill("库存");
  assert.equal(historyRequests, initialHistoryRequests, "Local filtering should not reload Git history");
  await dialog.getByRole("button", { name: "复制所选", exact: true }).click();
  const copied = await page.evaluate(() => navigator.clipboard.readText());
  assert.ok(copied.includes("这段改了什么") && copied.includes("/change-atlas/review?") && copied.includes("cps-web") && copied.includes("cps-api"));
  assert.ok(!copied.includes("保留校验"));
  await dialog.getByRole("button", { name: "插入输入框" }).click();
  await page.waitForFunction(() => document.activeElement?.matches('[contenteditable="true"]'));
  await page.getByRole("button", { name: "添加到聊天", exact: true }).filter({ visible: true }).click();
  await page.getByRole("menuitem", { name: /^协作/ }).click();
  await dialog.getByLabel(/^项目/).selectOption("2");
  await timeline.getByRole("button", { name: "甲 · repo-2 · 2026-11-18 · 2 次提交" }).waitFor();
  await page.keyboard.press("Escape");
  assert.equal(await dialog.count(), 0);
  assert.ok((await page.locator('[contenteditable="true"]').allTextContents()).some(text => text.startsWith("保留原有草稿") && text.includes("这段改了什么") && text.includes("/change-atlas/review?")));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "添加到聊天", exact: true }).filter({ visible: true }).click();
  await page.getByRole("menuitem", { name: /^协作/ }).click();
  await timeline.getByRole("button", { name: "甲 · cps-web · 2026-11-18 · 2 次提交" }).waitFor();
  const bounds = await dialog.boundingBox();
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390 && bounds.height <= 844);
  assert.ok(await timeline.evaluate(element => element.parentElement.scrollWidth > element.parentElement.clientWidth));
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  if (process.env.ATLAS_SCREENSHOT) await dialog.screenshot({ path: process.env.ATLAS_SCREENSHOT });
  await page.keyboard.press("Escape");
  await page.goto(base + "/change-atlas/review?project=1&snapshot=snapshot-1&commit=c1&file=f1");
  await page.locator("pre").getByText("+safe <img src=x onerror=alert(1)>", { exact: true }).waitFor();
  assert.equal(await page.locator("pre img").count(), 0);
  await page.getByText("差异已截断，不能视为完整审阅。").waitFor();
  await page.getByText("2026-11-18 01:00", { exact: false }).waitFor();
  failSnapshot = true;
  await page.reload(); await page.getByRole("alert").getByText("无权访问该项目或地图。").waitFor();
  assert.deepEqual(errors, []);
  console.log("PASS: 14-day Beijing timeline, person/repository lanes, commit selection, prompt, project switch, narrow viewport and protected review");
} finally { await browser.close(); }
