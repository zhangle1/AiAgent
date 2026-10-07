// ARCHIFY_TEST_TOOLS=<directory containing playwright and esbuild> node tests/chat-architecture.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.ARCHIFY_TEST_TOOLS ? createRequire(path.resolve(process.env.ARCHIFY_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright"), { build } = tools("esbuild");
const { css } = await require("postcss")([require("tailwindcss")({ content: [path.join(front, "components/chat/visualization/*.tsx")] })]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const graph = { version: 1, title: "订单系统", nodes: [
  { id: "web", label: "前端", group: "应用层", description: "<img src=x onerror=alert(1)>", source: "src/App.tsx:10" },
  { id: "api", label: "服务", group: "服务层" }, { id: "db", label: "数据库", group: "存储层" }, { id: "alone", label: "独立模块", group: "存储层" },
], edges: [{ from: "web", to: "api", label: "请求" }, { from: "api", to: "db", label: "查询" }] };
const bundle = await build({
  stdin: { contents: `import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
    import {MarkdownMessage} from './components/chat/MarkdownMessage';
    import {VisualizationDialog} from './components/chat/visualization/VisualizationDialog';
    import {VisualizationToolbar} from './components/chat/visualization/VisualizationToolbar';
    import {buildVisualizationMessage} from './lib/chat-visualization';
    function App(){const [open,setOpen]=useState(false),[bad,setBad]=useState(false),[scope,setScope]=useState(null);
    return <><button onClick={()=>setOpen(true)}>配置</button><button onClick={()=>setBad(!bad)}>切换无效数据</button>
    <VisualizationToolbar value={null} onChange={(type)=>{document.getElementById('request').textContent=buildVisualizationMessage('分析订单系统',type)}} disabled={false} project={null} commits={[]} onCommitsChange={()=>{}} />
    <pre id="request" hidden />
    <output>{JSON.stringify(scope)}</output>
    <MarkdownMessage content={'\u0060\u0060\u0060aiagent-architecture\\n'+(bad ? '{"version":1' : ${JSON.stringify(JSON.stringify(graph))})+'\\n\u0060\u0060\u0060'}/>
    {open && <VisualizationDialog project={{id:1,repositories:[{name:'repo',display_name:'测试仓库'}]}} value="interactive" commits={[]} scope={scope} onClose={()=>setOpen(false)} onApply={(type,commits,next)=>{setScope(next);setOpen(false)}}/>}</>}
    createRoot(document.getElementById('root')).render(<App/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, jsx: "automatic", tsconfig: path.join(front, "tsconfig.json"), define: { "process.env.NODE_ENV": '"development"' },
  plugins: [{ name: "mock-directory-api", setup(build) {
    build.onResolve({ filter: /^@\/lib\/code-repository-api$/ }, () => ({ path: "api", namespace: "mock" }));
    build.onLoad({ filter: /.*/, namespace: "mock" }, () => ({ contents: `export const resolveProjectCodeFileReference=()=>null;
      export const getProjectGitHistory=async()=>[];
      export const getCodeTree=async(repo,dir)=>{await new Promise(r=>setTimeout(r,30));return dir ? {directories:[], files:[{name:'App.sln',path:'src/App.sln'},{name:'package.json',path:'src/package.json'}]} : {directories:[{name:'src',path:'src'}],files:[]}};`, loader: "js" }));
  } }],
});
const server = http.createServer((request, response) => {
  response.setHeader("Content-Type", request.url === "/bundle.js" ? "text/javascript" : "text/html; charset=utf-8");
  response.end(request.url === "/bundle.js" ? bundle.outputFiles[0].text : `<!doctype html><html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style><div id="root"></div><script src="/bundle.js"></script></html>`);
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1200, height: 950 } });
  const errors = [], external = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("request", (request) => { if (!request.url().startsWith("http://127.0.0.1:") && /^https?:/.test(request.url())) external.push(request.url()); });
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  assert.equal(await page.locator("[data-node-id]").count(), 4);
  await page.getByRole("button", { name: "可视化", exact: true }).click();
  assert.equal(await page.getByRole("button", { name: /交互架构图/ }).getAttribute("aria-pressed"), "true");
  assert.equal(await page.getByText("体验 Archify 交互示例").count(), 0);
  await page.getByRole("button", { name: "使用此配置" }).click();
  assert.match(await page.locator("#request").textContent(), /aiagent-architecture/);
  await page.getByRole("button", { name: "浅色画布", exact: true }).click();
  assert.ok(await page.getByRole("button", { name: "深色画布", exact: true }).isVisible());
  await page.getByRole("button", { name: "深色画布", exact: true }).click();
  await page.getByRole("textbox", { name: "搜索节点" }).fill("独立");
  await page.getByRole("button", { name: "独立模块", exact: true }).click();
  assert.match(await page.locator("aside").innerText(), /独立模块/);
  await page.getByRole("textbox", { name: "搜索节点" }).fill("没有这个节点");
  assert.ok(await page.getByText("没有匹配的节点").isVisible());
  await page.getByRole("textbox", { name: "搜索节点" }).fill("");
  const rect = page.locator('[data-node-id="web"] rect');
  const originalX = Number(await rect.getAttribute("x"));
  const nodeBox = await rect.boundingBox();
  await page.mouse.move(nodeBox.x + 50, nodeBox.y + 30);
  await page.mouse.down();
  await page.mouse.move(nodeBox.x + 105, nodeBox.y + 60, { steps: 6 });
  await page.mouse.up();
  assert.ok(Number(await rect.getAttribute("x")) > originalX + 40);
  await page.getByRole("button", { name: "重置布局", exact: true }).click();
  assert.equal(Number(await rect.getAttribute("x")), originalX);
  await page.getByRole("button", { name: "清除选择", exact: true }).click();
  if (process.env.ARCHITECTURE_SCREENSHOT) await page.screenshot({ path: process.env.ARCHITECTURE_SCREENSHOT });
  await page.getByRole("button", { name: "节点：前端", exact: true }).click();
  assert.match(await page.locator("aside").innerText(), /src\/App.tsx:10/);
  assert.equal(await page.locator("aside img").count(), 0);
  await page.getByRole("button", { name: "下游", exact: true }).click();
  assert.equal(await page.locator('[data-node-id="db"]').getAttribute("opacity"), "1");
  assert.equal(await page.locator('[data-node-id="alone"]').getAttribute("opacity"), "0.25");
  await page.getByRole("button", { name: "路径追踪", exact: true }).click();
  await page.getByRole("button", { name: "节点：数据库", exact: true }).click();
  assert.match(await page.locator('[aria-live="polite"]').innerText(), /前端 → 服务 → 数据库/);
  await page.getByRole("button", { name: "放大", exact: true }).click();
  assert.ok(await page.getByRole("button", { name: "125% · 重置" }).isVisible());
  for (const format of ["JSON", "SVG", "PNG"]) {
    const pending = page.waitForEvent("download");
    await page.getByRole("button", { name: format, exact: true }).click();
    const download = await pending;
    assert.equal(await download.failure(), null);
    const data = readFileSync(await download.path());
    if (format === "JSON") assert.equal(JSON.parse(data).nodes.length, 4);
    if (format === "SVG") assert.match(data.toString(), /<svg/);
    if (format === "PNG") assert.equal(data.subarray(0, 8).toString("hex"), "89504e470d0a1a0a");
  }
  await page.getByRole("button", { name: "展开大图" }).click();
  assert.ok(await page.getByRole("dialog").isVisible());
  await page.keyboard.press("Escape");
  assert.ok(await page.getByRole("button", { name: "展开大图" }).isVisible());
  await page.getByRole("button", { name: "配置", exact: true }).click();
  await page.getByRole("button", { name: "📁 src" }).click();
  await page.getByRole("button", { name: "选择 App.sln" }).click();
  await page.getByRole("button", { name: "使用此配置" }).click();
  assert.equal(JSON.parse(await page.locator("output").innerText()).path, "src/App.sln");
  await page.getByRole("button", { name: "配置", exact: true }).click();
  await page.getByRole("button", { name: "清除代码范围" }).click();
  await page.getByRole("button", { name: "取消", exact: true }).click();
  assert.equal(JSON.parse(await page.locator("output").innerText()).path, "src/App.sln");
  await page.getByRole("button", { name: "切换无效数据" }).click();
  assert.equal(await page.locator("[data-node-id]").count(), 0);
  assert.match(await page.locator('p[role="status"]').innerText(), /JSON/);
  await page.getByRole("button", { name: "切换无效数据" }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  const viewport = page.getByRole("img", { name: "订单系统", exact: true }).locator("..");
  const viewportBox = await viewport.boundingBox();
  await page.mouse.move(viewportBox.x + 280, viewportBox.y + 10);
  await page.mouse.down();
  await page.mouse.move(viewportBox.x + 140, viewportBox.y + 10, { steps: 6 });
  await page.mouse.up();
  assert.ok(await viewport.evaluate((element) => element.scrollLeft) > 100);
  await page.getByRole("button", { name: "展开大图" }).click();
  const box = await page.getByRole("dialog").boundingBox();
  assert.ok(box.x >= 0 && box.width <= 390 && box.y >= 0 && box.y + box.height <= 844);
  await page.getByRole("button", { name: "关闭大图" }).click();
  assert.deepEqual(errors, []); assert.deepEqual(external, []);
  console.log("PASS: interactive default/request, no demo entry, themes, search, node drag/reset, canvas pan, markdown rendering, inert text, graph exploration, exports, scope selection/cancel, invalid streaming data, mobile dialog");
} finally { await browser?.close(); await new Promise((resolve) => server.close(resolve)); }
