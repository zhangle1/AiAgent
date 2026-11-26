import assert from "node:assert/strict";
import path from "node:path";
import { createRequire } from "node:module";

const require = process.env.GOAL_TEST_TOOLS
  ? createRequire(path.resolve(process.env.GOAL_TEST_TOOLS, "package.json"))
  : createRequire(import.meta.url);
const { chromium } = require("playwright");
const executablePath = process.env.CHAT_TEST_BROWSER_EXECUTABLE;
const browser = await chromium.launch(executablePath ? { executablePath } : { channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
page.setDefaultTimeout(15_000);
const base = process.env.CHAT_TEST_URL ?? "http://127.0.0.1:3782/chat";
const project = { id: 11, name: "fixture", display_name: "测试项目", repositories: [] };
const updatedAt = new Date(Date.now() - 35 * 60000).toISOString();
const sessions = ["查询仓库根目录与项目资料的完整聊天标题", "另一个会话"].map((title, index) => ({
  id: `sidebar-${index}`, title, project_id: project.id, messages: [], preferences: {},
  message_count: 0, created_at: updatedAt, updated_at: updatedAt,
}));
const errors = [];
page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const pathname = new URL(route.request().url()).pathname;
  let body = [];
  if (pathname.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture" };
  else if (pathname.endsWith("/sessions/list")) body = { sessions };
  else if (pathname.endsWith("/sessions/project-preferences")) body = { preferences: [{ project_id: project.id, is_pinned: true }] };
  else if (pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  else if (pathname.endsWith("/code-repositories/projects")) body = [project];
  else if (pathname.endsWith("/git/status")) body = { project_id: project.id, repositories: [], state: "ready" };
  else if (pathname.endsWith(`/code-runtime/projects/${project.id}`)) body = { project_id: project.id, profiles: [], runs: [] };
  else if (pathname.endsWith("/settings")) body = { ui: {}, catalog: { version: 1, services: {
    llm: { active_profile_id: null, profiles: [] },
  } } };
  else if (pathname.endsWith("/codex-model-policy")) body = { models: [], allowed_model_ids: [] };
  else if (pathname.endsWith("/goal")) body = { goal: null };
  else body = sessions.find(session => pathname.endsWith(`/sessions/${session.id}`)) ?? [];
  await route.fulfill({ json: body });
});

const resize = page.getByRole("separator", { name: "调整侧边栏宽度" });
const aside = page.locator("aside").filter({ has: page.getByRole("navigation", { name: "工作台导航" }) });
const tooltip = page.getByRole("tooltip");
const sessionLink = page.getByRole("link", { name: sessions[0].title, exact: true });
const checkWidth = width => page.waitForFunction(expected => {
  const sidebar = document.getElementById("workspace-sidebar-panel")?.closest("aside");
  return sidebar?.getBoundingClientRect().right === expected
    && document.querySelector("main")?.getBoundingClientRect().left === expected;
}, width);

try {
  await page.goto(`${base}?session=${sessions[1].id}`);
  await sessionLink.first().waitFor();
  assert.equal(await sessionLink.count(), 2, "Pinned and grouped sessions both expose the title");
  await checkWidth(296);
  await sessionLink.first().hover();
  await tooltip.waitFor();
  assert.equal(await tooltip.getByText(sessions[0].title, { exact: true }).count(), 1);
  assert.equal(await tooltip.getByText(project.display_name, { exact: true }).count(), 1);
  assert.equal(await tooltip.locator("time").getAttribute("datetime"), updatedAt);
  assert.ok((await tooltip.boundingBox()).x >= (await aside.boundingBox()).width);

  await page.keyboard.press("Escape");
  await tooltip.waitFor({ state: "hidden" });
  await sessionLink.last().click();
  await page.waitForURL(`**/chat?session=${sessions[0].id}`);
  await tooltip.waitFor();
  assert.equal(await sessionLink.last().getAttribute("aria-current"), "page");
  await page.mouse.move(800, 200);
  await tooltip.waitFor({ state: "hidden" });
  await page.keyboard.press("Tab");
  await sessionLink.last().focus();
  await tooltip.waitFor();
  await page.keyboard.press("Escape");
  await tooltip.waitFor({ state: "hidden" });

  const grip = await resize.boundingBox();
  const startX = grip.x + grip.width / 2;
  await page.mouse.move(startX, 380);
  await page.mouse.down();
  await page.mouse.move(startX + 114, 380, { steps: 6 });
  await checkWidth(410);
  await page.mouse.move(startX + 64, 380, { steps: 6 });
  await checkWidth(360);
  await page.mouse.up();
  await page.reload();
  await checkWidth(360);
  await resize.focus();
  await page.keyboard.press("End");
  await checkWidth(520);
  await page.keyboard.press("Home");
  await checkWidth(240);
  await page.keyboard.press("ArrowRight");
  await checkWidth(256);
  await aside.getByRole("button", { name: "收起侧边栏" }).first().click();
  await checkWidth(56);
  await resize.waitFor({ state: "hidden" });
  await aside.getByRole("button", { name: "展开侧边栏" }).click();
  await checkWidth(256);

  await page.setViewportSize({ width: 390, height: 844 });
  await resize.waitFor({ state: "hidden" });
  await page.getByRole("button", { name: "打开工作台抽屉" }).click();
  await sessionLink.last().hover();
  await page.waitForTimeout(250);
  assert.equal(await tooltip.count(), 0);
  assert.equal((await aside.boundingBox()).width, 296);
  assert.equal(await page.locator("main").evaluate(element => element.getBoundingClientRect().left), 0);
  assert.deepEqual(errors, []);
  console.log("PASS: session title preview, navigation, mouse and keyboard sidebar resize, persistence, collapse and mobile drawer.");
} finally {
  await browser.close();
}
