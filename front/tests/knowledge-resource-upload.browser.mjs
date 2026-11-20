// Actual UI, synthetic APIs and PDF bytes only; no user data or model calls.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir } from "node:fs/promises";
import path from "node:path";
const dependencies = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const artifacts = process.env.CHAT_TEST_ARTIFACTS;
if (artifacts) await mkdir(artifacts, { recursive: true });
const { chromium } = dependencies("playwright");
const browser = await chromium.launch({ headless: true, channel: "msedge" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
page.setDefaultTimeout(15000);
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const filename = "Design and Optimization of Manufacturing Systems, Second Edition.pdf";
const book = { uri: root.uri + encodeURIComponent(filename), parent_uri: root.uri, name: filename, kind: "file", extension: ".pdf", document_id: 101, knowledge_base_name: "resource-store", status: "pending" };
let mode = "http-error", uploaded = false, uploads = 0;
const errors = [];
page.on("pageerror", error => errors.push(error.stack));
await page.route("**/api/**", async route => {
  const url = new URL(route.request().url());
  let body = [];
  if (url.pathname.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture" };
  else if (url.pathname.endsWith("/sessions/list")) body = { sessions: [] };
  else if (url.pathname.endsWith("/sessions/project-preferences")) body = { preferences: [] };
  else if (url.pathname.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "name" };
  else if (url.pathname === "/api/v1/settings") body = { ui: { theme: "light", language: "zh-CN" }, catalog: { version: 1, services: {} } };
  else if (url.pathname.endsWith("/knowledge-agent/options")) body = { llm_models: [], default_model_id: null };
  else if (url.pathname.endsWith("/knowledge-resources/tree")) body = [root, ...(uploaded ? [book] : [])];
  else if (url.pathname.endsWith("/knowledge-resources/read")) body = { node: root, children: uploaded ? [book] : [], semantic_status: "missing_or_stale" };
  else if (url.pathname.endsWith("/knowledge-resources/upload")) {
    uploads++;
    const bytes = route.request().postDataBuffer();
    assert.ok(bytes.length > 16 * 1024 * 1024);
    assert.ok(bytes.includes(Buffer.from(filename)), "multipart must retain the book name");
    if (mode === "http-error") return route.fulfill({ status: 500, json: { message: "模拟上传服务异常" } });
    if (mode === "item-error") body = { items: [{ file_name: filename, status: "failed", message: "模拟保存失败" }], tasks: [], warnings: [] };
    else {
      uploaded = true;
      body = { items: [{ file_name: filename, status: "imported", document_id: 101, message: "原始文件已保存。" }], tasks: [], warnings: ["已保存，解析未入队：未配置模型"] };
    }
  }
  await route.fulfill({ json: body });
});
try {
  await page.goto("http://127.0.0.1:3782/knowledge");
  await page.getByRole("button", { name: "添加资源或导入会话" }).click();
  await page.getByRole("button", { name: "添加资源", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "添加资源", exact: true });
  await dialog.locator('input[type="file"]').setInputFiles({ name: filename, mimeType: "application/pdf", buffer: Buffer.alloc(Math.ceil(16.1 * 1024 * 1024), 32) });
  await dialog.getByRole("button", { name: "开始处理", exact: true }).click();
  await dialog.getByRole("alert").filter({ hasText: "模拟上传服务异常" }).waitFor();
  assert.equal(await dialog.count(), 1, "failed upload stays open and shows the error inside the dialog");
  mode = "item-error";
  await dialog.getByRole("button", { name: "开始处理", exact: true }).click();
  await dialog.getByRole("alert").filter({ hasText: "模拟保存失败" }).waitFor();
  assert.equal(await dialog.count(), 1, "zero imported files must not be reported as saved");
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "resource-upload-error.png") });
  mode = "saved";
  await dialog.getByRole("button", { name: "开始处理", exact: true }).click();
  await page.getByRole("button", { name: filename, exact: true }).first().waitFor();
  assert.equal(await dialog.count(), 0);
  assert.ok((await page.locator("main").last().innerText()).includes("解析未入队"));
  if (artifacts) await page.screenshot({ path: path.join(artifacts, "resource-upload-saved.png") });
  assert.equal(uploads, 3);
  assert.deepEqual(errors, []);
  console.log("PASS: 16.1 MB PDF multipart; HTTP/item failures shown in dialog with retry; saved raw book visible even when parsing cannot queue.");
} catch (error) {
  console.error({ url: page.url(), errors, visible: (await page.locator("body").innerText()).slice(-1800) });
  throw error;
} finally { await browser.close(); }
