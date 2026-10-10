// Uses an already running frontend; all API responses are synthetic, no user data is read or written.
// KNOWLEDGE_TEST_TOOLS=<directory containing playwright> node tests/knowledge-layout.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";

const require = createRequire(import.meta.url);
const dependencies = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : require;
const { chromium } = dependencies("playwright");
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1800, height: 1000 } });
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const nodes = [
  { uri: root.uri + "guide.md", parent_uri: root.uri, name: "guide.md", kind: "file", status: "processed", size: 18000 },
  { uri: root.uri + "broken.md", parent_uri: root.uri, name: "broken.md", kind: "file", status: "processed", size: 497 },
  { uri: root.uri + "raw.md", parent_uri: root.uri, name: "raw.md", kind: "file", status: "pending", size: 0 },
];
let recovered = false;
let failRead = false;
let processingCalls = 0;
let activeTask = false;
const errors = [];
page.on("pageerror", error => errors.push(error.message));
await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url());
  let body = [];
  if (url.pathname.endsWith("/auth/status")) body = { authenticated: true, username: "ui-fixture", user_id: "fixture" };
  if (url.pathname.endsWith("/sessions/list")) body = { sessions: [] };
  if (url.pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  if (url.pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "recent" };
  if (url.pathname.endsWith("/knowledge-resources/tree")) body = [root, ...nodes];
  if (url.pathname.endsWith("/knowledge-resources/read")) {
    const node = [root, ...nodes].find(item => item.uri === url.searchParams.get("uri")) ?? root;
    body = { node, children: nodes, semantic_status: "missing_or_stale", source_text: Array.from({ length: 160 }, (_, i) => `正文第 ${i + 1} 行。`).join("\n\n") };
  }
  if (url.pathname.endsWith("/knowledge-resources/task")) body = null;
  if (url.pathname.endsWith("/knowledge-resources/processing")) {
    processingCalls++;
    if (failRead) return route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ message: "模拟读取失败" }) });
    body = [
      { node: nodes[0], task_status: activeTask ? "processing" : "success", stage: activeTask ? "parsing" : "completed", progress: activeTask ? 12 : 100 },
      { node: nodes[1], task_status: recovered ? "success" : "error", stage: recovered ? "completed" : "semantic", error_message: recovered ? null : "模型连接超时；正文已保存", progress: recovered ? 100 : 35 },
      { node: nodes[2] },
    ];
  }
  await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
});

try {
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  await page.getByRole("button", { name: "文件处理", exact: true }).waitFor();
  const splitters = page.getByRole("separator");
  assert.equal(await splitters.count(), 2);
  const terminal = page.getByRole("separator", { name: "调整预览与 Agent 终端宽度" });
  assert.equal(await terminal.getAttribute("aria-valuenow"), "480");
  const box = await terminal.boundingBox();
  await page.mouse.move(box.x + 3, box.y + box.height / 2);
  await page.mouse.down(); await page.mouse.move(box.x - 97, box.y + box.height / 2); await page.mouse.up();
  assert.equal(await terminal.getAttribute("aria-valuenow"), "580");
  await terminal.focus(); await page.keyboard.press("ArrowRight");
  assert.equal(await terminal.getAttribute("aria-valuenow"), "556");
  await page.reload(); await page.getByRole("button", { name: "文件处理", exact: true }).waitFor();
  assert.equal(await page.getByRole("separator", { name: "调整预览与 Agent 终端宽度" }).getAttribute("aria-valuenow"), "556");

  await page.getByRole("button", { name: "guide.md", exact: true }).first().click();
  await page.getByText("正文第 160 行。", { exact: true }).waitFor();
  const layout = await page.locator("main").last().evaluate(element => ({ width: element.clientWidth, scrollWidth: element.scrollWidth, scrollContainers: [...element.querySelectorAll("article")].filter(node => ["auto", "scroll"].includes(getComputedStyle(node).overflowY)).length }));
  assert.equal(layout.width, layout.scrollWidth);
  assert.equal(layout.scrollContainers, 0, "Preview articles must not introduce a second scrollbar");
  await page.getByRole("button", { name: "文件处理", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "文件处理任务" });
  await dialog.getByText("处理失败", { exact: true }).waitFor();
  await dialog.getByRole("button", { name: "查看 broken.md 处理详情" }).click();
  await dialog.getByText("错误：模型连接超时；正文已保存", { exact: true }).waitFor();
  if (process.env.KNOWLEDGE_TEST_DIALOG_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_DIALOG_SCREENSHOT });
  assert.match(await dialog.innerText(), /正文已解析并保存.*语义生成 L0\/L1/);
  assert.equal(await dialog.getByText("待处理", { exact: true }).count(), 1);
  recovered = true;
  await dialog.getByRole("button", { name: "刷新", exact: true }).click();
  await dialog.getByText("处理失败", { exact: true }).waitFor({ state: "hidden" });
  assert.equal(await dialog.getByText("处理成功", { exact: true }).count(), 2);
  failRead = true;
  await dialog.getByRole("button", { name: "刷新", exact: true }).click();
  await dialog.getByRole("alert").waitFor();
  assert.match(await dialog.getByRole("alert").innerText(), /模拟读取失败/);
  assert.equal(await dialog.getByText("guide.md", { exact: true }).count(), 1, "Failed refresh preserves the last visible snapshot");
  failRead = false;
  activeTask = true;
  const beforePolling = processingCalls;
  await dialog.getByRole("button", { name: "刷新", exact: true }).click();
  await dialog.getByText("处理中", { exact: true }).waitFor();
  await page.waitForResponse(response => response.url().includes("/knowledge-resources/processing"), { timeout: 5000 });
  assert.ok(processingCalls >= beforePolling + 2, "Active tasks auto-refresh");
  activeTask = false;
  await page.keyboard.press("Escape");
  assert.equal(await page.getByRole("dialog").count(), 0);
  assert.equal(await page.getByRole("button", { name: "文件处理", exact: true }).evaluate(node => document.activeElement === node), true);
  if (process.env.KNOWLEDGE_TEST_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_SCREENSHOT, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(() => document.querySelector("main")?.getBoundingClientRect().left === 0);
  assert.equal(await page.getByRole("separator").count(), 0, "Small screens stack panels");
  assert.equal(await page.locator("body").evaluate(node => node.scrollWidth > innerWidth), false);
  assert.deepEqual(errors, []);
  assert.ok(processingCalls >= 3);
  if (process.env.KNOWLEDGE_TEST_MOBILE_SCREENSHOT) await page.screenshot({ path: process.env.KNOWLEDGE_TEST_MOBILE_SCREENSHOT, fullPage: true });
  console.log("PASS: panel drag/keyboard/persistence, single preview scrollbar, file statuses/errors/refresh, modal Escape/focus and mobile layout");
} catch (error) {
  console.log("Browser errors:", errors);
  console.log((await page.locator("body").innerText()).slice(0,1200));
  throw error;
} finally { await browser.close(); }
