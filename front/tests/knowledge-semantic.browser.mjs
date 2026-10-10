// KNOWLEDGE_TEST_TOOLS=<directory with playwright and esbuild> node tests/knowledge-semantic.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const files = ["one", "two"].map(name => ({ uri: `${root.uri}${name}.md`, parent_uri: root.uri, kind: "file", name: `${name}.md` }));
const overview = "# 概览\n\n目录简介。\n\n[one.md](viking://resources/one.md)\n\n[外部](https://example.com)\n\n![图片](https://example.com/a.png)\n\n<script>window.injected=true</script>";
const bundle = await build({
  stdin: { contents: `import React from 'react'; import {createRoot} from 'react-dom/client'; import {KnowledgeWorkspace} from './components/knowledge/KnowledgeWorkspace'; createRoot(document.getElementById('root')).render(<KnowledgeWorkspace/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, platform: "browser", format: "iife", jsx: "automatic",
  outfile: path.join(front, "knowledge-semantic-fixture", "bundle.js"),
});
let slowRead = false;
let jobId = 1;
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://fixture.test");
  if (url.pathname === "/bundle.js") { res.setHeader("Content-Type", "text/javascript"); return res.end(bundle.outputFiles.find(file => file.path.endsWith(".js")).text); }
  if (url.pathname === "/bundle.css") { res.setHeader("Content-Type", "text/css"); return res.end(bundle.outputFiles.find(file => file.path.endsWith(".css"))?.text ?? ""); }
  if (url.pathname.startsWith("/api/")) {
    res.setHeader("Content-Type", "application/json");
    if (url.pathname.endsWith("/tree")) return res.end(JSON.stringify([root, ...files]));
    const uri = url.searchParams.get("uri");
    if (url.pathname.endsWith("/read")) {
      if (uri === files[0].uri && slowRead) await new Promise(resolve => setTimeout(resolve, 700));
      return res.end(JSON.stringify(uri === root.uri ? { node: root, abstract_content: "目录简介。", overview_content: overview, semantic_status: "ready" } : { node: files.find(f => f.uri === uri), source_text: uri === files[0].uri ? `文件一 v${jobId}` : "文件二", semantic_content: `文件摘要\n\n${overview}` }));
    }
    if (url.pathname.endsWith("/parse")) { jobId++; return res.end(JSON.stringify({ id: jobId, status: "success", progress: 100 })); }
    if (url.pathname.endsWith("/task")) return res.end(JSON.stringify({ id: jobId, status: "success", progress: 100 }));
    res.writeHead(404); return res.end("{}");
  }
  res.setHeader("Content-Type", "text/html; charset=utf-8");
  res.end('<html><head><link rel="stylesheet" href="/bundle.css"></head><body><div id="root"></div><script src="/bundle.js"></script></body></html>');
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 }, acceptDownloads: true });
  const errors = []; const external = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("request", request => { if (request.url().startsWith("https://example.com")) external.push(request.url()); });
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.getByRole("button", { name: "下载 .overview.md", exact: true }).waitFor();
  await page.getByRole("button", { name: "L0 · 目录摘要", exact: true }).click();
  const event = page.waitForEvent("download");
  await page.getByRole("button", { name: "下载 .abstract.md", exact: true }).click();
  const download = await event;
  // Chromium on Windows strips the leading dot from download filenames.
  assert.match(download.suggestedFilename(), /^\.?abstract\.md$/);
  const stream = await download.createReadStream(); const chunks = [];
  for await (const chunk of stream) chunks.push(chunk);
  assert.equal(Buffer.concat(chunks).toString("utf8"), "目录简介。");
  await page.getByRole("button", { name: "L1 · 目录概览", exact: true }).click();
  assert.equal(await page.evaluate(() => window.injected), undefined);
  assert.deepEqual(external, []);
  await page.locator("article").getByRole("button", { name: "one.md", exact: true }).click();
  await page.getByText("文件一 v1", { exact: true }).waitFor();
  // A second task can finish between polling ticks, with the same terminal status.
  await page.getByRole("button", { name: "重新解析", exact: true }).click();
  await page.getByText("文件一 v2", { exact: true }).waitFor();
  slowRead = true;
  await page.getByRole("button", { name: "two.md", exact: true }).first().click();
  await page.getByText("文件二", { exact: true }).waitFor();
  await page.getByRole("button", { name: "one.md", exact: true }).first().click();
  await page.getByRole("button", { name: "two.md", exact: true }).first().click();
  await page.waitForTimeout(1000);
  assert.equal(await page.getByText("文件二", { exact: true }).count(), 1);
  assert.equal(await page.getByText("文件一 v2", { exact: true }).count(), 0);
  await page.getByRole("button", { name: "知识摘要", exact: true }).click();
  await page.getByText("文件摘要", { exact: true }).waitFor();
  assert.equal(await page.locator("article img, article a").count(), 0);
  assert.equal(await page.evaluate(() => window.injected), undefined);
  assert.deepEqual(external, []);
  assert.deepEqual(errors, []);
  console.log("PASS: L0/L1 navigation, Markdown download, safe rendering, repeat task refresh and stale response isolation");
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
