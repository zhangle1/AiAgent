// DOCUMENT_CARD_TEST_TOOLS=<directory with playwright and esbuild> node tests/chat-document-cards.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.DOCUMENT_CARD_TEST_TOOLS ? createRequire(path.resolve(process.env.DOCUMENT_CARD_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const { css } = await require("postcss")([require("tailwindcss")({ content: [path.join(front, "components/chat/{MarkdownMessage,ChatDocumentCard}.tsx")] })]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const markdown = "已生成：[方案](repo/docs/方案.md)\n\n[演示](repo/docs/slides.pptx)\n\n[缺失](repo/docs/missing.txt)\n\n[report.md](https://example.com/report.md)\n\n```\nrepo/docs/方案.md\n```";
const bundle = await build({
  stdin: { contents: `import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
    import {MarkdownMessage} from './components/chat/MarkdownMessage';
    function App(){const [selected,setSelected]=useState('');return <main style={{maxWidth:720,padding:16}}><MarkdownMessage content={${JSON.stringify(markdown)}} projectId={7} onOpenProjectMarkdownDocument={setSelected}/><aside aria-label="预览选择">{selected}</aside></main>}
    createRoot(document.getElementById('root')).render(<App/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, platform: "browser", format: "iife", jsx: "automatic",
  plugins: [{ name: "unrelated-diagrams", setup(build) {
    build.onResolve({ filter: /(?:ArchitectureDiagram|MermaidDiagram)$/ }, (args) => ({ path: args.path, namespace: "fixture" }));
    build.onLoad({ filter: /.*/, namespace: "fixture" }, () => ({ contents: "export const architectureSourceFromPre=()=>null; export const mermaidSourceFromPre=()=>null; export const ArchitectureDiagram=()=>null; export const MermaidDiagram=()=>null;" }));
  } }],
});
let denyDownload = false;
const requests = [];
const bytes = Buffer.from([80, 75, 3, 4, 0, 255]);
const server = http.createServer((req, res) => {
  const url = new URL(req.url, "http://fixture.test");
  if (url.pathname === "/bundle.js") { res.setHeader("Content-Type", "text/javascript"); return res.end(bundle.outputFiles[0].text); }
  if (url.pathname.startsWith("/api/")) {
    requests.push(req.url);
    if (url.pathname.endsWith("/download")) {
      if (denyDownload) { res.writeHead(403); return res.end("denied"); }
      res.setHeader("Content-Type", "application/octet-stream"); return res.end(bytes);
    }
    res.setHeader("Content-Type", "application/json");
    return res.end(JSON.stringify(["方案.md", "slides.pptx"].map(name => ({ repository_name: "repo", path: `docs/${name}`, name }))));
  }
  res.setHeader("Content-Type", "text/html; charset=utf-8");
  res.end(`<html><head><style>${css}</style></head><body><div id="root"></div><script src="/bundle.js"></script></body></html>`);
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1000, height: 800 }, acceptDownloads: true });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  assert.equal(await page.locator("[data-document-card]").count(), 3);
  await page.getByTitle("在右侧项目文档中打开 方案.md").click();
  assert.equal(await page.getByLabel("预览选择").innerText(), "repo/docs/方案.md");
  const downloadEvent = page.waitForEvent("download");
  await page.getByRole("button", { name: "下载 slides.pptx", exact: true }).click();
  const download = await downloadEvent;
  assert.equal(download.suggestedFilename(), "slides.pptx");
  const stream = await download.createReadStream();
  const chunks = []; for await (const chunk of stream) chunks.push(chunk);
  assert.deepEqual(Buffer.concat(chunks), bytes);
  assert.equal(await page.getByLabel("预览选择").innerText(), "repo/docs/方案.md");
  denyDownload = true;
  await page.getByRole("button", { name: "下载 slides.pptx", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "403" }).waitFor();
  await page.getByRole("button", { name: "下载 missing.txt", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "找不到" }).waitFor();
  assert.equal(await page.getByRole("link", { name: "report.md" }).getAttribute("href"), "https://example.com/report.md");
  assert.equal(await page.locator("pre button").count(), 0);
  assert.equal(await page.locator("pre").innerText(), "repo/docs/方案.md\n");
  await page.setViewportSize({ width: 390, height: 844 });
  for (const card of await page.locator("[data-document-card]").all()) {
    const box = await card.boundingBox(); assert.ok(box.x >= 0 && box.x + box.width <= 390);
  }
  assert.ok(requests.every(url => url.startsWith("/api/v1/code-repositories/projects/7/")));
  assert.deepEqual(errors, []);
  console.log("PASS: document cards, preview selection, original download bytes, errors, external links, code fences and mobile width");
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
