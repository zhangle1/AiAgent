// Real settings and goal components with synthetic APIs; no user settings or model calls.
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import http from "node:http";
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
const require = createRequire(path.resolve(process.env.GOAL_TEST_TOOLS, "package.json"));
const { build } = require("esbuild");
const { chromium } = require("playwright");
const front = path.resolve(import.meta.dirname, "..");
const output = await fs.mkdtemp(path.join(os.tmpdir(), "chat-goal-budget-browser-"));
const cssInput = path.join(output, "input.css");
const cssOutput = path.join(output, "styles.css");
await fs.writeFile(cssInput, (await fs.readFile(path.join(front, "app/globals.css"), "utf8")).replace(/^@import .*;\r?\n/gm, ""));
execFileSync(process.execPath, [path.join(front, "node_modules/tailwindcss/lib/cli.js"), "-i", cssInput, "-o", cssOutput, "--content", "./components/settings/agents/AgentProvidersSettingsPage.tsx,./components/settings/layout/SettingsShell.tsx"], { cwd: front, stdio: "pipe" });
const css = await fs.readFile(cssOutput, "utf8");
const bundle = await build({
  stdin: { contents: `import React from "react"; import {createRoot} from "react-dom/client";
    import {AgentProvidersSettingsPage} from "./components/settings/agents/AgentProvidersSettingsPage";
    import {I18nProvider} from "./i18n/I18nProvider";
    const root=createRoot(document.getElementById('app'));let key=0;
    window.renderSettings=()=>root.render(<I18nProvider><AgentProvidersSettingsPage key={++key}/></I18nProvider>);
    window.renderSettings();`, loader: "tsx", resolveDir: front },
  bundle: true, write: false, jsx: "automatic", alias: { "@": front },
  define: { "process.env.NODE_ENV": '"development"', "process.env": "{}" },
});
const server = http.createServer((request, response) => {
  response.setHeader("Content-Type", request.url === "/bundle.js" ? "text/javascript" : "text/html");
  response.end(request.url === "/bundle.js" ? bundle.outputFiles[0].text : `<meta name="viewport" content="width=device-width, initial-scale=1"><style>${css}</style><main style="padding:16px"><div id="app"></div></main><script src="/bundle.js"></script>`);
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const browser = await chromium.launch({ headless: true, channel: "msedge" });
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  let admin = true;
  let failSave = false;
  let policy = { models: [], profile_models: [], allowed_model_ids: ["fixture"], default_model_id: "fixture", allow_chat_model_override: true, allowed_reasoning_efforts: ["medium"], default_reasoning_effort: "medium", allow_chat_reasoning_effort_override: true, goal_max_token_budget: 10_000_000 };
  const saves = [];
  await page.route("**/api/**", async route => {
    const name = new URL(route.request().url()).pathname;
    let body = {};
    if (name.endsWith("/auth/status")) body = { authenticated: true, username: "fixture", is_admin: admin };
    if (name.endsWith("/settings/ui")) body = { preferred_agent: "codex" };
    if (name.endsWith("/agent-providers/environments")) body = [];
    if (name.endsWith("/agent-providers/image-ocr-policy")) body = { native_image_input_enabled: true };
    if (name.endsWith("/agent-providers/codex-model-policy")) {
      if (route.request().method() === "PUT") {
        if (failSave) return route.fulfill({ status: 400, json: { message: "模拟保存失败" } });
        const payload = route.request().postDataJSON(); saves.push(payload); policy = { ...policy, ...payload };
      }
      body = policy;
    }
    await route.fulfill({ json: body });
  });
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  const limit = page.getByRole("spinbutton", { name: "目标模式 Token 上限" });
  await limit.waitFor({ timeout: 10000 }).catch(async error => { throw new Error(`${error.message}; page errors: ${JSON.stringify(errors)}; page: ${await page.locator("body").innerText()}`); });
  assert.equal(await limit.inputValue(), "10000000");
  await limit.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, "goal-budget-settings-desktop.png") });
  await limit.fill("6000000");
  await page.getByRole("button", { name: "保存高级配置" }).click();
  await page.getByRole("status").waitFor();
  assert.equal(saves.at(-1).goal_max_token_budget, 6_000_000);
  await page.evaluate(() => window.renderSettings());
  await limit.waitFor();
  assert.equal(await limit.inputValue(), "6000000");
  await limit.fill("0");
  await page.getByRole("button", { name: "保存高级配置" }).click();
  await page.getByRole("alert").filter({ hasText: "目标 Token 上限须为正整数" }).waitFor();
  assert.equal(saves.length, 1);
  await limit.fill("12000000");
  failSave = true;
  await page.getByRole("button", { name: "保存高级配置" }).click();
  await page.getByRole("alert").filter({ hasText: "模拟保存失败" }).waitFor();
  assert.equal(await limit.inputValue(), "12000000");
  assert.equal(policy.goal_max_token_budget, 6_000_000);
  failSave = false;
  await page.getByRole("button", { name: "保存高级配置" }).click();
  await page.getByRole("status").waitFor();
  assert.equal(saves.at(-1).goal_max_token_budget, 12_000_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await limit.scrollIntoViewIfNeeded();
  const bounds = await limit.boundingBox();
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390);
  await page.screenshot({ path: path.join(output, "goal-budget-settings-mobile.png") });
  admin = false;
  await page.evaluate(() => window.renderSettings());
  await page.getByRole("heading", { name: "没有管理权限" }).waitFor();
  assert.equal(await limit.count(), 0);
  assert.deepEqual(errors, []);
  console.log(`Passed: default 10M, administrator save/reload, invalid value, failure retention/retry, >10M configured limit, non-admin visibility, mobile field bounds. Screenshot: ${output}`);
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
