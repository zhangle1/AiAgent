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
    window.renderPanel=(sessionId,enabled=true)=>root.render(<ChatGoalPanel sessionId={sessionId} enabled={enabled} sending={false} onChange={value=>{window.draft=value??null;}}/>);
    window.renderPanel(null);`, loader: "tsx", resolveDir: front },
  bundle: true, write: false, jsx: "automatic", alias: { "@": front },
});
const server = http.createServer((request, response) => {
  response.setHeader("Content-Type", request.url === "/bundle.js" ? "text/javascript" : "text/html");
  response.end(request.url === "/bundle.js" ? bundle.outputFiles[0].text : '<div id="app"></div><script src="/bundle.js"></script>');
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  let goal = null;
  const controls = [];
  await page.route("**/api/**", async route => {
    if (route.request().method() === "POST") {
      controls.push({ url: route.request().url(), body: route.request().postDataJSON() });
      goal = { ...goal, status: "ended", running: false, version: 9 };
    }
    await route.fulfill({ json: { goal } });
  });
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.getByRole("checkbox").check();
  await page.waitForFunction(() => window.draft?.action === "start");
  assert.equal(await page.evaluate(() => window.draft.objective), "");
  await page.getByRole("textbox", { name: "目标与验收标准" }).fill("实现功能并验证测试");
  await page.getByRole("textbox", { name: "目标 Token 预算" }).fill("-1");
  await page.waitForFunction(() => window.draft?.token_budget === 0);
  await page.getByRole("textbox", { name: "目标 Token 预算" }).fill("5000");
  await page.waitForFunction(() => window.draft?.token_budget === 5000);
  await page.getByRole("textbox", { name: "目标 Token 预算" }).fill("");
  await page.waitForFunction(() => window.draft && !("token_budget" in window.draft));
  await page.evaluate(() => window.renderPanel("existing"));
  await page.waitForFunction(() => window.draft === null);
  goal = { id: "goal", objective: "恢复目标", status: "paused", tokenBudget: null, tokensUsed: 40, timeUsedSeconds: 2, version: 8, stopReason: "连接中断", running: false };
  await page.evaluate(() => window.renderPanel("paused"));
  await page.getByText("恢复目标", { exact: true }).waitFor();
  await page.getByRole("checkbox").check();
  await page.waitForFunction(() => window.draft?.action === "resume" && window.draft.expected_version === 8);
  await page.getByRole("button", { name: "结束目标" }).click();
  await page.getByText(/已结束/).waitFor();
  assert.equal(controls[0].body.expected_version, 8);
  assert.ok(controls[0].url.endsWith("/end"));
  await page.screenshot({ path: path.join(output, "goal-mobile.png") });
  assert.deepEqual(errors, []);
  console.log(`Passed: goal opt-in, required objective, budget validation/omission, session reset, versioned resume/end, mobile rendering. Screenshot: ${output}`);
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
