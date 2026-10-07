// Optional browser tools: npm install --prefix <tools-dir> playwright esbuild
// ARCHIFY_TEST_TOOLS=<tools-dir> node tests/archify-demo.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.ARCHIFY_TEST_TOOLS
  ? createRequire(path.resolve(process.env.ARCHIFY_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const { css } = await require("postcss")([require("tailwindcss")({
  content: [path.join(front, "components/chat/visualization/*.tsx")],
})]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const bundle = await build({
  stdin: { contents: `import React from 'react'; import {createRoot} from 'react-dom/client';
    import {VisualizationDialog} from './components/chat/visualization/VisualizationDialog';
    createRoot(document.getElementById('root')).render(<VisualizationDialog project={null} value="sequence" commits={[]} onClose={()=>{}} onApply={()=>{}}/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, jsx: "automatic", tsconfig: path.join(front, "tsconfig.json"),
  define: { "process.env.NODE_ENV": '"development"' },
});
// A fixture host for the actual React dialog: no login, backend, or user data.
const server = http.createServer((request, response) => {
  const route = request.url;
  if (route === "/bundle.js") {
    response.setHeader("Content-Type", "text/javascript"); response.end(bundle.outputFiles[0].text);
  } else if (route === "/archify/aiagent-demo.html") {
    response.setHeader("Content-Type", "text/html; charset=utf-8");
    response.end(readFileSync(path.join(front, "public/archify/aiagent-demo.html")));
  } else if (route === "/") {
    response.setHeader("Content-Type", "text/html; charset=utf-8");
    response.end(`<!doctype html><html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style><div id="root"></div><script src="/bundle.js"></script></html>`);
  } else { response.writeHead(404); response.end(); }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = [], external = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
  page.on("request", request => { if (/^https?:/.test(request.url()) && !request.url().startsWith("http://127.0.0.1:")) external.push(request.url()); });
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  await page.getByRole("button", { name: /体验 Archify/ }).click();
  assert.equal(await page.locator("iframe").getAttribute("sandbox"), "allow-scripts allow-downloads");
  const frame = page.frameLocator("iframe");
  assert.equal(await frame.locator("svg [data-node-id]").count(), 7);
  await frame.locator("#node-api").click();
  assert.match(await frame.locator("#focus-chip").innerText(), /API 服务/);
  await frame.locator("#btn-reach-downstream").click();
  assert.equal(await frame.locator("#btn-reach-downstream").getAttribute("aria-pressed"), "true");
  await frame.locator("#btn-focus-clear").click();
  const theme = await frame.locator("html").getAttribute("data-theme");
  await frame.locator("#btn-theme").click();
  assert.notEqual(await frame.locator("html").getAttribute("data-theme"), theme);
  await frame.getByRole("button", { name: "放大", exact: true }).click();
  assert.notEqual(await frame.locator("[data-view-percent]").innerText(), "100%");
  await frame.getByRole("button", { name: /重置图表视图/ }).click();
  await frame.getByRole("button", { name: "追踪有向路径", exact: true }).click();
  await frame.locator("#node-user").click();
  await frame.locator("#node-model").click();
  assert.match(await frame.locator("#route-probe-path").innerText(), /API 服务/);
  await frame.locator("#route-probe-clear").click();
  for (const format of ["svg", "png"]) {
    await frame.locator("#btn-export").click();
    const downloaded = page.waitForEvent("download", { timeout: 15000 });
    await frame.locator(`[data-format="${format}"]`).click();
    const download = await downloaded;
    assert.equal(await download.failure(), null);
    const bytes = readFileSync(await download.path());
    assert.ok(bytes.length > 1000);
    if (format === "png") assert.equal(bytes.subarray(0, 8).toString("hex"), "89504e470d0a1a0a");
    else assert.match(bytes.toString("utf8"), /<svg/);
  }
  if (process.env.ARCHIFY_SCREENSHOT) await page.screenshot({ path: process.env.ARCHIFY_SCREENSHOT });
  await page.getByRole("button", { name: "返回图形选择" }).click();
  assert.equal(await page.getByRole("button", { name: /时序图/ }).getAttribute("aria-pressed"), "true");
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: /体验 Archify/ }).click();
  await frame.locator("#btn-theme").click();
  const bounds = await page.getByRole("dialog").boundingBox();
  assert.ok(bounds.x >= 0 && bounds.width <= 390 && bounds.y >= 0 && bounds.y + bounds.height <= 844);
  assert.ok(await page.getByRole("button", { name: "返回图形选择" }).isVisible());
  await page.getByRole("button", { name: "返回图形选择" }).click();
  assert.deepEqual(errors, []);
  assert.deepEqual(external, []);
  console.log("PASS: actual dialog + sandbox viewer; nodes, reach, path, theme, zoom, SVG/PNG downloads, selection retention, mobile bounds, no external requests or browser errors");
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
