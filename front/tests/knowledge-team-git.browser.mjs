import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
const require = createRequire(import.meta.url);
const dependencies = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : require;
const { chromium } = dependencies("playwright");
const browser = await chromium.launch({ headless: true, ...(process.env.KNOWLEDGE_TEST_BROWSER ? { executablePath: process.env.KNOWLEDGE_TEST_BROWSER } : { channel: "msedge" }) });
const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
let config = { remote_url: "", branch: "main" };
let pushed = false;
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
  if (url.pathname === "/api/v1/settings") body = { ui: { language: "zh-CN", theme: "light" }, catalog: { version: 1, services: Object.fromEntries(["llm", "embedding", "search", "tts", "stt", "imagegen", "videogen"].map(name => [name, { profiles: [], active_profile_id: null, active_model_id: null }])) }, providers: {} };
  if (url.pathname.endsWith("/codex-model-policy") || url.pathname.endsWith("/compiler-settings"))
    return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "测试未加载模型配置" }) });
  if (url.pathname.endsWith("/knowledge/team-git")) {
    if (request.method() === "PUT") { config = request.postDataJSON(); body = config; }
    else body = { root_path: "D:\\KnowledgeCenter\\storage\\knowledge_bases", workspace_path: "D:\\KnowledgeCenter\\workspaces\\team-knowledge", config, status: null };
  }
  if (url.pathname.endsWith("/team-git/refresh")) body = { snapshot_sha256: "fixture-snapshot", is_valid: true, files: ["?? projects/订单系统/需求.md"], checks: [{ name: "remote", passed: true, message: "远端检查成功" }] };
  if (url.pathname.endsWith("/team-git/push")) {
    assert.deepEqual(request.postDataJSON(), { message: "更新团队资料", snapshot: "fixture-snapshot" });
    pushed = true; body = { ok: true, commit_sha: "abc1234", output: "ok" };
  }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});
try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/settings/knowledge#team-git`);
  const section = page.locator("#team-git");
  await section.getByText("团队知识 Git", { exact: true }).waitFor();
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
  await section.getByRole("checkbox").check();
  await push.click();
  await section.getByText("已推送到团队仓库 · abc1234", { exact: true }).waitFor();
  assert.equal(pushed, true);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(200);
  const overflow = await page.evaluate(() => [...document.querySelectorAll("#team-git, #team-git *")].filter(element => element.getBoundingClientRect().right > window.innerWidth + 1).map(element => ({ tag: element.tagName, class: element.className })));
  assert.deepEqual(overflow, []);
  assert.deepEqual(errors, []);
  console.log("Team Git settings: configuration, file review, confirmation, push and mobile layout passed.");
} finally { await browser.close(); }
