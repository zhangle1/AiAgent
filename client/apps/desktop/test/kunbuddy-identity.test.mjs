import assert from "node:assert/strict";
import test from "node:test";
import { register } from "node:module";

register(new URL("./helpers/ts-import-hooks.mjs", import.meta.url));
register(new URL("./helpers/updater-module-stubs.mjs", import.meta.url));
const { resolveDataDir, applyDevelopmentUserData } = await import("../electron/main/data-paths.ts");
const { AppUpdaterController } = await import("../electron/main/updater.ts");
const { kunBuddyAgentDir } = await import("../../../packages/agent-runtime/src/client-paths.ts");
const { resolve } = await import("node:path");

test("production and development use separate KunBuddy data and Chromium locks", () => {
  const home = resolve("fixture-home");
  assert.equal(resolveDataDir({home, development:false}), resolve(home,".kunbuddy"));
  assert.equal(resolveDataDir({home, development:true}), resolve(home,".kunbuddy-dev"));
  assert.equal(resolveDataDir({home, development:false,override:"fixture-data"}),resolve("fixture-data"));
  const calls=[];
  const app={commandLine:{hasSwitch:()=>false}, getPath:()=>home,setPath:(...args)=>calls.push(args)};
  applyDevelopmentUserData(app,true);
  assert.deepEqual(calls, [["userData",resolve(home,"KunBuddy Dev")]]);
  calls.length=0; app.commandLine.hasSwitch=()=>true;
  applyDevelopmentUserData(app,true); assert.deepEqual(calls,[]);
  app.commandLine.hasSwitch=()=>false;
  applyDevelopmentUserData(app,false,"fixture-data");
  assert.deepEqual(calls,[["userData",resolve("fixture-data/electron")]]);
});

test("agent resources follow the active profile and ignore legacy Pi environment", () => {
  const home=resolve("fixture-home");
  assert.equal(kunBuddyAgentDir({PI_DESKTOP_DATA_DIR:"legacy"},home),resolve(home,".kunbuddy/agent"));
  assert.equal(kunBuddyAgentDir({KUNBUDDY_DEV:"1"},home),resolve(home,".kunbuddy-dev/agent"));
  assert.equal(kunBuddyAgentDir({KUNBUDDY_DATA_DIR:"test-profile"},home),resolve("test-profile/agent"));
});

test("packaged KunBuddy has no update checks or downloads without an explicit feed", async () => {
  const previous=process.env.KUNBUDDY_UPDATE_FEED_URL;
  delete process.env.KUNBUDDY_UPDATE_FEED_URL;
  try {
    const updater=new AppUpdaterController({logger:{app(){}},send(){},currentVersion:"0.20.1",isPackaged:true,platform:"win32"});
    assert.equal(updater.getState().mode,"disabled");
    assert.equal(updater.getState().automaticSupported,false);
    updater.startAutoCheck();
    await assert.rejects(updater.check(),/disabled/);
    updater.dispose();
  } finally {if(previous!==undefined)process.env.KUNBUDDY_UPDATE_FEED_URL=previous;}
});

test("update feed rejects insecure URLs and embedded credentials before networking", () => {
  for(const updateFeedUrl of ["http://updates.example.test/feed","https://name:secret@updates.example.test/feed"]) {
    assert.throws(()=>new AppUpdaterController({logger:{app(){}},send(){},currentVersion:"0.20.1",isPackaged:true,updateFeedUrl}),/HTTPS URL without credentials/);
  }
});
