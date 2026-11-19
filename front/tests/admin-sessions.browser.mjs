// Synthetic fixtures only; no real accounts, messages or model calls.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
const require = process.env.GOAL_TEST_TOOLS ? createRequire(path.resolve(process.env.GOAL_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(10000);
  page.on("console", m => { if (m.type() === "error") console.error(m.text()); });
  const errors = [];
  page.on("pageerror", e => errors.push(e.message));
  const row = (id, user_id, title, day) => ({ id, user_id, username: user_id, title, updated_at: `2026-11-${day}T09:00:00Z`, created_at: `2026-11-${day}T09:00:00Z`, message_count: 2, project_name: "测试项目", last_message: "最后回复" });
  const rows = [row("new", "alice", "较新会话", "19"), row("old", "bob", "较早会话", "18")];
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    let body = [];
    if (url.pathname.endsWith("/auth/status")) body = { authenticated: true, is_admin: true, username: "admin", role: "admin" };
    else if (url.pathname === "/api/v1/settings") body = { ui: { theme: "light", language: "zh-CN", preferred_agent: "codex" }, catalog: { version: 1, services: { llm: { active_profile_id: "fixture", active_model_id: "fixture", profiles: [{ id: "fixture", name: "Fixture", models: [{ id: "fixture", name: "Fixture", model: "fixture" }] }] } } } };
    else if (url.pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
    else if (url.pathname.endsWith("/sessions/list")) body = { sessions: [] };
    else if (url.pathname === "/api/v1/admin/users") body = [{ id: "alice", username: "alice", alias: "小艾" }, { id: "bob", username: "bob", alias: "小波" }];
    else if (url.pathname === "/api/v1/admin/sessions") body = rows.filter(r => !url.searchParams.get("user_id") || r.user_id === url.searchParams.get("user_id"));
    else if (url.pathname.includes("/sessions/")) {
      const id = url.pathname.split("/").at(-1);
      if (id === "new") await new Promise(r => setTimeout(r, 600));
      body = { title: id, messages: [{ id: 1, role: "user", content: `提问 ${id}`, created_at: "2026-11-19T09:00:00Z" }, { id: 2, role: "assistant", content: `## 回答 ${id}\n\n| 名称 | 值 |\n| --- | --- |\n| 测试 | 成功 |\n\n\`\`\`js\nconst answer = 42;\n\`\`\``, created_at: "2026-11-19T09:01:00Z" }] };
    }
    await route.fulfill({ json: body });
  });
  await page.goto(`${process.env.ADMIN_TEST_URL || "http://127.0.0.1:3783"}/settings/admin/sessions`);
  const buttons = page.locator("section aside button");
  await page.getByRole("button", { name: /较新会话/ }).waitFor();
  assert.match(await buttons.first().innerText(), /较新会话/);
  await page.getByRole("button", { name: /更新时间/ }).click();
  assert.match(await buttons.first().innerText(), /较早会话/);
  await page.getByRole("textbox", { name: "搜索会话" }).fill("小波");
  assert.equal(await buttons.count(), 1);
  await page.getByRole("textbox", { name: "搜索会话" }).fill("");
  await page.getByRole("button", { name: /较新会话/ }).click();
  await page.getByRole("button", { name: /较早会话/ }).click();
  await page.locator("article table").waitFor();
  await page.waitForTimeout(800);
  assert.equal(await page.locator("article h2").last().innerText(), "回答 old");
  assert.match(await page.locator("article pre").innerText(), /answer = 42/);
  const before = await page.locator("article").innerText();
  assert.ok(before.indexOf("提问 old") < before.indexOf("回答 old"));
  await page.getByRole("button", { name: /消息顺序/ }).click();
  const after = await page.locator("article").innerText();
  assert.ok(after.indexOf("回答 old") < after.indexOf("提问 old"));
  assert.equal(await page.locator("article table").count(), 1);
  await page.getByLabel("筛选人员").selectOption("alice");
  await page.getByRole("button", { name: /较新会话/ }).waitFor();
  assert.equal(await buttons.count(), 1);
  assert.equal(await page.locator("article table").count(), 0);
  await page.getByRole("button", { name: /较新会话/ }).click();
  await page.locator("article table").waitFor();
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  assert.deepEqual(errors, []);
  console.log("PASS: sorting, alias search, person filtering, Markdown, stale detail isolation and mobile layout");
} finally { await browser.close(); }
