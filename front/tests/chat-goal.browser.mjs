// Isolated component browser test with synthetic API responses, no backend/model/user data.
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import http from "node:http";
import { createRequire } from "node:module";
const require = process.env.GOAL_TEST_TOOLS ? createRequire(path.resolve(process.env.GOAL_TEST_TOOLS, "package.json")) : createRequire(import.meta.url);
const { build } = require("esbuild");
const { chromium } = require("playwright");
const front = path.resolve(import.meta.dirname, "..");
const output = await fs.mkdtemp(path.join(os.tmpdir(), "chat-goal-browser-"));
const bundle = await build({
  stdin: { contents: `import React from "react"; import {createRoot} from "react-dom/client"; import {ChatGoalPanel} from "./components/chat/ChatGoalPanel";
    const root=createRoot(document.getElementById('app'));
    let session=null,available=true,activation=0,submission=0;
    window.renderPanel=(sessionId,enabled=true)=>{session=sessionId;available=enabled;root.render(<ChatGoalPanel key={sessionId??'new'} sessionId={sessionId} enabled={enabled} activation={activation} submission={submission} sending={false} onChange={value=>{window.draft=value??null;}}/>);};
    window.consumeGoal=()=>{submission++;window.renderPanel(session,available);};
    window.activateGoal=()=>{activation++;window.renderPanel(session,available);};
    window.renderPanel(null);`, loader: "tsx", resolveDir: front },
  bundle: true, write: false, jsx: "automatic", alias: { "@": front },
});
const server = http.createServer((request, response) => {
  response.setHeader("Content-Type", request.url === "/bundle.js" ? "text/javascript" : "text/html");
  response.end(request.url === "/bundle.js" ? bundle.outputFiles[0].text : '<div id="app"></div><script src="/bundle.js"></script>');
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const browser = await chromium.launch({ headless: true, channel: process.env.CHAT_TEST_BROWSER_CHANNEL ?? "msedge" });
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  let goal = null;
  const controls = [];
  let failDelete=false;
  await page.route("**/api/**", async route => {
    if (route.request().method() === "POST") {
      if(failDelete) return route.fulfill({status:409,json:{message:"模拟目标版本冲突"}});
      controls.push({ url: route.request().url(), body: route.request().postDataJSON() });
      goal = { ...goal, status: "ended", running: false, version: (goal?.version ?? 0) + 1 };
    }
    await route.fulfill({ json: { goal } });
  });
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.waitForFunction(() => window.draft === null);
  assert.equal(await page.getByRole("checkbox").count(), 0);
  assert.equal(await page.getByRole("textbox").count(), 0);
  await page.evaluate(() => window.activateGoal());
  const dialog = page.getByRole("dialog", { name: "设置目标模式" });
  await dialog.waitFor();
  assert.equal(await dialog.getByRole("button", { name: "启用目标" }).isDisabled(), true);
  assert.equal(await page.getByRole("textbox", { name: "目标 Token 预算" }).count(), 0);
  await dialog.getByRole("textbox", { name: "目标与验收标准" }).fill("实现功能并验证测试");
  await page.screenshot({ path: path.join(output, "goal-setup-mobile.png") });
  await dialog.getByRole("button", { name: "启用目标" }).click();
  await page.waitForFunction(() => window.draft?.action === "start" && window.draft.token_budget === 2000000);
  assert.equal(await page.evaluate(() => window.draft.objective), "实现功能并验证测试");
  await page.getByRole("button", { name: "取消目标模式", exact: true }).click();
  await page.waitForFunction(() => window.draft === null);
  await page.evaluate(() => window.activateGoal());
  await dialog.waitFor();
  await page.keyboard.press("Escape");
  await dialog.waitFor({ state: "detached" });
  assert.equal(await page.evaluate(() => window.draft), null);
  await page.evaluate(() => window.renderPanel("existing"));
  await page.waitForFunction(() => window.draft === null);
  goal = { id: "goal", objective: "恢复目标", status: "paused", tokenBudget: null, tokensUsed: 40, timeUsedSeconds: 2, version: 8, stopReason: "连接中断", running: false };
  await page.evaluate(() => window.renderPanel("paused"));
  await page.getByRole("button", { name: /目标.*恢复目标/ }).waitFor();
  await page.evaluate(() => window.activateGoal());
  await dialog.waitFor();
  await dialog.getByRole("button",{name:"继续目标",exact:true}).click();
  await page.waitForFunction(() => window.draft?.action === "resume" && window.draft.expected_version === 8);
  await page.evaluate(() => window.consumeGoal());
  await page.waitForFunction(() => window.draft === null);
  await page.getByRole("button", { name: /目标.*恢复目标/ }).click(); await dialog.waitFor();
  failDelete=true; await dialog.getByRole("button", {name:"删除目标",exact:true}).click();
  await dialog.getByRole("alert").filter({hasText:"模拟目标版本冲突"}).waitFor();
  assert.equal(await dialog.isVisible(),true);
  failDelete=false; await dialog.getByRole("button", {name:"删除目标",exact:true}).click();
  await dialog.waitFor({state:"detached"});
  assert.equal(controls[0].body.expected_version, 8); assert.ok(controls[0].url.endsWith("/end"));
  assert.equal(await page.getByRole("button", {name:/目标.*恢复目标/}).count(),0);
  await page.evaluate(()=>window.activateGoal()); await dialog.waitFor();
  await dialog.getByRole("textbox",{name:"目标与验收标准"}).fill("新目标");
  await dialog.getByRole("button",{name:"启用目标",exact:true}).click();
  await page.waitForFunction(()=>window.draft?.expected_version===9);
  await page.getByRole("button",{name:/目标.*新目标/}).click(); await dialog.waitFor();
  await dialog.getByRole("button",{name:"删除目标",exact:true}).click();
  await dialog.waitFor({state:"detached"}); await page.waitForFunction(()=>window.draft===null);
  assert.equal(controls.length,1,"deleting a local draft needs no server operation");
  goal={id:"completed",objective:"已经完成的目标",status:"complete",tokensUsed:19649,timeUsedSeconds:11,version:10,running:false,stopReason:null,tokenBudget:2000000};
  await page.evaluate(()=>window.renderPanel("completed"));
  await page.waitForTimeout(100);
  assert.equal(await page.getByRole("button",{name:/目标.*已经完成/}).count(),0);
  await page.evaluate(()=>window.activateGoal()); await dialog.waitFor();
  await dialog.getByText("已完成：已经完成的目标",{exact:true}).waitFor();
  await page.screenshot({path:path.join(output,"goal-delete-complete-mobile.png")});
  await dialog.getByRole("button",{name:"删除目标",exact:true}).click(); await dialog.waitFor({state:"detached"});
  assert.equal(controls.at(-1).body.expected_version,10);
  goal={...goal,id:"running",status:"active",running:true,version:20};
  await page.evaluate(()=>window.renderPanel("running"));
  await page.getByRole("button",{name:/目标.*已经完成/}).waitFor();
  await page.evaluate(()=>window.activateGoal()); await dialog.waitFor();
  assert.equal(await dialog.getByRole("button",{name:"删除目标",exact:true}).isDisabled(),true);
  assert.equal(await dialog.getByRole("button",{name:"继续目标",exact:true}).isDisabled(),true);
  await page.keyboard.press("Escape");
  assert.deepEqual(errors, []);
  console.log(`Passed: goal dialog opt-in/cancel, required objective, hidden fixed two-million budget, session reset, one-shot consumption, hidden completed target, versioned resume/delete with error recovery, local draft delete, active delete guard, mobile rendering. Screenshot: ${output}`);
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
