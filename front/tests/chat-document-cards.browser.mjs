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
const { css } = await require("postcss")([require("tailwindcss")({ content: [path.join(front, "components/chat/{MarkdownMessage,ChatDocumentCard,ChatInspectorPanel,SaveMarkdownToKnowledgeDialog}.tsx"), path.join(front, "components/knowledge/KnowledgeResourcePicker.tsx")] })]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const chapterUri = "viking://resources/books/_parsed/Manual-41/parts/Cycle%20time.md";
const chapterSource = "viking://resources/books/Manual.pdf";
const chapterNode = { uri: chapterUri, kind: "file", name: "Cycle time.md", extension: ".md", document_id: 41, parsed_document_id: 57, source_uri: chapterSource };
const chapterImageUri = "viking://resources/books/_parsed/Manual-41/assets/chart.png";
const pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/5FoAAAAASUVORK5CYII=";
const personalTarget = "viking://user/reader/Reading/";
const savedUri = `${personalTarget}Cycle%20time.md`;
const projectTarget = "viking://projects/project-7/";
const sharedTarget = "viking://resources/books/";
const directories = [
  [personalTarget, "阅读笔记"], [projectTarget, "项目目录"], [sharedTarget, "共享书架"],
  ["viking://user/another/", "其他用户目录"], ["viking://user/reader/summaries/", "生成摘要"],
].map(([uri, name]) => ({ uri, name, kind: "directory" }));
const chapterLink = `/api/v1/knowledge-resources/file?${new URLSearchParams({ uri: chapterUri, parsed_document_id: "57" })}`;
const markdown = "已生成：[方案](repo/docs/方案.md)\n\n[演示](repo/docs/slides.pptx)\n\n[缺失](repo/docs/missing.txt)\n\n[report.md](https://example.com/report.md)\n\n```\nrepo/docs/方案.md\n```" +
  `\n\n[本轮章节](${chapterLink})\n\n[历史临时路径](file:///D:/Temp/aiagent-books-abc/41-Manual/parts/Cycle%20time.md)\n\n[历史书内路径](parts/Cycle%20time.md)`;
const bundle = await build({
  stdin: { contents: `import React,{useEffect,useState} from 'react'; import {createRoot} from 'react-dom/client';
    import {MarkdownMessage} from './components/chat/MarkdownMessage';
    import {ChatInspectorPanel} from './components/chat/ChatInspectorPanel';
    import {MarkdownDocumentPreview} from './components/MarkdownDocumentPreview';
    import {readKnowledgeResource,scopedKnowledgeChapterReferences} from './lib/knowledge-api';
    const chapters = scopedKnowledgeChapterReferences([${JSON.stringify(chapterNode)}], [${JSON.stringify(chapterSource)}]);
    function App(){const [selected,setSelected]=useState('');const [chapter,setChapter]=useState(null);return <main style={{display:'flex',padding:16,height:720}}><section style={{flex:1,minWidth:0}}><MarkdownMessage content={${JSON.stringify(markdown)}} projectId={7} onOpenProjectMarkdownDocument={setSelected} onOpenKnowledgeChapter={value=>setChapter({...value})} scopedKnowledgeChapters={chapters}/><aside aria-label="预览选择">{selected}</aside></section><ChatInspectorPanel isOpen={!!chapter} project={null} fileReference={null} requestedTab={chapter?'chapter':null} requestedKnowledgeChapter={chapter} onClose={()=>setChapter(null)}/></main>}
    function SavedChapter(){const [text,setText]=useState(null);useEffect(()=>{void readKnowledgeResource(${JSON.stringify(savedUri)}).then(resource=>setText(resource.source_text));},[]);return <section aria-label="已保存章节">{text && <MarkdownDocumentPreview content={text} knowledge={{}}/>}</section>}
    createRoot(document.getElementById('root')).render(location.pathname === '/saved-chapter' ? <SavedChapter/> : <App/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, outdir: "browser-fixture", platform: "browser", format: "iife", jsx: "automatic",
  plugins: [{ name: "unrelated-diagrams", setup(build) {
    build.onResolve({ filter: /(?:ArchitectureDiagram|MermaidDiagram|ChatPrototypePanel|OntologyWorkbench|ChatLibraryPanel)$/ }, (args) => ({ path: args.path, namespace: "fixture" }));
    build.onLoad({ filter: /.*/, namespace: "fixture" }, () => ({ contents: "export const architectureSourceFromPre=()=>null; export const mermaidSourceFromPre=()=>null; export const ArchitectureDiagram=()=>null; export const MermaidDiagram=()=>null; export const ChatPrototypePanel=()=>null; export const OntologyWorkbench=()=>null; export const ChatLibraryPanel=()=>null;" }));
  } }],
});
let denyDownload = false;
let chapterStatus = 200;
let assetStatus = 200;
const requests = [];
const uploads = [];
const bytes = Buffer.from([80, 75, 3, 4, 0, 255]);
const chapterText = "# 生产节拍\n\n资料库章节正文。\n\n## 计算示例\n\n- 观察等待时间\n\n| 字段 | 数值 |\n| --- | --- |\n| WIP | 2 |\n";
const chapterWithImages = `${chapterText}\n![解析插图](${chapterImageUri})\n\n![重复引用同一张图](${chapterImageUri})\n\n\`\`\`md\n![示例语法](${chapterImageUri})\n\`\`\`\n\n![禁止的图片格式](data:text/html;base64,PGgxPg==)`;
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://fixture.test");
  if (url.pathname === "/bundle.js") { res.setHeader("Content-Type", "text/javascript"); return res.end(bundle.outputFiles.find(file => file.path.endsWith(".js")).text); }
  if (url.pathname === "/bundle.css") { res.setHeader("Content-Type", "text/css"); return res.end(bundle.outputFiles.find(file => file.path.endsWith(".css")).text); }
  if (url.pathname.startsWith("/api/")) {
    requests.push(req.url);
    if (url.pathname === "/api/v1/auth/status") {
      res.setHeader("Content-Type", "application/json");
      return res.end(JSON.stringify({ authenticated: true, username: "reader", user_id: "reader" }));
    }
    if (url.pathname === "/api/v1/knowledge-resources/read") {
      res.setHeader("Content-Type", "application/json");
      if (url.searchParams.get("uri") === savedUri) {
        return res.end(JSON.stringify({ node: { uri: savedUri, kind: "file" }, source_text: uploads.at(-1)?.text }));
      }
      assert.equal(url.searchParams.get("uri"), chapterUri);
      if (chapterStatus === 403) { res.statusCode = 403; return res.end(JSON.stringify({ message: "当前账号无权访问该章节。" })); }
      return res.end(JSON.stringify({ node: { ...chapterNode, parsed_document_id: chapterStatus === 409 ? 58 : 57 }, source_text: chapterWithImages }));
    }
    if (url.pathname === "/api/v1/knowledge-resources/upload") {
      const chunks = []; for await (const chunk of req) chunks.push(chunk);
      const payload = await new Response(Buffer.concat(chunks), { headers: { "content-type": req.headers["content-type"] } }).formData();
      const file = payload.get("Files");
      uploads.push({ uri: payload.get("Uri"), process: payload.get("Process"), name: file.name, text: await file.text() });
      res.setHeader("Content-Type", "application/json");
      return res.end(JSON.stringify({ items: [{ status: "imported", file_name: file.name }], warnings: [], tasks: [] }));
    }
    if (url.pathname === "/api/v1/knowledge-resources/file") {
      assert.equal(url.searchParams.get("parsed_document_id"), "57");
      if (url.searchParams.get("uri") === chapterImageUri) {
        if (assetStatus !== 200) { res.statusCode = assetStatus; return res.end(); }
        res.setHeader("Content-Type", "image/png");
        return res.end(Buffer.from(pngBase64, "base64"));
      }
      assert.equal(url.searchParams.get("uri"), chapterUri);
      res.writeHead(chapterStatus, { "Content-Type": "text/markdown; charset=utf-8" });
      return res.end(chapterText);
    }
    if (url.pathname === "/api/v1/knowledge-resources/tree") {
      res.setHeader("Content-Type", "application/json");
      return res.end(JSON.stringify([...directories, { ...chapterNode, uri: chapterNode.uri.replace("parts/", "another/") }, chapterNode]));
    }
    if (url.pathname.endsWith("/download")) {
      if (denyDownload) { res.writeHead(403); return res.end("denied"); }
      res.setHeader("Content-Type", "application/octet-stream"); return res.end(bytes);
    }
    res.setHeader("Content-Type", "application/json");
    return res.end(JSON.stringify(["方案.md", "slides.pptx"].map(name => ({ repository_name: "repo", path: `docs/${name}`, name }))));
  }
  res.setHeader("Content-Type", "text/html; charset=utf-8");
  res.end(`<html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style><link rel="stylesheet" href="/bundle.css"></head><body><div id="root"></div><script src="/bundle.js"></script></body></html>`);
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true, executablePath: process.env.DOCUMENT_CARD_TEST_BROWSER });
  const page = await browser.newPage({ viewport: { width: 1000, height: 800 }, acceptDownloads: true });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  assert.equal(await page.locator("[data-document-card]").count(), 6);
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
  assert.ok(requests.every(url => url.startsWith("/api/v1/code-repositories/projects/7/")));
  const chapterRequestsStart = requests.length;
  const previews = page.getByTitle("预览资料库章节 Cycle time.md");
  const chapterPreview = page.getByRole("region", { name: "资料库章节预览" });
  const closePanel = () => page.getByRole("button", { name: "关闭右侧面板" }).click();
  await previews.first().click();
  const mainHeading = chapterPreview.getByRole("heading", { name: "生产节拍" });
  await mainHeading.waitFor();
  assert.equal(await page.getByRole("dialog").count(), 0);
  const fontSize = element => parseFloat(getComputedStyle(element).fontSize);
  assert.ok(await mainHeading.evaluate(fontSize) > await chapterPreview.getByRole("heading", { name: "计算示例" }).evaluate(fontSize));
  assert.ok(await chapterPreview.getByRole("heading", { name: "计算示例" }).evaluate(fontSize) > await chapterPreview.getByText("资料库章节正文。", { exact: true }).evaluate(fontSize));
  assert.equal(await chapterPreview.getByRole("list").evaluate(element => getComputedStyle(element).listStyleType), "disc");
  await chapterPreview.getByRole("cell", { name: "WIP" }).waitFor();
  await page.waitForFunction(() => document.querySelector('img[alt="解析插图"]')?.naturalWidth === 1);
  await closePanel();
  await chapterPreview.waitFor({ state: "hidden" });
  await previews.nth(1).click();
  await chapterPreview.getByRole("heading", { name: "生产节拍" }).waitFor();
  await closePanel();
  await previews.last().click();
  await chapterPreview.getByRole("heading", { name: "生产节拍" }).waitFor();
  await closePanel();
  const chapterDownloadEvent = page.waitForEvent("download");
  await page.getByRole("button", { name: "下载 Cycle time.md", exact: true }).last().click();
  const chapterDownload = await chapterDownloadEvent;
  assert.equal(chapterDownload.suggestedFilename(), "Cycle time.md");
  const chapterChunks = [];
  for await (const chunk of await chapterDownload.createReadStream()) chapterChunks.push(chunk);
  assert.equal(Buffer.concat(chapterChunks).toString("utf8"), chapterText);
  chapterStatus = 409;
  await previews.first().click();
  await chapterPreview.getByRole("alert").filter({ hasText: "版本或内容已变化" }).waitFor();
  await closePanel();
  chapterStatus = 403;
  await page.getByRole("button", { name: "下载 Cycle time.md", exact: true }).first().click();
  await page.getByRole("alert").filter({ hasText: "当前账号无权访问" }).waitFor();
  const saveChapter = page.getByRole("button", { name: "保存 Cycle time.md 到知识库", exact: true });
  assert.equal(await saveChapter.count(), 3);
  const saveDialog = page.getByRole("dialog", { name: "保存 Markdown 到知识库" });
  await saveChapter.last().click();
  await saveDialog.getByRole("treeitem", { name: "阅读笔记" }).click();
  await saveDialog.getByRole("treeitem", { name: "项目目录" }).waitFor();
  await saveDialog.getByRole("treeitem", { name: "共享书架" }).waitFor();
  assert.equal(await saveDialog.getByRole("treeitem", { name: /其他用户目录|生成摘要/ }).count(), 0);
  await saveDialog.getByRole("button", { name: "保存 Markdown", exact: true }).click();
  await saveDialog.getByRole("alert").filter({ hasText: "当前账号无权访问" }).waitFor();
  assert.equal(uploads.length, 0);
  chapterStatus = 200;
  assetStatus = 404;
  await saveDialog.getByRole("button", { name: "保存 Markdown", exact: true }).click();
  await saveDialog.getByRole("alert").filter({ hasText: "读取章节插图失败" }).waitFor();
  assert.equal(uploads.length, 0);
  assetStatus = 200;
  const assetsBeforeCopy = requests.filter(request => request.includes(encodeURIComponent(chapterImageUri))).length;
  await saveDialog.getByRole("button", { name: "保存 Markdown", exact: true }).click();
  await page.getByRole("status").filter({ hasText: "已保存到知识库" }).waitFor();
  const embedded = `data:image/png;base64,${pngBase64}`;
  assert.deepEqual(uploads, [{ uri: personalTarget, process: "false", name: "Cycle time.md", text: chapterWithImages.replace(chapterImageUri, embedded).replace(chapterImageUri, embedded) }]);
  assert.equal(requests.filter(request => request.includes(encodeURIComponent(chapterImageUri))).length - assetsBeforeCopy, 1);
  await page.setViewportSize({ width: 390, height: 844 });
  for (const card of await page.locator("[data-document-card]").all()) {
    const box = await card.boundingBox(); assert.ok(box.x >= 0 && box.x + box.width <= 390);
  }
  chapterStatus = 200;
  await previews.first().click();
  await chapterPreview.getByRole("heading", { name: "生产节拍" }).waitFor();
  const previewBox = await chapterPreview.boundingBox();
  assert.ok(previewBox.x >= 0 && previewBox.x + previewBox.width <= 390);
  assert.ok(requests.slice(chapterRequestsStart).every(url => url.startsWith("/api/v1/knowledge-resources/") || url === "/api/v1/auth/status"));
  const requestsBeforeReopen = requests.length;
  chapterStatus = 403;
  assetStatus = 404;
  await page.goto(`http://127.0.0.1:${server.address().port}/saved-chapter`);
  const savedChapter = page.getByRole("region", { name: "已保存章节" });
  await savedChapter.getByRole("cell", { name: "WIP" }).waitFor();
  assert.ok(await savedChapter.getByRole("heading", { name: "生产节拍" }).evaluate(fontSize) >
    await savedChapter.getByRole("heading", { name: "计算示例" }).evaluate(fontSize));
  await page.waitForFunction(() => [...document.images].filter(image => image.alt !== "禁止的图片格式").length === 2 &&
    [...document.images].every(image => image.naturalWidth === 1));
  assert.equal(await savedChapter.getByRole("img", { name: "禁止的图片格式" }).count(), 0);
  assert.deepEqual(requests.slice(requestsBeforeReopen), [`/api/v1/knowledge-resources/read?uri=${encodeURIComponent(savedUri)}`]);
  assert.deepEqual(errors, []);
  console.log("PASS: formatted previews, downloads, chapter copy including images, and reopening independently of the source");
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
