// RUNTIME_TEST_TOOLS points to an optional directory containing playwright and esbuild.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";
import http from "node:http";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(front, "package.json"));
const tools = process.env.RUNTIME_TEST_TOOLS ? createRequire(path.resolve(process.env.RUNTIME_TEST_TOOLS, "package.json")) : require;
const { chromium } = tools("playwright");
const { build } = tools("esbuild");
const { css } = await require("postcss")([require("tailwindcss")({ content: [path.join(front, "components/chat/ChatRunDialog.tsx"), path.join(front, "components/chat/ChatRuntimeFloat.tsx"), path.join(front, "components/chat/ChatRuntimeToolbar.tsx")] })]).process("@tailwind base; @tailwind components; @tailwind utilities;", { from: undefined });
const bundle = await build({
  stdin: { contents: `import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
    import {ChatRuntimeToolbar} from './components/chat/ChatRuntimeToolbar';
    import {RuntimeTestWindow} from './components/chat/RuntimeTestWindow';
    function App(){const [id,setId]=useState(7);const [prompt,setPrompt]=useState('');window.switchProject=()=>setId(x=>x+1);
      const repositories=['api','web'].map((name,i)=>({id:i+1,name,display_name:name,solution_files:[],configuration_files:[],chat_editable_configuration_files:[]}));
      return <><ChatRuntimeToolbar project={{id,repositories}} rightPanelOpen={false} onToggleRightPanel={()=>{}} onOpenRuntimePanel={()=>{}} onPackagePrompt={setPrompt}/><textarea aria-label="prompt" value={prompt} readOnly/></>}
    createRoot(document.getElementById('root')).render(location.pathname==='/runtime-test'?<RuntimeTestWindow/>:<App/>);`, resolveDir: front, loader: "tsx" },
  bundle: true, write: false, jsx: "automatic", tsconfig: path.join(front, "tsconfig.json"), define: { "process.env.NODE_ENV": '"development"' },
});
const preview = http.createServer((q,r) => { r.setHeader("Content-Type", "text/html"); r.end("<h1>Fixture application</h1>"); });
await new Promise(resolve => preview.listen(0, "127.0.0.1", resolve));
let standaloneRuns = [], invalidJobs = false, stoppedRunPath;
let jobs = [], visits = 0, stops = 0, prepared, failStop = false;
const id = "a".repeat(32);
const server = http.createServer(async (q,r) => {
  const url = new URL(q.url, "http://localhost");
  if (url.pathname === "/bundle.js") { r.setHeader("Content-Type", "text/javascript"); r.end(bundle.outputFiles[0].text); return; }
  if (url.pathname.startsWith("/api/")) {
    r.setHeader("Content-Type", "application/json");
    let value = {};
    if (url.pathname.endsWith("/tree")) value = { directories: [{name:"src",path:"src"}], files: [{name:"package.json",path:"src/package.json"},{name:"Api.csproj",path:"src/Api.csproj"}] };
    else if (url.pathname.endsWith("/chat-runs")) {
      if (q.method === "POST") {
        let body=""; for await (const part of q) body+=part; prepared=JSON.parse(body);
        const job={request_id:id,project_id:7,manifest_repository:"api",manifest_path:`artifacts/aiagent-runs/${id}.json`,status:"waiting",idle_minutes:prepared.idle_minutes,runs:[],targets:[]};
        jobs=[job]; value=job;
      } else value=invalidJobs ? {unexpected:true} : jobs;
    } else if (url.pathname.endsWith("/visit")) { visits++; value={ok:true}; }
    else if (url.pathname.includes("/runs/") && url.pathname.endsWith("/stop")) { stoppedRunPath=url.pathname; standaloneRuns=standaloneRuns.map(run=>({...run,status:"stopped"})); value={ok:true}; }
    else if (url.pathname.endsWith("/stop")) {
      stops++;
      jobs=jobs.map(job=>({...job,status:failStop?"failed":"stopped",message:failStop?"关闭未完成：端口仍在运行，请重试。":"整组服务已关闭。",runs:job.runs.map(run=>({...run,status:failStop?"stopping":"stopped"}))}));
      if(failStop) { r.statusCode=409; value={message:jobs[0].message}; } else value={ok:true};
    }
    else if (url.pathname.includes("code-runtime")) value={profiles:[],runs:[...standaloneRuns,...jobs.flatMap(job=>job.runs)]};
    else value={state:"synced",message:"synced",repositories:[]};
    r.end(JSON.stringify(value)); return;
  }
  r.setHeader("Content-Type", "text/html; charset=utf-8"); r.end(`<html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body><div id="root"></div><script src="/bundle.js"></script></body></html>`);
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({headless:true});
  const page = await browser.newPage({viewport:{width:1280,height:900}});
  const errors=[]; page.on("pageerror",e=>errors.push(e.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  const open = async () => { await page.getByRole("button",{name:/项目程序运行：|代码已同步：/}).click(); await page.getByRole("button",{name:"配置 AI 运行 · 多选工程"}).click(); };
  await open();
  await page.getByRole("checkbox",{name:"web",exact:true}).check();
  await page.getByRole("checkbox",{name:"Api.csproj",exact:true}).check();
  await page.getByLabel("浏览代码库").selectOption("web");
  await page.getByRole("checkbox",{name:"package.json",exact:true}).check();
  await page.getByLabel("测试页面路径").fill("/login");
  await page.getByLabel("补充运行与页面配置要求").fill("connect API before frontend");
  await page.getByRole("button",{name:"填入聊天并准备运行"}).click();
  await page.getByRole("dialog").waitFor({state:"hidden"});
  assert.deepEqual(prepared.selections,[{repository_name:"api",entry_paths:["src/Api.csproj"]},{repository_name:"web",entry_paths:["src/package.json"]}]);
  const prompt=await page.getByLabel("prompt").inputValue();
  assert.ok(prompt.includes("/login")&&prompt.includes("connect API before frontend")&&prompt.includes(`${server.address().port}`));
  await page.getByRole("button",{name:"AI 进程浮窗"}).click();
  await page.getByText("等待 AI 清单",{exact:true}).waitFor();
  assert.equal(visits,0,"opening float must not keep idle jobs alive");
  jobs=[{...jobs[0],status:"running",message:"ready",runs:[{run_id:"run1",project_id:7,repository_name:"web",entry_path:"src/package.json",role:"frontend",port:preview.address().port,process_id:12345,status:"running"}],targets:[{repository_name:"web",entry_path:"src/package.json",page_path:"/login"}]}];
  await page.getByRole("button",{name:"刷新运行进程"}).click();
  await page.getByText(/PID 12345/).waitFor();
  assert.equal(await page.getByText(/PID 12345/).count(),1,"grouped processes must not be duplicated");
  const popupPromise=page.waitForEvent("popup");
  await page.getByRole("link",{name:"新窗口测试"}).click();
  const popup=await popupPromise; popup.on("pageerror",e=>errors.push(e.message));
  await popup.getByText("Fixture application").waitFor();
  assert.equal(popup.url(),`http://127.0.0.1:${preview.address().port}/login`);
  await popup.close();
  const redirect=await browser.newPage();
  await redirect.goto(`http://127.0.0.1:${server.address().port}/runtime-test?project_id=7&request_id=${id}`);
  await redirect.getByText("Fixture application").waitFor();
  assert.equal(redirect.url(),`http://127.0.0.1:${preview.address().port}/login`);
  await redirect.close();
  const managerPromise=page.waitForEvent("popup");
  await page.getByRole("link",{name:"管理 / 续期"}).click();
  const manager=await managerPromise;
  manager.on("pageerror",e=>errors.push(e.message));
  await manager.frameLocator('iframe[title="项目测试页面"]').getByText("Fixture application").waitFor();
  assert.ok(visits>0);
  await manager.getByLabel("页面路径").fill("/settings");
  await manager.getByRole("button",{name:"打开页面"}).click();
  assert.ok((await manager.locator("iframe").getAttribute("src")).endsWith("/settings"));
  failStop=true;
  await manager.getByRole("button",{name:"关闭整组服务",exact:true}).click();
  await manager.getByRole("alert").filter({hasText:"关闭未完成"}).waitFor();
  assert.ok(await manager.getByRole("button",{name:"关闭整组服务",exact:true}).isEnabled());
  await manager.close();
  await page.getByRole("button",{name:"刷新运行进程"}).click();
  await page.getByText("运行失败",{exact:true}).waitFor();
  failStop=false;
  await page.getByRole("button",{name:"关闭整组",exact:true}).click();
  await page.getByText("已关闭",{exact:true}).waitFor();
  assert.equal(stops,2);
  standaloneRuns=[{run_id:"other",repository_name:"other-app",entry_path:"package.json",role:"frontend",port:preview.address().port,process_id:54321,status:"stopping"}];
  jobs=[];
  await page.getByRole("button",{name:"刷新运行进程"}).click();
  await page.getByText(/PID 54321/).waitFor();
  await page.getByRole("button",{name:"结束对应进程"}).click();
  await page.getByText(/PID 54321.*stopped/).waitFor();
  assert.equal(stops,2,"individual stop must not call group stop");
  assert.equal(stoppedRunPath,"/api/v1/code-runtime/projects/7/runs/other/stop");
  invalidJobs=true; standaloneRuns=[];
  await page.getByRole("button",{name:"刷新运行进程"}).click();
  await page.getByRole("alert").filter({hasText:"无效的进程列表"}).waitFor();
  assert.equal(await page.getByText(/当前项目暂无托管运行进程/).count(),0);
  invalidJobs=false;
  await page.getByRole("button",{name:"收起运行进程"}).click();
  await page.setViewportSize({width:390,height:844});
  await open();
  assert.ok(await page.getByRole("dialog").isVisible());
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.evaluate(()=>window.switchProject());
  await page.getByRole("dialog").waitFor({state:"hidden"});
  assert.deepEqual(errors,[]);
  console.log("Browser passed: selection, direct application port, legacy link redirect, management renewal, deduplication, group and individual stop, invalid response, mobile, project switch.");
} finally { await browser?.close(); await Promise.all([new Promise(resolve=>server.close(resolve)),new Promise(resolve=>preview.close(resolve))]); }
