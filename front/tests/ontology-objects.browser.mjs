import assert from "node:assert/strict";
import http from "node:http";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const front = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.resolve(process.env.CHAT_LIBRARY_TEST_TOOLS || front, "package.json"));
const { chromium } = require("playwright");
const { build } = require("esbuild");
const bundle = await build({
  stdin: {
    contents: `import React from "react";import {createRoot} from "react-dom/client";
    import {ObjectModelWorkspace} from "./components/ontology/ObjectModelWorkspace";
    createRoot(document.getElementById("root")).render(<ObjectModelWorkspace projectId={43} repositoryNames={["AiAgent","AiAgent-Web"]} onClose={()=>{}} onInsert={text=>document.querySelector("output").textContent=text} onAttach={value=>document.querySelector("output").textContent=JSON.stringify(value)} />);`,
    resolveDir: front,
    loader: "tsx",
  },
  tsconfig: path.join(front, "tsconfig.json"),
  bundle: true,
  write: false,
  outdir: "out",
  jsx: "automatic",
});
const model = { id: "f".repeat(32), name: "项目对象模型", version: 1, sources: [] };
let deleted = false;
let lookupUnavailable = false;
const server = http.createServer(async (req, res) => {
  const asset = bundle.outputFiles.find((file) => req.url === "/" + path.basename(file.path));
  if (asset) {
    res.setHeader("content-type", asset.path.endsWith(".css") ? "text/css" : "text/javascript");
    return res.end(asset.contents);
  }
  if (!req.url.startsWith("/api/")) {
    res.setHeader("content-type", "text/html; charset=utf-8");
    return res.end(
      '<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/stdin.css"><style>*{box-sizing:border-box}body{margin:0;font:14px system-ui}#root{width:min(100%,850px);height:640px;display:flex}button{cursor:pointer}</style><div id="root"></div><output></output><script src="/stdin.js"></script>',
    );
  }
  res.setHeader("content-type", "application/json");
  if (req.url.endsWith("/models")) return res.end(JSON.stringify(deleted ? [] : [model]));
  let raw = "";
  for await (const part of req) raw += part;
  if (req.url.endsWith("/test")) return res.end(JSON.stringify({ checkedAt: new Date().toISOString() }));
  if (req.url.endsWith("/name")) {
    model.name = JSON.parse(raw).name;
    model.version++;
    return res.end(JSON.stringify(model));
  }
  if (req.method === "DELETE") {
    deleted = true;
    return res.end(JSON.stringify({ id: model.id }));
  }
  if (req.method === "PUT") {
    const source = JSON.parse(raw).source;
    model.sources = [
      ...model.sources.filter((s) => s.definition.id !== source.id),
      { definition: source, checkedAt: new Date().toISOString() },
    ];
    model.version++;
  }
  if (req.url.endsWith("/lookup") && JSON.parse(raw).key === "stale") {
    res.statusCode = 409;
    return res.end(JSON.stringify({ message: "模型版本已变化" }));
  }
  if (req.url.endsWith("/lookup") && lookupUnavailable) {
    res.statusCode = 503;
    return res.end(JSON.stringify({ message: "查询来源离线" }));
  }
  if (req.url.endsWith("/lookup"))
    return res.end(JSON.stringify({ text: "已读取记录 A1", version: model.version }));
  return res.end(JSON.stringify(model));
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true, executablePath: process.env.CHAT_LIBRARY_TEST_BROWSER });
  const page = await browser.newPage({ viewport: { width: 860, height: 750 } });
  page.setDefaultTimeout(6000);
  const errors = [];
  page.on("pageerror", (error) => {
    errors.push(error.message);
    console.error(error.message);
  });
  await page.goto("http://127.0.0.1:" + server.address().port);
  const button = (name) => page.getByRole("button", { name, exact: true });
  await page.getByText("代码库 · AiAgent、AiAgent-Web", { exact: true }).waitFor();
  assert.equal(await button("旧版连接与流程").count(), 0);
  assert.equal(await page.getByLabel("记录键值", { exact: true }).count(), 0);
  async function dragDivider(label, distance) {
    const separator = page.getByRole("separator", { name: label });
    const pane = page.locator('[id="' + (await separator.getAttribute("aria-controls")) + '"]');
    const before = (await pane.boundingBox()).width;
    const box = await separator.boundingBox();
    const x = box.x + box.width / 2,
      y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + distance, y, { steps: 6 });
    await page.mouse.up();
    assert.ok((await pane.boundingBox()).width > before + 20, label);
    await separator.press("Home");
    assert.equal(
      await separator.getAttribute("aria-valuenow"),
      await separator.getAttribute("aria-valuemin"),
    );
    await separator.press("End");
    assert.equal(
      await separator.getAttribute("aria-valuenow"),
      await separator.getAttribute("aria-valuemax"),
    );
  }
  await dragDivider("调整本体树宽度", 40);
  await page.getByRole("separator", { name: "调整本体树宽度" }).press("Home");
  await button("添加组件").click();
  await dragDivider("调整组件配置宽度", -40);
  await page.getByRole("separator", { name: "调整组件配置宽度" }).press("Home");
  await page.getByLabel("数据库驱动").selectOption("oracle");
  assert.equal(await page.getByLabel("端口").inputValue(), "2484");
  await page.getByLabel("服务名（Service Name）").waitFor();
  await page.getByLabel("数据库账号").waitFor();
  const modelNav = await button("对象模型").boundingBox();
  const componentNav = await button("组件树").boundingBox();
  assert.ok(componentNav.y >= modelNav.y + modelNav.height);
  const rootNode = await page.locator('[id$="-node-root"]').boundingBox();
  const onlyChild = await page.locator("main button[aria-pressed=true]").boundingBox();
  assert.ok(onlyChild.y > rootNode.y + rootNode.height);
  assert.ok(Math.abs(rootNode.x + rootNode.width / 2 - onlyChild.x - onlyChild.width / 2) < 1);
  await page.getByLabel("数据库驱动").selectOption("sqlite");
  await page.getByLabel("项目数据库别名").fill("demo");
  assert.equal(await page.getByLabel("表或视图").isVisible(), false);
  await button("测试连接").click();
  await page.getByText("数据库连接成功，可以保存组件。", { exact: true }).waitFor();
  await button("保存组件").click();
  await page.getByText("组件已保存，可勾选带入聊天。", { exact: true }).waitFor();
  assert.equal(model.sources[0].definition.relation, "");
  await page.getByText("按键查询配置（可选）", { exact: true }).click();
  await page.getByLabel("表或视图").fill("records");
  await page.getByLabel("查询键字段").fill("id");
  await page.getByLabel("返回字段").fill("status");
  await button("测试连接").click();
  await page.getByLabel("组件名称").fill("记录查询");
  assert.equal(await button("保存组件").isEnabled(), true);
  await page.getByLabel("项目数据库别名").fill("demo2");
  assert.equal(await button("保存组件").isEnabled(), false);
  await page.getByLabel("项目数据库别名").fill("demo");
  await button("测试连接").click();
  await button("保存组件").click();
  await button("预览与注入").click();
  await page.getByLabel("整份本体").check();
  await page.getByLabel("记录键值", { exact: true }).fill("stale");
  await button("查询并预览").click();
  assert.equal(await page.getByRole("alert").innerText(), "模型版本已变化");
  await page.getByLabel("记录键值", { exact: true }).fill("A1");
  await button("查询并预览").click();
  assert.equal(await page.getByLabel("Prompt 预览").inputValue(), "已读取记录 A1");
  lookupUnavailable = true;
  await button("查询并预览").click();
  await page.getByText("查询来源离线", { exact: true }).waitFor();
  assert.equal(await button("填入聊天草稿").count(), 0, "读取失败后不可插入上一次结果");
  lookupUnavailable = false;
  await button("查询并预览").click();
  await page.getByLabel("Prompt 预览").waitFor();
  await page.getByLabel("组件名称").fill("编辑中的记录查询");
  await page.getByRole("button", { name: /编辑中的记录查询.*未保存/ }).waitFor();
  assert.equal(await button("填入聊天草稿").count(), 0);
  await button("测试连接").click();
  await button("保存组件").click();
  await page.getByRole("button", { name: /编辑中的记录查询.*已保存/ }).waitFor();
  await page.getByLabel("整份本体").check();
  await button("查询并预览").click();
  await button("填入聊天草稿").click();
  assert.equal(await page.locator("output").innerText(), "已读取记录 A1");
  await button("挂载到本轮提问").click();
  assert.equal(JSON.parse(await page.locator("output").innerText()).key, "A1");
  await page.setViewportSize({ width: 365, height: 720 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  const treeDivider = page.getByRole("separator", { name: "调整本体树宽度" });
  const narrowTree = page.locator('[id="' + (await treeDivider.getAttribute("aria-controls")) + '"]');
  const initialWidth = (await narrowTree.boundingBox()).width;
  await treeDivider.press("ArrowRight");
  assert.ok((await narrowTree.boundingBox()).width >= initialWidth + 5, "在窄屏最小宽度上也能立即调宽");
  await button("预览与注入").click();
  await page.getByLabel("记录键值", { exact: true }).press("Escape");
  assert.equal(await button("预览与注入").getAttribute("aria-expanded"), "false");
  assert.equal(await page.getByRole("separator", { name: "调整组件配置宽度" }).count(), 0);
  await page.setViewportSize({ width: 860, height: 750 });
  await page.getByRole("button", { name: /^项目对象模型( ✓)?$/ }).click({ button: "right" });
  await button("重命名").click();
  await page.getByLabel("新模型名称").fill("接口字典");
  await button("保存名称").click();
  await page.getByRole("button", { name: /^接口字典 ✓$/ }).waitFor();
  await button("组件树").click();
  await button("接口字典").click();
  assert.ok(await page.locator('[id$="-node-root"]').evaluate((el) => el === document.activeElement));
  await button("编辑中的记录查询").click();
  assert.equal(await page.getByLabel("组件名称").inputValue(), "编辑中的记录查询");
  assert.ok(
    await page.locator("main button[aria-pressed=true]").evaluate((el) => el === document.activeElement),
  );
  await button("添加组件").click();
  await page.getByLabel("组件类型").selectOption("http");
  await page.getByLabel("项目接口别名").waitFor();
  await page.getByLabel("组件类型").selectOption("note");
  await page.getByLabel("说明内容").fill("只读字段说明");
  assert.equal(await button("测试连接").count(), 0);
  await button("保存组件").click();
  await page.getByRole("button", { name: /文本说明.*已保存/ }).waitFor();
  assert.equal(await button("保存组件").isEnabled(), false);
  if (process.env.ONTOLOGY_SCREENSHOT) {
    const region = page.getByRole("region", { name: "本体模型工作区" });
    await region.evaluate((element) => {
      document.getElementById("root").style.height = element.scrollHeight + "px";
      element.scrollTop = 0;
    });
    await region.screenshot({ path: process.env.ONTOLOGY_SCREENSHOT });
  }
  await button("对象模型").click();
  await button("接口字典的操作").click();
  page.once("dialog", async (dialog) => dialog.dismiss());
  await button("删除模型").click();
  await button("接口字典").click({ button: "right" });
  page.once("dialog", async (dialog) => dialog.accept());
  await button("删除模型").click();
  await button("创建模型").waitFor();
  assert.ok(deleted);
  assert.deepEqual(errors, []);
  console.log(
    "PASS right workspace, test invalidation, save, query preview, draft, attachment and narrow layout",
  );
} finally {
  await browser?.close();
  await new Promise((resolve) => server.close(resolve));
}
