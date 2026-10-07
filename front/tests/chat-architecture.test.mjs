import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

function load(file, mocks = {}) {
  const source = ts.transpileModule(fs.readFileSync(new URL(file, import.meta.url), "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText;
  const module = { exports: {} };
  new Function("require", "module", "exports", source)((name) => mocks[name] ?? {}, module, module.exports);
  return module.exports;
}
const api = load("../lib/chat-architecture.ts");
const visual = load("../lib/chat-visualization.ts", { "@/lib/chat-architecture": api });
const graph = { version: 1, title: "架构", nodes: ["a", "b", "c", "isolated"].map((id) => ({ id, label: id })), edges: [{ from: "a", to: "b" }, { from: "b", to: "c" }, { from: "c", to: "a" }] };
test("validates limits, duplicate ids and dangling references", () => {
  assert.equal(api.parseArchitecture(JSON.stringify(graph)).nodes.length, 4);
  for (const bad of [null, {}, { ...graph, version: 2 }, { ...graph, nodes: [] }, { ...graph, nodes: [graph.nodes[0], graph.nodes[0]] }, { ...graph, edges: [{ from: "missing", to: "a" }] }, { ...graph, title: "a".repeat(121) }, { ...graph, nodes: Array.from({ length: 41 }, (_, i) => ({ id: `a${i}`, label: "a" })) }, { ...graph, edges: Array(101).fill(graph.edges[0]) }]) {
    assert.throws(() => api.parseArchitecture(JSON.stringify(bad)));
  }
  assert.throws(() => api.parseArchitecture(" ".repeat(100001)));
  assert.throws(() => api.parseArchitecture('{"version":1'));
});
test("projects only supported fields; hostile strings remain inert text", () => {
  const parsed = api.parseArchitecture(JSON.stringify({ ...graph, html: "<script>alert(1)</script>", nodes: [{ id: "a", label: "<img src=x onerror=alert(1)>", source: "javascript:alert(1)", html: "bad" }], edges: [] }));
  assert.equal(parsed.nodes[0].label, "<img src=x onerror=alert(1)>");
  assert.equal(parsed.html, undefined);
  assert.equal(parsed.nodes[0].html, undefined);
});
test("reachability and shortest directed paths terminate on cycles and handle isolated nodes", () => {
  const parsed = api.parseArchitecture(JSON.stringify(graph));
  assert.deepEqual([...api.relatedNodes(parsed, "a", "downstream")], ["a", "b", "c"]);
  assert.deepEqual([...api.relatedNodes(parsed, "a", "upstream")], ["a", "c", "b"]);
  assert.deepEqual(api.architecturePath(parsed, "a", "c"), ["a", "b", "c"]);
  assert.deepEqual(api.architecturePath(parsed, "a", "isolated"), []);
  assert.deepEqual(api.architecturePath(parsed, "a", "a"), ["a"]);
});
test("scope persists in the request and cannot escape repository; other modes ignore it", () => {
  const scope = { projectId: 1, repository: "repo", path: "src\\App.sln" };
  const request = visual.buildVisualizationMessage("分析", "interactive", [], scope);
  assert.match(request, /aiagent-architecture/);
  assert.match(request, /src\/App.sln/);
  assert.match(request, /不得静默改用其他解决方案/);
  assert.match(request, /不修改项目文件/);
  assert.equal(visual.buildVisualizationMessage(request, null), request);
  assert.doesNotMatch(visual.buildVisualizationMessage("分析", "architecture", [], scope), /App.sln/);
  for (const path of ["../file", "C:\\file", "/tmp/file", "x/../../y", "x\u0000y"]) assert.throws(() => api.normalizeArchitecturePath(path));
  assert.equal(api.normalizeArchitecturePath(""), "");
});
