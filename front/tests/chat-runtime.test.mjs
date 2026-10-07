import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";
import { createRequire } from "node:module";

const exports = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-runtime.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(exports);
const { runtimeAccessUrl, runtimeTestFromHref, buildRuntimePrompt } = exports;
const id = "a".repeat(32);

test("Markdown preserves runtime URL and renders a new-window test card", () => {
  const require = createRequire(import.meta.url);
  const component = {};
  const compiled = ts.transpileModule(fs.readFileSync(new URL("../components/chat/MarkdownMessage.tsx", import.meta.url), "utf8"), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  new Function("require", "exports", compiled)((name) => {
    if (name === "@/lib/chat-runtime") return exports;
    if (name === "react" || name === "react/jsx-runtime") return require(name);
    if (name === "react-markdown") return { default: "markdown", defaultUrlTransform: url => url };
    return {};
  }, component);
  const href = `/runtime-test?project_id=7&request_id=${id}`;
  const markdown = component.MarkdownMessage({ content: `[Test](${href})`, projectId: 7 });
  assert.equal(markdown.props.urlTransform(href), href);
  const card = markdown.props.components.a({href,children:"Test"});
  assert.equal(card.props.href,href);
  assert.equal(card.props.target,"_blank");
  assert.equal(card.props.rel,"noopener noreferrer");
});

test("external host and IPv6 are preserved while development port and route change", () => {
  assert.equal(runtimeAccessUrl("http://124.70.221.213:3782", 4301, "/login?from=test"), "http://124.70.221.213:4301/login?from=test");
  assert.equal(runtimeAccessUrl("https://[::1]:3782", 5101), "http://[::1]:5101/");
});
test("routes cannot escape to another host", () => {
  for (const path of ["//evil.test", "/\\evil.test", "https://evil.test", "/\r\nevil", "/#bad"]) assert.throws(() => runtimeAccessUrl("http://example.test", 4301, path));
  for (const port of [0, 80, 65536, 4301.5]) assert.throws(() => runtimeAccessUrl("http://example.test", port));
});
test("test cards only accept the current project and a managed request id", () => {
  assert.equal(runtimeTestFromHref(`/runtime-test?project_id=7&request_id=${id}`, 7), `/runtime-test?project_id=7&request_id=${id}`);
  assert.equal(runtimeTestFromHref(`/runtime-test?project_id=8&request_id=${id}`, 7), null);
  assert.equal(runtimeTestFromHref(`https://evil.test/runtime-test?project_id=7&request_id=${id}`, 7), null);
  assert.equal(runtimeTestFromHref("/runtime-test?project_id=7&request_id=../../outside", 7), null);
});
test("multi-entry prompt includes selected scope, external host, managed manifest and requirements", () => {
  const selections = [{ repository_name: "api", entry_paths: ["src/Api.csproj"] }, { repository_name: "web", entry_paths: ["package.json", "config.json"] }];
  const prompt = buildRuntimePrompt(7, selections, "open /login", "http://124.70.221.213:3782", { request_id: id, manifest_repository: "api", manifest_path: `artifacts/aiagent-runs/${id}.json`, idle_minutes: 15 });
  for (const value of ["src/Api.csproj", "config.json", "http://124.70.221.213:3782", "open /login", `${id}.result.json`, "preferred_port", "CORS", "15", "后端健康检查通过后才启动前端"]) assert.ok(prompt.includes(value), value);
});
