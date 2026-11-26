// Actual Next.js UI with synthetic resources and intercepted APIs; never deletes user data.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdtemp } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
const require = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const output = await mkdtemp(path.join(os.tmpdir(), "knowledge-delete-ui-"));
const content = "BT /F1 22 Tf 40 750 Td (Synthetic PDF deletion fixture) Tj ET\n";
const objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
  "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
  "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", `<< /Length ${Buffer.byteLength(content)} >>\nstream\n${content}endstream`];
let pdfText = "%PDF-1.7\n"; const offsets = [];
objects.forEach((value, index) => { offsets.push(Buffer.byteLength(pdfText)); pdfText += `${index + 1} 0 obj\n${value}\nendobj\n`; });
const xref = Buffer.byteLength(pdfText);
pdfText += `xref\n0 6\n0000000000 65535 f \n${offsets.map(offset => `${String(offset).padStart(10, "0")} 00000 n \n`).join("")}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
const pdf = Buffer.from(pdfText);
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const book = { uri: root.uri + "Factory%20Physics.pdf", parent_uri: root.uri, name: "Factory Physics.pdf", kind: "file", extension: ".pdf", document_id: 101, can_delete: true, source_status: "available", status: "error" };
const busy = { ...book, uri: root.uri + "busy.pdf", name: "busy.pdf", document_id: 102, status: "parsing" };
const readonly = { ...book, uri: root.uri + "readonly.pdf", name: "readonly.pdf", document_id: 103, can_delete: false };
const parsed = { uri: root.uri + "_parsed/", parent_uri: root.uri, name: "解析正文", kind: "directory" };
const chapter = { uri: parsed.uri + "chapter.md", parent_uri: parsed.uri, name: "chapter.md", kind: "file", extension: ".md", document_id: 101, source_uri: book.uri, can_delete: false };
let removed = false, failure = true, deleteCalls = 0, releaseDelete;
const nodes = () => [root, busy, readonly, ...(removed ? [] : [book, parsed, chapter])];
const browser = await chromium.launch({ headless: true, channel: process.env.KNOWLEDGE_TEST_BROWSER_CHANNEL ?? "msedge" });
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(15000);
  const errors = []; page.on("pageerror", error => errors.push(error.message));
  await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), name = url.pathname;
    let body = [];
    if (name.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture", role: "admin" };
    else if (name.endsWith("/sessions/list")) body = { sessions: [] };
    else if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
    else if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "name" };
    else if (name.endsWith("/settings")) body = { ui: { language: "zh-CN" }, catalog: { services: {} } };
    else if (name.endsWith("/knowledge-agent/options")) body = { llm_models: [], skills: [] };
    else if (name.endsWith("/knowledge-resources/tree")) body = nodes();
    else if (name.endsWith("/knowledge-resources/task")) body = null;
    else if (name.endsWith("/cleanup/preview")) body = { snapshot_sha256: "synthetic-empty", includes_shared: true, items: [] };
    else if (name.endsWith("/knowledge-resources/read")) {
      const node = nodes().find(n => n.uri === url.searchParams.get("uri")) ?? root;
      body = { node, children: nodes().filter(n => n.parent_uri === node.uri), source_text: node === chapter ? "# Synthetic chapter\n\nRetained text." : null, semantic_status: "not_applicable" };
    } else if (name.endsWith("/knowledge-resources/file")) {
      if (request.method() === "DELETE") {
        deleteCalls++;
        assert.equal(url.searchParams.get("uri"), book.uri);
        assert.equal(url.searchParams.get("document_id"), "101");
        if (failure) return route.fulfill({ status: 409, json: { message: "文件存在活动处理任务，请先取消任务并等待结束后再删除。" } });
        await new Promise(resolve => { releaseDelete = resolve; });
        removed = true; body = { deleted: true, document_id: book.document_id, uri: book.uri };
      } else return route.fulfill({ body: pdf, contentType: "application/pdf" });
    }
    await route.fulfill({ json: body });
  });
  await page.goto(`${process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782"}/knowledge`);
  const tree = page.locator("main nav"), deleteButton = page.getByRole("button", { name: "删除文件", exact: true });
  await page.getByRole("button", { name: "清理失效记录", exact: true }).click();
  await page.getByText("正常文件请选中文件后，使用预览顶部的“删除文件”。", { exact: true }).waitFor();
  await page.keyboard.press("Escape");
  await tree.getByRole("button", { name: "readonly.pdf", exact: true }).click();
  await page.locator("h1").filter({ hasText: "readonly.pdf" }).waitFor();
  assert.equal(await deleteButton.count(), 0);
  await tree.getByRole("button", { name: "busy.pdf", exact: true }).click();
  await deleteButton.waitFor(); assert.equal(await deleteButton.isDisabled(), true);
  await tree.getByRole("button", { name: "解析正文", exact: true }).click();
  await tree.getByRole("button", { name: "chapter.md", exact: true }).click();
  await page.locator("h1").filter({ hasText: "chapter.md" }).waitFor();
  assert.equal(await deleteButton.count(), 0);
  await tree.getByRole("button", { name: book.name, exact: true }).click();
  await page.waitForFunction(() => document.querySelector(".react-pdf__Page canvas")?.width > 100);
  assert.equal(await deleteButton.isEnabled(), true);
  await deleteButton.click();
  const dialog = page.getByRole("dialog", { name: "删除资料", exact: true });
  const cancel = dialog.getByRole("button", { name: "取消", exact: true });
  const confirm = dialog.getByRole("button", { name: "确认删除", exact: true });
  await dialog.getByText(book.name, { exact: true }).waitFor();
  assert.equal(await cancel.evaluate(element => document.activeElement === element), true);
  await page.keyboard.press("Escape"); await dialog.waitFor({ state: "hidden" });
  assert.equal(deleteCalls, 0); assert.equal(await deleteButton.evaluate(element => document.activeElement === element), true);
  await deleteButton.click(); await cancel.click(); assert.equal(deleteCalls, 0);
  await deleteButton.click();
  await page.screenshot({ path: path.join(output, "delete-desktop.png") });
  await confirm.click(); await dialog.getByRole("alert").filter({ hasText: "活动处理任务" }).waitFor();
  assert.equal(removed, false); assert.equal(await tree.getByRole("button", { name: book.name, exact: true }).count(), 1);
  await page.setViewportSize({ width: 390, height: 844 });
  const bounds = await dialog.evaluate(element => ({ width: element.clientWidth, scroll: element.scrollWidth, right: element.getBoundingClientRect().right, viewport: innerWidth }));
  assert.ok(bounds.scroll <= bounds.width + 1 && bounds.right <= bounds.viewport);
  await page.screenshot({ path: path.join(output, "delete-mobile.png") });
  failure = false; await confirm.click();
  await page.waitForFunction(() => [...document.querySelectorAll("dialog button")].some(button => button.textContent.includes("确认删除") && button.disabled));
  await page.keyboard.press("Escape"); assert.equal(await dialog.isVisible(), true);
  assert.equal(await cancel.isDisabled(), true);
  releaseDelete(); await dialog.waitFor({ state: "hidden" });
  await page.getByText(`已从资料库移除 ${book.name}，磁盘原文件和历史记录已保留。`, { exact: true }).waitFor();
  assert.equal(await tree.getByRole("button", { name: book.name, exact: true }).count(), 0);
  assert.equal(await tree.getByRole("button", { name: "chapter.md", exact: true }).count(), 0);
  assert.equal(await tree.getByRole("button", { name: "readonly.pdf", exact: true }).count(), 1);
  assert.equal(removed, true); assert.equal(deleteCalls, 2); assert.deepEqual(errors, []);
  console.log(`PASS: PDF deletion, cleanup guidance, permission/busy/generated UI, cancel/Escape/focus, conflict/retry, pending protection, source/derived refresh, 390px dialog. Screenshots: ${output}`);
} finally { releaseDelete?.(); await browser.close(); }
