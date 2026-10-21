import assert from "node:assert/strict";
import { register } from "node:module";
import test from "node:test";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
register(new URL("./helpers/ts-import-hooks.mjs", import.meta.url));
const { conversationEntries, KunBuddySessionSync } = await import("../electron/main/kunbuddy-session-sync.ts");
const message = (id, content, extra={}) => ({ id, role:"assistant", content, createdAt:"2026-10-21T01:00:00Z", status:"complete", ...extra });

test("Markdown export preserves original Unicode, tools and revisions without hidden prompts or attachment bytes", () => {
  const output = conversationEntries([message("1","原始 🟦",{thinking:"已有推理",toolName:"read",toolArgs:{path:"a"},toolResult:"```danger```",attachments:[{name:"图片.png",kind:"image",data:"PRIVATE-BINARY"}]}),message("2","hidden model prompt",{modelSystem:{}}),message("3","incomplete",{status:"streaming"})]);
  const markdown=output.map(item=>item.markdown).join("");
  for(const text of ["原始 🟦","已有推理","工具参数","工具结果","图片.png"])assert(markdown.includes(text));
  assert(!markdown.includes("PRIVATE-BINARY"));assert(!markdown.includes("hidden model prompt"));assert(!markdown.includes("incomplete"));
});

test("large original messages are split losslessly at Unicode boundaries", () => {
  const text="长🟦".repeat(60000);const entries=conversationEntries([message("big",text)]);
  assert(entries.length>1);assert(entries.every(entry=>Buffer.byteLength(entry.markdown)<=256*1024));
  const joined=entries.map(entry=>entry.markdown).join("");assert(joined.includes(text));assert(!joined.includes("\ufffd"));
});

async function fixture(t, {status=200}={}) {
  const directory=await mkdtemp(join(tmpdir(),"kunbuddy-sync-"));
  const messages=[message("one","first",{role:"user"}),message("two","reply")];
  const providers=[{id:"provider",authKind:"kunbuddy_subscription",oauthAccountLabel:"alice",baseUrl:"https://fixture.test/api/v1/kunbuddy/profiles/1"}];
  const calls=[];const logs=[];let responseStatus=status;let sessionProvider="provider";let badReceipt=false;
  const host={async call(method,input){
    if(method==="session.get")return{session:{id:"session",title:"会话",providerId:sessionProvider,messages:messages.slice(),hasMoreBefore:false}};
    if(method==="session.list")return{sessions:[{id:"session",providerId:sessionProvider}]};
    if(method==="providers.list")return{providers:structuredClone(providers)};
    if(method==="providers.getSecret")return{value:"fixture-short-token"};
    throw Error("unexpected host call "+method);
  }};
  const fetcher=async(url,input)=>{calls.push({url,body:JSON.parse(input.body),headers:input.headers});return new Response(badReceipt?"<html>not an upload receipt</html>":JSON.stringify({document_id:1,uri:'viking://user/alice/sessions/'+"a".repeat(64)+'.md',sha256:"b".repeat(64),appended:0}),{status:responseStatus});};
  let sync=new KunBuddySessionSync(directory,()=>host,(...args)=>logs.push(args),fetcher);
  t.after(async()=>{await sync.close();await rm(directory,{recursive:true,force:true});});
  return{directory,messages,providers,calls,logs,host,fetcher,get sync(){return sync},setStatus(value){responseStatus=value},setBadReceipt(value){badReceipt=value},setSessionProvider(value){sessionProvider=value},async restart(){await sync.close();sync=new KunBuddySessionSync(directory,()=>host,(...args)=>logs.push(args),fetcher)}};
}

test("only new or changed entries upload; failed requests retry and successful entries are acknowledged", async t=>{
  const f=await fixture(t,{status:503});await f.sync.mark("session");await f.sync.flush();
  assert.equal((await f.sync.status()).pending,1);assert.equal(f.calls.length,1);
  f.setStatus(200);await f.sync.flush();assert.equal((await f.sync.status()).pending,0);
  assert.equal(f.calls[1].body.account,"alice");assert.equal(f.calls[1].body.entries.length,2);
  await f.sync.mark("session");await f.sync.flush();assert.equal(f.calls.length,2);
  f.messages.push(message("three","next"));await f.sync.mark("session");await f.sync.flush();
  assert.equal(f.calls.at(-1).body.entries.length,1);
  assert.equal(f.calls.at(-1).headers.Authorization,"Bearer fixture-short-token");
  const state=await readFile(join(f.directory,"kunbuddy-session-sync.json"),"utf8");
  assert(!state.includes("fixture-short-token"));assert(!state.includes('"content"'));assert(!state.includes('"markdown"'));
});

