// Actual Next.js knowledge UI and PDF.js; all APIs, documents and images are synthetic.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdtemp, writeFile } from "node:fs/promises";
import { deflateSync } from "node:zlib";
import os from "node:os";
import path from "node:path";
const require = process.env.KNOWLEDGE_TEST_TOOLS ? createRequire(path.resolve(process.env.KNOWLEDGE_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { chromium } = require("playwright");
const output = await mkdtemp(path.join(os.tmpdir(), "knowledge-pdf-ui-"));
// Generate tiny, valid fixtures without a Python installation or third-party PDF tools.
const jpx = Buffer.from("/0//UQAvAAAAAAAYAAAAGAAAAAAAAAAAAAAAGAAAABgAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAEABAQEAAH/XAAQQEBISFBISFBISFBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAAAAAAAuAAH/k8+0EAiMTufAdCAB18+0FAHU+1R/gICAgICAgICAgICA/9k=", "base64");
const content = ["BT /F1 22 Tf 40 750 Td (Chapter One - Synthetic PDF) Tj ET\nq 290 0 0 140 40 500 cm /Scan Do Q\n", "BT /F1 22 Tf 40 750 Td (Chapter Two) Tj ET\n"];
const objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
  ...[6, 7].map(index => `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> /XObject << /Scan 8 0 R >> >> /Contents ${index} 0 R >>`),
  "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", ...content.map(value => `<< /Length ${value.length} >>\nstream\n${value}endstream`),
  `<< /Type /XObject /Subtype /Image /Width 24 /Height 24 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /JPXDecode /Length ${jpx.length} >>\nstream\n${jpx.toString("latin1")}\nendstream`];
let source = "%PDF-1.7\n"; const offsets = [];
objects.forEach((value, index) => { offsets.push(source.length); source += `${index + 1} 0 obj\n${value}\nendobj\n`; });
const xref = source.length;
source += `xref\n0 9\n0000000000 65535 f \n${offsets.map(offset => `${String(offset).padStart(10, "0")} 00000 n \n`).join("")}trailer\n<< /Size 9 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
const pdf = Buffer.from(source, "latin1");
function chunk(name, data) {
  const type = Buffer.from(name), length = Buffer.alloc(4), checksum = Buffer.alloc(4);
  length.writeUInt32BE(data.length); let crc = 0xffffffff;
  for (const byte of Buffer.concat([type, data])) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = crc & 1 ? 0xedb88320 ^ (crc >>> 1) : crc >>> 1; }
  checksum.writeUInt32BE((crc ^ 0xffffffff) >>> 0);
  return Buffer.concat([length, type, data, checksum]);
}
const header = Buffer.alloc(13); header.writeUInt32BE(290, 0); header.writeUInt32BE(140, 4); header[8] = 8; header[9] = 2;
const pixels = Buffer.alloc(140 * (1 + 290 * 3));
for (let y = 0; y < 140; y++) for (let x = 0; x < 290; x++) { const offset = y * 871 + 1 + x * 3; pixels[offset] = 51; pixels[offset + 1] = 128; pixels[offset + 2] = 230; }
const image = Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk("IHDR", header), chunk("IDAT", deflateSync(pixels)), chunk("IEND", Buffer.alloc(0))]);
await writeFile(path.join(output, "Fixture.pdf"), pdf);
const root = { uri: "viking://resources/", name: "资料", kind: "directory" };
const folder = { uri: root.uri + "Books/", parent_uri: root.uri, name: "Books", kind: "directory" };
const book = { uri: folder.uri + "Fixture.pdf", parent_uri: folder.uri, name: "Fixture.pdf", kind: "file", extension: ".pdf", status: "processed", source_status: "available", size: pdf.length, document_id: 101, parsed_document_id: 1, raw_relative_path: "raw/resources/Books/Fixture.pdf" };
const parsedRoot = { uri: folder.uri + "_parsed/Fixture-101/", parent_uri: folder.uri + "_parsed/", name: "Fixture-101", kind: "directory", source_uri: book.uri, document_id: 101 };
const chapter = { uri: parsedRoot.uri + "chapter.md", parent_uri: parsedRoot.uri, name: "chapter.md", kind: "file", extension: ".md", source_uri: book.uri, document_id: 101, parsed_document_id: 1, section_order: 0 };
const later = { uri: parsedRoot.uri + "later/", parent_uri: parsedRoot.uri, name: "Later Chapter", kind: "directory", section_order: 1 };
const asset = { uri: parsedRoot.uri + "assets/figure.png", parent_uri: parsedRoot.uri + "assets/", name: "figure.png", kind: "file", extension: ".png", source_uri: book.uri, document_id: 101, parsed_document_id: 1 };
const assets = { uri: asset.parent_uri, parent_uri: parsedRoot.uri, name: "assets", kind: "directory" };
const markdown = `# Chapter One\n\nLocal parsing retained text and a figure.\n\n![figure](${asset.uri})`;
let uploaded = false;
const nodes = () => [root, folder, ...(uploaded ? [book, { uri: folder.uri + "_parsed/", parent_uri: folder.uri, name: "解析正文", kind: "directory" }, parsedRoot, later, chapter, assets, asset] : [])];
const browser = await chromium.launch({ headless: true, channel: "msedge" });
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(20000);
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.addInitScript(() => localStorage.setItem("aiagent.frontend-onboarding.completed", "true"));
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url()); const name = url.pathname;
    let body = [];
    if (name.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", user_id: "fixture" };
    else if (name.endsWith("/sessions/list")) body = { sessions: [] };
    else if (name.endsWith("/sessions/project-preferences")) body = { preferences: [] };
    else if (name.endsWith("/sessions/sidebar-preference")) body = { project_sort_mode: "name" };
    else if (name.endsWith("/settings")) body = { ui: { language: "zh-CN" }, catalog: { services: {} } };
    else if (name.endsWith("/knowledge-agent/options")) body = { llm_models: [], default_model_id: null };
    else if (name.endsWith("/knowledge-resources/tree")) body = nodes();
    else if (name.endsWith("/knowledge-resources/task")) body = null;
    else if (name.endsWith("/knowledge-resources/upload")) {
      assert.ok(route.request().postDataBuffer().includes(Buffer.from(folder.uri)));
      uploaded = true;
      body = { items: [{ file_name: book.name, status: "imported", document_id: 101 }], tasks: [], warnings: [] };
    } else if (name.endsWith("/knowledge-resources/read")) {
      const node = nodes().find(n => n.uri === url.searchParams.get("uri")) ?? root;
      body = { node, children: nodes().filter(n => n.parent_uri === node.uri), semantic_status: "missing_or_stale",
        source_text: node.uri === chapter.uri ? markdown : null, parsed_content: node.kind === "file" ? markdown : null,
        parsed_outputs: node.uri === book.uri ? [chapter] : [], parser: "pdfpig-pdfium-layout" };
    } else if (name.endsWith("/knowledge-resources/file")) {
      return route.fulfill({ body: url.searchParams.get("uri") === book.uri ? pdf : image,
        contentType: url.searchParams.get("uri") === book.uri ? "application/pdf" : "image/png" });
    }
    await route.fulfill({ json: body });
  });
  await page.goto(process.env.KNOWLEDGE_TEST_URL ?? "http://127.0.0.1:3782/knowledge");
  await page.getByRole("button", { name: /^Books/ }).first().click().catch(async error => { throw new Error(`${error.message}; url=${page.url()}; errors=${JSON.stringify(errors)}; body=${(await page.locator("body").innerText()).slice(-2200)}`); });
  await page.getByRole("button", { name: "添加资源或导入会话" }).click();
  await page.getByRole("button", { name: "添加资源", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "添加资源", exact: true });
  await dialog.locator('input[type="file"]').setInputFiles(path.join(output, "Fixture.pdf"));
  await dialog.getByRole("button", { name: "开始处理", exact: true }).click();
  await page.getByRole("button", { name: book.name, exact: true }).first().click();
  await page.getByText(book.raw_relative_path, { exact: true }).waitFor();
  const canvas = page.locator(".react-pdf__Page canvas");
  await canvas.waitFor().catch(async error => { throw new Error(`${error.message}; errors=${JSON.stringify(errors)}; body=${(await page.locator("body").innerText()).slice(-2500)}`); });
  await page.waitForFunction(() => {
    const canvas = document.querySelector(".react-pdf__Page canvas");
    if (!canvas || canvas.width <= 100) return false;
    const [red, green, blue, alpha] = canvas.getContext("2d").getImageData(Math.floor(canvas.width * 190 / 595),
      Math.floor(canvas.height * 272 / 842), 1, 1).data;
    return red < 100 && green > 90 && blue > 175 && alpha === 255;
  }, null, { timeout: 6000 }).catch(() => { throw new Error("The PDF canvas loaded but did not decode its JPEG 2000 image."); });
  assert.equal(await page.locator('iframe[title="Fixture.pdf"]').count(), 0);
  await page.screenshot({ path: path.join(output, "pdf-preview-desktop.png") });
  await page.getByRole("button", { name: "下一页", exact: true }).click();
  await page.locator('.react-pdf__Page[data-page-number="2"] canvas').waitFor();
  const tree = page.getByRole("navigation", { name: "知识资料目录" });
  const books = tree.getByRole("button", { name: folder.name, exact: true });
  await books.click();
  await page.getByRole("heading", { name: folder.name, exact: true }).waitFor();
  assert.equal(await tree.getByRole("button", { name: book.name, exact: true }).count(), 0, "Preview completion must not reopen the folder");
  await books.click();
  await tree.getByRole("button", { name: "解析正文", exact: true }).click();
  await tree.getByRole("button", { name: parsedRoot.name, exact: true }).click();
  assert.equal(await tree.getByRole("button", { name: "assets", exact: true }).count(), 0);
  const chapterRows = tree.getByRole("button", { name: /^(?:chapter\.md|Later Chapter)$/ });
  assert.deepEqual(await chapterRows.allTextContents(), [chapter.name, later.name]);
  await tree.getByRole("button", { name: book.name, exact: true }).click();
  await page.getByRole("heading", { name: book.name, exact: true }).waitFor();
  await page.getByRole("button", { name: "解析正文", exact: true }).last().click();
  const figure = page.getByRole("img", { name: "figure", exact: true });
  await figure.waitFor();
  await page.waitForFunction(() => document.querySelector('img[alt="figure"]')?.naturalWidth > 0);
  assert.ok((await figure.getAttribute("src")).startsWith("/api/v1/knowledge-resources/file?"));
  assert.ok((await figure.getAttribute("src")).includes("parsed_document_id=1"));
  await page.screenshot({ path: path.join(output, "pdf-markdown-images-desktop.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  await figure.scrollIntoViewIfNeeded();
  const bounds = await figure.boundingBox();
  assert.ok(bounds.width <= 390);
  await page.screenshot({ path: path.join(output, "pdf-markdown-images-mobile.png") });
  await page.locator("summary").filter({ hasText: "解析章节" }).click();
  await page.getByRole("button", { name: /Fixture-101\/chapter.md/ }).click();
  await page.getByRole("heading", { name: "chapter.md", exact: true }).waitFor();
  await figure.waitFor();
  await page.waitForFunction(() => document.querySelector('img[alt="figure"]')?.naturalWidth > 0);
  assert.deepEqual(errors, []);
  console.log(`PASS: folder collapse after read, chapter order, JPEG 2000 canvas and paging, controlled Markdown image, mobile rendering. Screenshots: ${output}`);
} finally { await browser.close(); }
