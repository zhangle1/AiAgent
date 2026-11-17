import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";
const front = fileURLToPath(new URL("../", import.meta.url));
const tools = createRequire(path.resolve(process.env.CHAT_LIBRARY_TEST_TOOLS || "../../goal-browser-tmp", "package.json"));
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const bundle = await build({ stdin: { contents: `import React from 'react';import{createRoot}from'react-dom/client';import{ChatPrototypePanel}from'./components/prototype-studio/ChatPrototypePanel';const html='<html><head><title>合成原型</title></head><body><h1>Preview</h1><script>parent.__prototypeEscaped=true</script></body></html>';createRoot(document.getElementById('root')).render(<ChatPrototypePanel projectId={7} messages={[{id:'a',content:'说明\\n\\n\`\`\`html\\n'+html+'\\n\`\`\`\\n\\n结束'}]} onAdjust={text=>document.getElementById('draft').textContent=text}/>);`, resolveDir: front, loader: "tsx" }, bundle: true, write: false, format: "iife", jsx: "automatic", tsconfig: path.join(front, "tsconfig.json"), logLevel: "silent" });
let saved;
const server = http.createServer(async (req, res) => {
  if (req.url === "/bundle.js") { res.setHeader("Content-Type", "text/javascript"); res.end(bundle.outputFiles[0].text); return; }
  if (req.url.startsWith("/api/")) {
    res.setHeader("Content-Type", "application/json");
    if (req.method === "POST") {
      let body = ""; for await (const chunk of req) body += chunk;
      saved = JSON.parse(body); res.end(JSON.stringify({ ...saved, id: 12, created_by_me: true })); return;
    }
    res.end(JSON.stringify({ templates: [ { id: 99, name: "其他项目", project_id: 8, tags: ["prototype-html"] } ] })); return;
  }
  res.end('<html><body><div id="root"></div><pre id="draft"></pre><script src="/bundle.js"></script></body></html>');
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  const errors = []; page.on("pageerror", error => errors.push(error.message));
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.getByRole("option", { name: "合成原型" }).waitFor({ state: "attached" });
  assert.equal(await page.getByRole("option", { name: "其他项目" }).count(), 0);
  await page.getByLabel("选择原型产出").selectOption("a:0");
  assert.equal(await page.locator("iframe").getAttribute("sandbox"), "");
  await page.frameLocator("iframe").getByRole("heading", { name: "Preview" }).waitFor();
  assert.equal(await page.evaluate(() => window.__prototypeEscaped), undefined);
  await page.getByRole("button", { name: "编辑 HTML", exact: true }).click();
  assert.doesNotMatch(await page.getByLabel("HTML 源码").inputValue(), /结束/);
  await page.getByLabel("HTML 源码").fill('<html><body><h1>Changed</h1></body></html>');
  await page.getByLabel("注解与调整要求").fill("按钮改蓝色");
  await page.getByRole("button", { name: "保存原型与注解", exact: true }).click();
  await page.getByRole("status").waitFor();
  assert.equal(saved.description, "按钮改蓝色"); assert.equal(saved.project_id, 7); assert.equal(saved.visibility, "personal");
  await page.getByRole("button", { name: "带入聊天调整原型", exact: true }).click();
  const draft = await page.locator("#draft").textContent();
  assert.match(draft, /Changed/); assert.match(draft, /按钮改蓝色/);
  assert.equal(await page.getByRole("button", { name: /\.psd/ }).count(), 3);
  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: /移动端\.psd/ }).click();
  const download = await downloadPromise;
  const stream = await download.createReadStream();
  const chunks = []; for await (const chunk of stream) chunks.push(chunk);
  const psd = Buffer.concat(chunks);
  assert.equal(psd.subarray(0, 4).toString(), "8BPS");
  assert.equal(psd.readUInt32BE(18), 390);
  assert.deepEqual(errors, []);
  console.log("PASS: chat HTML extraction, project scope, sandbox, source edit, annotation persistence, adjustment draft and PSD entries");
} finally { await browser?.close(); await new Promise(resolve => server.close(resolve)); }