test("pending conversation uploads survive restart and auth expiry without dropping original material", async t=>{
  const f=await fixture(t,{status:401});await f.sync.mark("session");await f.sync.flush();
  assert.equal((await f.sync.status()).sessions[0].error,"login_required");
  const clientId=f.calls[0].body.client_id;
  await f.restart();f.setStatus(200);await f.sync.flush();
  assert.equal((await f.sync.status()).pending,0);assert.equal(f.calls.at(-1).body.client_id,clientId);
  assert.equal(f.calls.at(-1).body.entries.length,2);
});

test("ordinary API providers do not upload and account changes cannot reassign an existing conversation", async t=>{
  const f=await fixture(t);f.providers[0].authKind="api_key";await f.sync.mark("session");await f.sync.flush();assert.equal(f.calls.length,0);
  f.providers[0].authKind="kunbuddy_subscription";await f.sync.mark("session");await f.sync.flush();assert.equal(f.calls.length,1);
  f.providers[0].oauthAccountLabel="bob";f.messages.push(message("bob","other account"));await f.sync.mark("session");await f.sync.flush();
  assert.equal(f.calls.length,1);assert.equal((await f.sync.status()).sessions[0].error,"account_changed");
});

test("history paging sends every original message in chronological order", async t=>{
  const f=await fixture(t);const source=Array.from({length:450},(_,i)=>message("m"+i,String(i)));
  f.host.call=async(method,input)=>{
    if(method==="providers.list")return{providers:f.providers};if(method==="providers.getSecret")return{value:"fixture-short-token"};
    if(method==="session.list")return{sessions:[{id:"session",providerId:"provider"}]};
    const end=input.messageBefore??source.length,start=Math.max(0,end-input.messageLimit);
    return{session:{id:"session",title:"Paged",providerId:"provider",messages:source.slice(start,end),messageStart:start,hasMoreBefore:start>0}};
  };
  await f.sync.mark("session");await f.sync.flush();
  const entries=f.calls.flatMap(call=>call.body.entries);assert.equal(entries.length,450);assert.equal(entries[0].id,"m0:0");assert.equal(entries.at(-1).id,"m449:0");
});

test("the persistence hook runs after Rust accepts the canonical message", async t=>{
  const { PersistenceOutbox }=await import("../electron/main/persistence-outbox.ts");
  const directory=await mkdtemp(join(tmpdir(),"kunbuddy-persist-hook-"));
  t.after(()=>rm(directory,{recursive:true,force:true}));
  const events=[];const host={isAvailable:()=>true,async call(method){events.push(method);}};
  const outbox=new PersistenceOutbox(directory,()=>{}, {onMessagePersisted:async id=>{events.push("sync:"+id)}});
  await outbox.enqueue({key:"one",sessionId:"session",message:message("one","original")},()=>host);
  await outbox.flush(()=>host);
  assert.deepEqual(events,["session.appendMessage","sync:session"]);
});

test("JSON-escaped control text stays within the request byte budget without truncation", async t=>{
  const f=await fixture(t);const original="\u0000".repeat(900000);
  f.messages.splice(0,f.messages.length,message("controls",original));
  await f.sync.mark("session");await f.sync.flush();
  assert(f.calls.length>1);
  assert(f.calls.every(call=>Buffer.byteLength(JSON.stringify(call.body))<2*1024*1024));
  assert(f.calls.flatMap(call=>call.body.entries).map(entry=>entry.markdown).join("").includes(original));
});

test("startup and login refresh backfill pre-existing subscription conversations without a new chat turn", async t=>{
  const f=await fixture(t);await f.sync.flush();
  assert.equal(f.calls.length,1);assert.equal(f.calls[0].body.entries.length,2);
  await f.sync.refresh();assert.equal(f.calls.length,1);
});

test("an HTTP 200 page without a valid upload receipt never acknowledges or drops original messages", async t=>{
  const f=await fixture(t);f.setBadReceipt(true);await f.sync.flush();
  assert.equal((await f.sync.status()).pending,1);assert.equal((await f.sync.status()).sessions[0].error,"upload_receipt_invalid");
  f.setBadReceipt(false);await f.sync.flush();assert.equal((await f.sync.status()).pending,0);assert.equal(f.calls.at(-1).body.entries.length,2);
});
