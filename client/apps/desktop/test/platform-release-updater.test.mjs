import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
register(new URL("./helpers/ts-import-hooks.mjs", import.meta.url));
const { PlatformReleaseUpdater, newerVersion } = await import("../electron/main/platform-release-updater.ts");
const bytes = Buffer.alloc(128,7), sha = createHash("sha256").update(bytes).digest("hex");
const release = () => ({ id:"a".repeat(32), version:"0.21.0", platform:"windows", architecture:"x64", filename:"KunBuddy.exe", size:128, sha256:sha, notes:"fixture notes", download_path:"https://evil.test/file" });
const account = (site="a") => ({state:"authenticated",baseUrl:`https://${site}.test/prefix`,username:"fixture-user"});
async function harness(t, custom={}) {
 const directory=await mkdtemp(join(tmpdir(),"platform-updater-"));const calls=[],installed=[],states=[];let row=release();
 const host={async call(method,input){if(method==="providers.list")return{providers:["a","b"].map(site=>({id:site,authKind:"kunbuddy_subscription",enabled:true,oauthAccountLabel:"fixture-user",baseUrl:`https://${site}.test/prefix/api/v1/kunbuddy/profiles/profile`}))};if(method==="providers.getSecret")return{value:`fixture-token-${input.id}`};throw Error(method);}};
 const fallback=async(url,init)=>{calls.push({url,authorization:init.headers.Authorization});assert.equal(init.redirect,"error");assert.equal(init.headers.Authorization,`Bearer fixture-token-${new URL(url).hostname[0]}`);return url.includes('/latest?')?Response.json({release:row}):new Response(bytes);};
 const updater=new PlatformReleaseUpdater({currentVersion:"0.20.1",directory,getHost:()=>host,send:s=>states.push({...s}),install:async file=>installed.push(file),canInstall:true,platform:"win32",architecture:"x64",fetch:custom.fetch?((u,i)=>custom.fetch(u,i,fallback)):fallback,...custom.options});
 t.after(async()=>{updater.dispose();await rm(directory,{recursive:true,force:true});});
 return {updater,calls,installed,states,directory,setRow(value){row=value}};
}
test("stable versions compare numerically and never offer downgrades",()=>{
 assert(newerVersion("0.10.0","0.9.0"));assert(!newerVersion("0.9.0","0.10.0"));assert(!newerVersion("0.20.1","0.20.1"));assert(newerVersion("1.0.0","1.0.0-rc.1"));assert(!newerVersion("bad","0.1.0"));
});
test("only a valid account enables current-platform checks with same-origin download paths",async t=>{
 const f=await harness(t);assert.equal(f.updater.getState().mode,"disabled");f.updater.setAccount(account());await f.updater.check();
 assert.equal(f.updater.getState().availableVersion,"0.21.0");await f.updater.download();assert.equal(f.updater.getState().status,"downloaded");
 assert(!f.calls.some(x=>x.url.includes("evil")));assert(!JSON.stringify(f.states).includes("fixture-token"));await f.updater.install();assert.equal(f.installed.length,1);
});
test("switching URL replaces cached update authority even for identical versions and IDs",async t=>{
 const f=await harness(t);f.updater.setAccount(account());await f.updater.check();await f.updater.download();const source=f.updater.getState().sourceKey;
 f.updater.setAccount(account("b"));await f.updater.check();assert.notEqual(f.updater.getState().sourceKey,source);assert.equal(f.updater.getState().status,"available");
 await assert.rejects(()=>f.updater.install());assert.equal(f.installed.length,0);await f.updater.download();await f.updater.install();assert.equal(f.installed.length,1);
});
test("switching during a download aborts the old stream and ignores its late state",async t=>{
 let started;const ready=new Promise(r=>started=r);let aborted=false;
 const f=await harness(t,{fetch:async(url,init,fallback)=>{
  if(url.startsWith('https://a.test')&&url.endsWith('/file'))return new Response(new ReadableStream({start(controller){controller.enqueue(bytes.subarray(0,64));started();init.signal.addEventListener('abort',()=>{aborted=true;controller.error(Error('aborted'));},{once:true});}}));return fallback(url,init);
 }});
 f.updater.setAccount(account());await f.updater.check();const download=f.updater.download();await ready;f.updater.setAccount(account('b'));await f.updater.check();await download;
 assert(aborted);assert.equal(f.updater.getState().status,'available');assert.equal(f.updater.getState().releasesUrl,'https://b.test/prefix');assert.equal(f.installed.length,0);
});
test("hash mismatch never becomes installable",async t=>{
 const f=await harness(t);f.setRow({...release(),sha256:'f'.repeat(64)});f.updater.setAccount(account());await f.updater.check();await f.updater.download();
 assert.equal(f.updater.getState().status,'error');await assert.rejects(()=>f.updater.install());assert.equal(f.installed.length,0);
});
test("installer is rehashed and release withdrawal is rechecked immediately before launch",async t=>{
 const f=await harness(t);f.updater.setAccount(account());await f.updater.check();await f.updater.download();
 await writeFile(join(f.directory,f.updater.getState().sourceKey,'a'.repeat(32)+'-'+sha+'.exe'),Buffer.alloc(128,8));await assert.rejects(()=>f.updater.install());assert.equal(f.installed.length,0);
 await f.updater.check();await f.updater.download();f.setRow(null);await assert.rejects(()=>f.updater.install());assert.equal(f.installed.length,0);
});
test("logout/expiry invalidates downloads and offline status preserves the same unexpired source",async t=>{
 const f=await harness(t);f.updater.setAccount(account());await f.updater.check();const source=f.updater.getState().sourceKey;
 f.updater.setAccount({...account(),state:'unavailable'});assert.equal(f.updater.getState().sourceKey,source);
 await f.updater.download();f.updater.setAccount({...account(),state:'expired'});assert.equal(f.updater.getState().mode,'disabled');await assert.rejects(()=>f.updater.install());assert.equal(f.installed.length,0);
});
test("development installs may verify downloads but never execute installers",async t=>{
 const f=await harness(t,{options:{canInstall:false}});f.updater.setAccount(account());await f.updater.check();await f.updater.download();assert.equal(f.updater.getState().status,'downloaded');await assert.rejects(()=>f.updater.install());assert.equal(f.installed.length,0);
});
test("macOS arm64 downloads only its platform DMG and opens the validated container",async t=>{
 const f=await harness(t,{options:{platform:'darwin',architecture:'arm64'}});
 f.setRow({...release(),platform:'macos',architecture:'arm64',filename:'KunBuddy.dmg'});
 f.updater.setAccount(account());await f.updater.check();await f.updater.download();
 assert.equal(f.updater.getState().status,'downloaded');await f.updater.install();
 assert(f.installed[0].endsWith('.dmg'));assert(f.calls[0].url.includes('platform=macos&architecture=arm64'));
});
test("wrong-platform/architecture metadata and malformed versions are rejected",async t=>{
 const f=await harness(t);for(const patch of [{platform:'macos'},{architecture:'arm64'},{version:'../1'},{size:3*1024**3},{filename:'../file.exe'}]){f.setRow({...release(),...patch});f.updater.setAccount(account());await f.updater.check();assert.equal(f.updater.getState().status,'error');}
 assert.equal(f.installed.length,0);
});
