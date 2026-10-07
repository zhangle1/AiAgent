// Optional browser tools: PACKAGING_TEST_TOOLS=<directory containing node_modules> node tests/chat-packaging.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.PACKAGING_TEST_TOOLS ? createRequire(path.resolve(process.env.PACKAGING_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const { css } = await require("postcss")([require("tailwindcss")({ content: [path.join(front, "components/chat/ChatPackageDialog.tsx"), path.join(front, "components/chat/ChatRuntimeToolbar.tsx")] })]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const bundle = await build({
  stdin: { contents: `import React, {useState} from 'react'; import {createRoot} from 'react-dom/client';
    import {ChatRuntimeToolbar} from './components/chat/ChatRuntimeToolbar';
    function App() { const [id,setId]=useState(7); const [prompt,setPrompt]=useState('');
      const repository={id:1,name:'fixture',display_name:'测试代码库',solution_files:[],configuration_files:[],chat_editable_configuration_files:[]};
      window.switchProject=()=>setId(x=>x+1);
      return <><ChatRuntimeToolbar project={{id,display_name:'测试项目',repositories:[repository]}} rightPanelOpen={false} onToggleRightPanel={()=>{}} onOpenRuntimePanel={()=>{}} onPackagePrompt={setPrompt}/><textarea aria-label="聊天输入" value={prompt} readOnly/></>; }
    createRoot(document.getElementById('root')).render(<App/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, jsx: "automatic", tsconfig: path.join(front, "tsconfig.json"), define: { "process.env.NODE_ENV": '"development"' },
});
let failNext = false;
const requests = [];
const file = (name, prefix = "") => ({ name, path: prefix + name, extension: path.extname(name), size: 10 });
const server = http.createServer((request, response) => {
  const url = new URL(request.url, "http://localhost");
  if (url.pathname === "/bundle.js") { response.setHeader("Content-Type", "text/javascript"); response.end(bundle.outputFiles[0].text); }
  else if (url.pathname.startsWith("/api/")) {
    requests.push({ method: request.method, path: url.pathname, directory: url.searchParams.get("path") });
    response.setHeader("Content-Type", "application/json");
    if (url.pathname.endsWith("/tree")) {
      if (failNext) { failNext = false; response.statusCode = 503; response.end(JSON.stringify({ message: "测试目录读取失败" })); return; }
      const directory = url.searchParams.get("path");
      response.end(JSON.stringify(directory ? { path: directory, directories: [], files: [file("服务.slnx", "src\\"), file("package.json", "src\\")] } : { path: ".", directories: [{ name: "src", path: "src" }], files: [file("A.sln"), file("B.sln"), file("readme.md")] }));
    } else if (url.pathname.includes("code-runtime")) response.end(JSON.stringify({ profiles: [], runs: [] }));
    else response.end(JSON.stringify({ state: "synced", message: "已同步", repositories: [] }));
  } else { response.setHeader("Content-Type", "text/html; charset=utf-8"); response.end(`<html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body><div id="root"></div><script src="/bundle.js"></script></body></html>`); }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  async function open() { await page.getByRole("button", { name: /项目程序运行：|代码已同步：/ }).click(); await page.getByRole("button", { name: "打包", exact: true }).click(); }
  await open();
  const apply = page.getByRole("button", { name: "填入聊天", exact: true });
  assert.equal(await apply.isDisabled(), true);
  await page.getByRole("radio", { name: "B.sln", exact: true }).check();
  await page.getByRole("button", { name: "src", exact: true }).click();
  await page.getByRole("radio", { name: "服务.slnx", exact: true }).check();
  await page.getByLabel("额外打包要求（可选）").fill("Release，win-x64；附部署说明");
  await page.getByText("预览完整打包提示词", { exact: true }).click();
  assert.match(await page.locator("details pre").innerText(), /src\/服务.slnx/);
  await page.getByRole("button", { name: "上一级", exact: true }).click();
  await page.getByRole("radio", { name: "A.sln", exact: true }).waitFor();
  await apply.click();
  const prompt = await page.getByRole("textbox", { name: "聊天输入" }).inputValue();
  assert.match(prompt, /src\/服务.slnx/); assert.match(prompt, /Release，win-x64/); assert.ok(!prompt.includes('"B.sln"'));
  await open();
  assert.equal(await apply.isDisabled(), true);
  await page.getByRole("button", { name: "取消", exact: true }).click();
  assert.equal(await page.getByRole("textbox", { name: "聊天输入" }).inputValue(), prompt);
  failNext = true; await open();
  await page.getByRole("alert").waitFor();
  await page.getByRole("button", { name: "重试", exact: true }).click();
  await page.getByRole("radio", { name: "A.sln", exact: true }).waitFor();
  await page.evaluate(() => window.switchProject());
  await page.getByRole("dialog").waitFor({ state: "detached" });
  await page.setViewportSize({ width: 390, height: 844 }); await open();
  await page.getByRole("button", { name: "src", exact: true }).click();
  await page.getByRole("radio", { name: "package.json", exact: true }).check();
  const bounds = await page.getByRole("dialog").boundingBox();
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390 && bounds.height <= 844);
  await apply.click();
  assert.match(await page.getByRole("textbox", { name: "聊天输入" }).inputValue(), /src\/package.json/);
  await open(); await page.getByRole("checkbox", { name: /由 AI 自动判断/ }).check(); await apply.click();
  assert.ok(!(await page.getByRole("textbox", { name: "聊天输入" }).inputValue()).includes("本次唯一打包入口"));
  await open(); await page.keyboard.press("Escape"); await page.getByRole("dialog").waitFor({ state: "detached" });
  assert.ok(requests.every(request => request.method === "GET"));
  assert.deepEqual(errors, []);
  console.log("PASS: toolbar, nested solution/JSON selection, requirements, cancel, retry, project switch, mobile, automatic mode, Escape; no mutation requests or page errors");
} finally { await browser?.close(); await new Promise(resolve => server.close(resolve)); }
