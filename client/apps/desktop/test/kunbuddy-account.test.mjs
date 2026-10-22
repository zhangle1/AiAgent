import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
register(new URL("./helpers/ts-import-hooks.mjs", import.meta.url));
const { KunBuddyAccount } = await import("../electron/main/kunbuddy-account.ts");
const provider = { id: "subscription", enabled: true, authKind: "kunbuddy_subscription", oauthAccountLabel: "fixture-user", baseUrl: "https://fixture.test/prefix/api/v1/kunbuddy/profiles/profile" };
function fixture() {
  const state = { providers: [provider], token: "fixture-short-token", status: 200, calls: 0 };
  const host = { async call(method) { if(method === "providers.list")return {providers:state.providers}; if(method === "providers.getSecret")return {value:state.token}; throw Error(method); } };
  const fetch = async (url,init) => { state.calls++; assert.equal(url,"https://fixture.test/prefix/api/v1/kunbuddy/models");assert.equal(init.headers.Authorization,"Bearer fixture-short-token");assert.equal(init.redirect,"error");if(state.status===0)throw Error("network");return new Response(JSON.stringify({profiles:[]}),{status:state.status}); };
  return {state,host,fetch};
}
test("fresh and ordinary-key-only installs require homepage login",async()=>{
 const f=fixture();f.state.providers=[];const account=new KunBuddyAccount(()=>f.host,undefined,f.fetch);
 assert.equal((await account.status()).state,"signed_out");assert.equal(f.state.calls,0);
 f.state.providers=[{...provider,authKind:"api_key"}];assert.equal((await account.status(true)).state,"signed_out");
});
test("existing subscription is restored using the scoped catalog without any model call",async()=>{
 const f=fixture(),account=new KunBuddyAccount(()=>f.host,undefined,f.fetch);
 const status=await account.status();assert.equal(status.state,"authenticated");assert.equal(status.baseUrl,"https://fixture.test/prefix");
 assert(!JSON.stringify(status).includes(f.state.token));await account.status();assert.equal(f.state.calls,1);
});
test("only platform unauthorized/missing credentials expire an account; outage and server errors do not",async()=>{
 const f=fixture(),account=new KunBuddyAccount(()=>f.host,undefined,f.fetch);
 f.state.status=401;assert.equal((await account.status(true)).state,"expired");
 for(const code of [0,403,500]){f.state.status=code;assert.equal((await account.status(true)).state,"unavailable");}
 f.state.token=undefined;assert.equal((await account.status(true)).state,"expired");
});
test("saved expiry overrides cached success and never needs a generation request",async()=>{
 const f=fixture(),account=new KunBuddyAccount(()=>f.host,undefined,f.fetch);
 await account.connected({baseUrl:"https://fixture.test/prefix",username:"fixture-user",expiresAt:new Date(Date.now()-1000).toISOString()});
 assert.equal((await account.status()).state,"expired");assert.equal(f.state.calls,0);
});
test("login hints persist and restore without credentials, logout stays logged out across restart",async()=>{
 const dir=await mkdtemp(join(tmpdir(),"kunbuddy-account-"));
 try{
 const f=fixture(),file=join(dir,"account.json"),account=new KunBuddyAccount(()=>f.host,file,f.fetch);
 await account.connected({baseUrl:"https://fixture.test/prefix",username:"fixture-user",expiresAt:new Date(Date.now()+3600000).toISOString()});
 const raw=await readFile(file,"utf8");assert(!raw.includes(f.state.token));assert(!raw.includes("password"));
 const restored=new KunBuddyAccount(()=>f.host,file,f.fetch);assert.equal((await restored.status()).state,"authenticated");
 await restored.disconnected();const loggedOut=await new KunBuddyAccount(()=>f.host,file,f.fetch).status();assert.equal(loggedOut.state,"signed_out");assert.equal(loggedOut.username,"fixture-user");
 await account.connected({baseUrl:"https://fixture.test/prefix",username:"fixture-user",expiresAt:"invalid"});assert.equal((await account.status(true)).expiresAt,undefined);
 }finally{await rm(dir,{recursive:true,force:true});}
});
test("an unrelated still-valid account cannot bypass expiry of the active login",async()=>{
 const f=fixture(),account=new KunBuddyAccount(()=>f.host,undefined,f.fetch);
 await account.connected({baseUrl:"https://different.test",username:"different"});
 assert.equal((await account.status()).state,"signed_out");assert.equal(f.state.calls,0);
});
