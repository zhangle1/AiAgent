// CHAT_LIBRARY_TEST_TOOLS=<directory containing playwright and esbuild> node tests/chat-library.browser.mjs
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.CHAT_LIBRARY_TEST_TOOLS ? createRequire(path.resolve(process.env.CHAT_LIBRARY_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const { css } = await require("postcss")([require("tailwindcss")({ content: [path.join(front, "components/chat/ChatLibraryPanel.tsx")] })]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const bundle = await build({
  stdin: { contents: `import React,{useState} from 'react';import{createRoot}from'react-dom/client';import{ChatLibraryPanel}from'./components/chat/ChatLibraryPanel';
    function App(){const[value,setValue]=useState('');const[scope,setScope]=useState(1);return <><button onClick={()=>setScope(s=>s+1)}>切换会话</button><output>{value}</output><main style={{height:720,width:380,display:'flex'}}><ChatLibraryPanel key={scope} projectId={7} onInsert={d=>setValue('引用:'+d.path)} onAttach={async f=>setValue('附件:'+f.name+':'+await f.text())} onPreview={d=>setValue('预览:'+d.path)}/></main></>};createRoot(document.getElementById('root')).render(<App/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, platform: "browser", format: "iife", jsx: "automatic",
  tsconfig: path.join(front, "tsconfig.json"),
});
let denied = false, extra = false, delay = false;
const server = http.createServer((req, res) => {
  const url = new URL(req.url, "http://fixture.test");
  if (url.pathname === "/bundle.js") { res.setHeader("Content-Type", "text/javascript"); return res.end(bundle.outputFiles[0].text); }
  if (url.pathname.endsWith("/content") || url.pathname.endsWith("/download")) {
    if (denied) { res.writeHead(403); return res.end("denied"); }
    res.setHeader("Content-Type", "text/plain");
    if (delay) return setTimeout(() => res.end("original bytes"), 500);
    return res.end("original bytes");
  }
  if (url.pathname.startsWith("/api/")) {
    res.setHeader("Content-Type", "application/json");
    if (url.pathname.endsWith("/mine")) return res.end(JSON.stringify([{id:"u1",file_name:"上传.txt",content_type:"text/plain",kind:"document",created_at:"2026-10-09T10:00:00Z"}]));
    return res.end(JSON.stringify(["原型.html", "代码.ts", "src/template.html", ...(extra ? ["新生成.doc"] : [])].map(name => ({repository_name:"repo",path:name,name:name.split('/').at(-1),source:"repository",updated_at:"2026-10-10T10:00:00Z"}))));
  }
  res.setHeader("Content-Type", "text/html; charset=utf-8");
  res.end(`<html><head><style>${css}</style></head><body><div id="root"></div><script src="/bundle.js"></script></body></html>`);
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: {width: 900, height: 850}, acceptDownloads: true });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.getByRole("heading", {name:"原型.html"}).waitFor();
  assert.equal(await page.locator("article").count(), 2);
  await page.getByRole("button", {name:"我的上传", exact:true}).click();
  assert.equal(await page.locator("article").count(), 1);
  await page.getByRole("button", {name:"关联到聊天"}).click();
  await page.getByText("附件:上传.txt:original bytes", {exact:true}).waitFor();
  await page.getByRole("button", {name:"全部", exact:true}).click();
  const html = page.locator("article").filter({has:page.getByRole("heading", {name:"原型.html"})});
  await html.getByRole("button", {name:"关联到聊天"}).click();
  await page.getByText("引用:原型.html", {exact:true}).waitFor();
  await html.getByRole("button", {name:"预览",exact:true}).click();
  await page.getByText("预览:原型.html", {exact:true}).waitFor();
  const download = page.waitForEvent("download");
  await html.getByRole("button", {name:"下载",exact:true}).click();
  assert.equal((await download).suggestedFilename(), "原型.html");
  denied = true;
  await html.getByRole("button", {name:"下载",exact:true}).click();
  await page.getByRole("alert").filter({hasText:"403"}).waitFor();
  denied = false;
  extra = true;
  await page.getByRole("button", {name:"刷新资料库"}).click();
  await page.getByRole("heading", {name:"新生成.doc"}).waitFor();
  await page.getByRole("textbox", {name:"搜索资料库"}).fill("原型");
  assert.equal(await page.locator("article").count(), 1);
  await page.getByRole("textbox", {name:"搜索资料库"}).fill("上传");
  delay = true;
  await page.getByRole("button", {name:"关联到聊天"}).click();
  await page.getByRole("button", {name:"切换会话"}).click();
  await page.waitForTimeout(700);
  assert.equal(await page.locator("output").textContent(), "预览:原型.html");
  assert.equal(await page.locator("main").evaluate(el => el.scrollWidth <= el.clientWidth), true);
  assert.deepEqual(errors, []);
  console.log("PASS: discovery, filtering, search, original download, 403, preview, both chat associations, refresh and session race");
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
