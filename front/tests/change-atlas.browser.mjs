// Start the built frontend, then set ATLAS_TEST_URL and optionally AIAGENT_BROWSER_TOOLS / ATLAS_TEST_BROWSER_CHANNEL.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";

const require = createRequire(import.meta.url);
const tools = process.env.AIAGENT_BROWSER_TOOLS ? createRequire(path.resolve(process.env.AIAGENT_BROWSER_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const url = process.env.ATLAS_TEST_URL || "http://127.0.0.1:4392";
const projects = [1, 2].map(id => ({ id, name: `project-${id}`, display_name: `测试项目 ${id}`, root_path: "", repository_count: 1, repositories: [{ id, project_id: id, name: `repo-${id}`, display_name: `测试代码库 ${id}`, root_path: "" }] }));
const files = [{ id: "material-file", repository_id: 1, module_id: "material", path: "Services/Material/换算.cs", at_head: true }, { id: "context-file", repository_id: 1, module_id: "material", path: "Services/Material/上下文.cs", at_head: true }, { id: "inventory-file", repository_id: 1, module_id: "inventory", path: "Services/Inventory/库存.cs", at_head: true }, { id: "label-file", repository_id: 1, module_id: "label", path: "Services/Label/标签.cs", at_head: true }];
const change = file_id => ({ file_id, added: 2, deleted: 1 });
const commits = [
  { id: "c3", repository_id: 1, sha: "3".repeat(40), parent_sha: "2".repeat(40), is_merge: false, author_id: "chen", author: "陈工", committed_at: "2026-11-02T02:00:00Z", subject: "标签客户码", files: [change("label-file")], truncated: false },
  { id: "c2", repository_id: 1, sha: "2".repeat(40), parent_sha: "1".repeat(40), is_merge: false, author_id: "lin", author: "林工", committed_at: "2026-11-01T02:00:00Z", subject: "补齐换算边界", files: [change("material-file")], truncated: false },
  { id: "c1", repository_id: 1, sha: "1".repeat(40), parent_sha: null, is_merge: false, author_id: "chen", author: "陈工", committed_at: "2026-10-31T02:00:00Z", subject: "修正替代料扣减", files: [change("material-file"), change("inventory-file")], truncated: false },
];
const browser = await chromium.launch({ headless: true, channel: process.env.ATLAS_TEST_BROWSER_CHANNEL || undefined });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const errors = [], external = [];
page.on("pageerror", error => errors.push(error.message));
page.on("request", request => { if (/^https?:/.test(request.url()) && !request.url().startsWith(url)) external.push(request.url()); });
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
let counter = 0, lastAnalysis, lastEvidence, delayAnalysis = false, delayEvidence = false;
const answer = data => ({ status: 200, contentType: "application/json", body: JSON.stringify(data) });
await page.route("**/api/**", async route => {
  const request = route.request(), pathname = new URL(request.url()).pathname;
  let value = {};
  if (pathname.endsWith("/auth/status")) value = { authenticated: true, username: "fixture", user_id: "fixture", is_admin: true };
  else if (pathname === "/api/v1/code-repositories/projects") value = projects;
  else if (pathname.endsWith("/sessions/list")) value = { sessions: [] };
  else if (pathname.endsWith("/project-preferences")) value = { preferences: [] };
  else if (pathname.endsWith("/sidebar-preference")) value = { project_sort_mode: "recent" };
  else if (pathname.endsWith("/codex-model-policy")) value = { models: [{ id: "fixture-model", name: "测试模型", supports_reasoning_effort: true, reasoning_efforts: ["high"] }], allowed_model_ids: ["fixture-model"], default_model_id: "fixture-model", allowed_reasoning_efforts: ["high"], default_reasoning_effort: "high", allow_chat_model_override: true, allow_chat_reasoning_effort_override: true };
  else if (pathname.endsWith("/change-atlas/snapshots")) {
    const body = request.postDataJSON(); counter++;
    const id = Number(pathname.split('/')[4]);
    value = { id: counter.toString(16).padStart(32, '0'), project_id: id, days: body.days, created_at: "2026-11-02T12:00:00Z", start_at: new Date(Date.parse("2026-11-02T00:00:00Z") - (body.days - 1) * 86400000).toISOString(), coverage: "directory", warnings: [], repositories: [{ id, name: `测试代码库 ${id}`, head_sha: "3".repeat(40), ref: "main", source: body.refresh_remote ? "remote" : "local", refs: ["refs/heads/main"] }], files, modules: [{ id: "material", repository_id: id, name: "Material", directory: "Services/Material" }, { id: "inventory", repository_id: id, name: "Inventory", directory: "Services/Inventory" }, { id: "label", repository_id: id, name: "Label", directory: "Services/Label" }], commits: id === 2 ? [{ ...commits[1], subject: "其他项目改动" }] : commits };
  } else if (pathname.endsWith("/evidence")) {
    if (delayEvidence) { delayEvidence = false; await new Promise(resolve => setTimeout(resolve, 650)); }
    const params = new URL(request.url()).searchParams, file = files.find(file => file.id === params.get("file_id"));
    lastEvidence = { commit: params.get("commit_id"), file: file.id };
    value = { id: "e1", file_id: file.id, commit_id: params.get("commit_id"), path: file.path, sha: "2".repeat(40), patch: "-qty = required;\n+qty = required * ratio;\n+// <img src=x onerror=window.__atlasInjected=1>", truncated: false, suppressed: false };
  } else if (pathname.endsWith("/analysis/stream")) {
    lastAnalysis = request.postDataJSON(); const delayed = delayAnalysis; delayAnalysis = false;
    if (delayed) await new Promise(resolve => setTimeout(resolve, 650));
    value = { summary: delayed ? "迟到的旧模块解读" : "模型完成换算解读", module_label: "领料业务候选", claims: [{ kind: "inference", text: "建议核对单位换算", evidence_ids: ["e1"] }], checks: ["检查退料场景"], unknowns: ["未关联测试结果"], evidence: [{ id: "e1", file_id: "material-file", commit_id: "c2", path: files[0].path, sha: "2".repeat(40), patch: "+qty = required * ratio;", truncated: false, suppressed: false }], model: "fixture-model", created_at: "2026-11-02T12:00:00Z" };
  }
  await route.fulfill(pathname.endsWith("/analysis/stream") ? { status: 200, contentType: "text/event-stream", body: `event: status\ndata: {"message":"正在分析证据"}\n\nevent: result\ndata: ${JSON.stringify(value)}\n\n` } : answer(value)).catch(() => {});
});
try {
  await page.goto(url + "/change-atlas?project=1");
  await page.getByText("3 次已载入提交", { exact: true }).waitFor();
  assert.equal(await page.getByRole("link", { name: "变化地图", exact: true }).count(), 1);
  await page.getByRole("tab", { name: "代码结构", exact: true }).click();
  assert.equal(await page.locator("[data-atlas-file]").count(), 2);
  await page.getByLabel("文件目录树", { exact: true }).locator("summary").first().click();
  assert.equal(await page.locator('[data-atlas-file="material-file"]').isVisible(), false);
  await page.getByLabel("文件目录树", { exact: true }).locator("summary").first().click();
  await page.emulateMedia({ colorScheme: "dark" });
  assert.equal(await page.locator("main").evaluate(node => getComputedStyle(node).backgroundColor), "rgb(255, 255, 255)");
  await page.emulateMedia({ colorScheme: "light" });
  await page.getByLabel("提交文件改动关系图").waitFor();
  await page.getByRole("button", { name: "补齐换算边界 → Services/Material/换算.cs", exact: true }).click();
  await page.getByLabel("Git 差异证据").waitFor();
  assert.deepEqual(lastEvidence, { commit: "c2", file: "material-file" });
  await page.locator('[data-atlas-file="material-file"]').click();
  await page.getByLabel("Git 差异证据").waitFor();
  assert.match(await page.getByLabel("Git 差异证据").innerText(), /required \* ratio/);
  assert.equal(await page.evaluate(() => window.__atlasInjected), undefined);
  await page.getByRole("tab", { name: "协作交集", exact: true }).click();
  await page.getByRole("button", { name: "陈工 Material 1 次提交", exact: true }).click();
  await page.getByText("2 次已载入提交", { exact: true }).waitFor();
  await page.getByRole("tab", { name: "代码结构", exact: true }).click();
  assert.equal(await page.locator("[data-atlas-file]").count(), 2, "person filter preserves context files");
  await page.getByRole("button", { name: "清除筛选", exact: true }).click();
  await page.getByRole("tab", { name: "变化回放", exact: true }).click();
  await page.getByRole("button", { name: "Material 2026-10-31 1 次提交", exact: true }).click();
  await page.getByText("1 次已载入提交", { exact: true }).waitFor();
  await page.getByRole("slider", { name: "活动时间截面" }).press("End");
  await page.getByText("3 次已载入提交", { exact: true }).waitFor();
  await page.getByLabel("仅看选中当天", { exact: true }).check();
  await page.getByText("1 次已载入提交", { exact: true }).waitFor();
  await page.getByLabel("仅看选中当天", { exact: true }).uncheck();
  await page.getByRole("tab", { name: "业务地图", exact: true }).click();
  await page.getByRole("button", { name: "开发视角", exact: true }).click();
  assert.equal(await page.getByRole("tab", { name: "代码结构", exact: true }).getAttribute("aria-selected"), "true");
  await page.getByRole("button", { name: "解读当前模块", exact: true }).click();
  await page.getByText("模型完成换算解读", { exact: true }).waitFor();
  assert.equal(lastAnalysis.module_id, "material"); assert.equal(lastAnalysis.perspective, "developer"); assert.equal(lastAnalysis.model_id, "fixture-model");
  await page.getByRole("tab", { name: "业务地图", exact: true }).click();
  assert.equal(await page.getByText("AI 名称候选", { exact: true }).count(), 1);
  await page.getByRole("button", { name: /换算.cs · 2222222/ }).click();
  await page.getByLabel("Git 差异证据").waitFor();
  delayAnalysis = true;
  const started = page.waitForRequest(request => request.url().endsWith("/analysis/stream") && request.method() === "POST");
  await page.getByRole("button", { name: "解读当前模块", exact: true }).click(); await started;
  await page.locator('[data-atlas-module="inventory"]').click();
  await page.waitForTimeout(800);
  assert.equal(await page.getByText("迟到的旧模块解读", { exact: true }).count(), 0);
  assert.equal(await page.getByText("模型完成换算解读", { exact: true }).count(), 0);
  delayEvidence = true;
  await page.getByRole("button", { name: "修正替代料扣减", exact: true }).click();
  await page.getByLabel("变化地图项目", { exact: true }).selectOption("2");
  await page.getByRole("button", { name: "其他项目改动", exact: true }).waitFor(); await page.waitForTimeout(800);
  assert.equal(await page.getByLabel("Git 差异证据").count(), 0, "old evidence cannot leak into next project");
  await page.getByLabel("变化地图项目", { exact: true }).selectOption("1");
  await page.getByText("3 次已载入提交", { exact: true }).waitFor();
  await page.getByLabel("同步远端后读取", { exact: true }).check();
  await page.getByText(/测试代码库 1 · 已同步远端/).waitFor();
  for (const width of [1440, 1024, 736, 375, 320]) {
    await page.setViewportSize({ width, height: 1000 });
    for (const name of ["业务地图", "代码结构", "变化回放", "协作交集"]) {
      await page.getByRole("tab", { name, exact: true }).click();
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2), `${width}px ${name} should not overflow`);
    }
  }
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.getByRole("tab", { name: "业务地图", exact: true }).click();
  await page.getByRole("button", { name: "交付视角", exact: true }).click();
  assert.equal(await page.getByRole("tab", { name: "业务地图", exact: true }).getAttribute("aria-selected"), "true");
  if (process.env.ATLAS_SCREENSHOT) await page.screenshot({ path: process.env.ATLAS_SCREENSHOT, fullPage: true });
  assert.deepEqual(errors, []); assert.deepEqual(external, []);
  console.log("PASS: workbench navigation, four views, person/time/project/ref scope, immutable structure, evidence escaping, model request, AI citations, stale response cancellation and 1440/1024/736/375/320px layouts.");
} finally { await browser.close(); }
