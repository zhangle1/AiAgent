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
const layout = load("../lib/architecture-layout.ts");
const visual = load("../lib/chat-visualization.ts", { "@/lib/chat-architecture": api });
const graph = { version: 1, title: "架构", nodes: ["a", "b", "c", "isolated"].map((id) => ({ id, label: id })), edges: [{ from: "a", to: "b" }, { from: "b", to: "c" }, { from: "c", to: "a" }] };
test("five diagram types preserve semantics and reject unsupported schema values", () => {
  for (const diagramType of Object.keys(api.architectureTypes)) {
    const parsed = api.parseArchitecture(JSON.stringify({ ...graph, diagramType }));
    assert.equal(parsed.diagramType, diagramType);
    const result = layout.layoutArchitecture(parsed);
    assert.equal(result.positions.size, 4);
    assert.ok(Number.isFinite(result.width) && Number.isFinite(result.height));
    for (const [i] of parsed.edges.entries()) assert.doesNotMatch(layout.edgeRoute(parsed, result, i).d, /NaN|Infinity/);
  }
  for (const patch of [{ diagramType: "html" }, { diagramType: "__proto__" }, { nodes: [{ id: "x", label: "x", kind: "script" }] }, { edges: [{ from: "a", to: "b", style: "url(x)" }] }]) assert.throws(() => api.parseArchitecture(JSON.stringify({ ...graph, ...patch })));
});
test("sequence messages retain order, repeats and self calls; lifecycle cycles occupy two rows", () => {
  const sequence = api.parseArchitecture(JSON.stringify({ ...graph, diagramType: "sequence", edges: [{ from: "a", to: "b" }, { from: "a", to: "b", style: "dashed" }, { from: "b", to: "b" }] }));
  const result = layout.layoutArchitecture(sequence);
  const routes = sequence.edges.map((_, i) => layout.edgeRoute(sequence, result, i));
  assert.ok(routes[1].label.y > routes[0].label.y);
  assert.match(routes[2].d, /h56 v30 h-56/);
  assert.equal(sequence.edges[1].style, "dashed");
  const lifecycle = layout.layoutArchitecture({ ...sequence, diagramType: "lifecycle" });
  assert.equal(new Set([...lifecycle.positions.values()].map(p => p.y)).size, 2);
});
test("architecture packs many groups, fits viewport and routes around intervening cards", () => {
  const many = api.parseArchitecture(JSON.stringify({ ...graph, nodes: Array.from({ length: 20 }, (_, i) => ({ id: `n${i}`, label: `节点${i}`, group: `group${i}` })), edges: [] }));
  const packed = layout.layoutArchitecture(many);
  assert.ok(packed.width < 1500);
  const zoom = layout.fitDiagram(packed.width, packed.height, 1200, 600);
  assert.ok(packed.width * zoom <= 1168 && packed.height * zoom <= 568);
  const parsed = api.parseArchitecture(JSON.stringify(graph));
  const positioned = layout.layoutArchitecture(parsed, { a: { x: 80, y: 100 }, b: { x: 380, y: 100 }, c: { x: 680, y: 100 }, isolated: { x: 80, y: 400 } });
  const route = layout.edgeRoute({ ...parsed, edges: [{ from: "a", to: "c", label: "跨越" }] }, positioned, 0);
  const points = [...route.d.matchAll(/[ML]([\d.]+),([\d.]+)/g)].map(m => ({ x: +m[1], y: +m[2] }));
  assert.ok(points.some(p => p.y <= 100 || p.y >= 180));
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1], b = points[i];
    if (a.y === b.y && a.y > 100 && a.y < 180) assert.ok(Math.max(a.x, b.x) <= 380 || Math.min(a.x, b.x) >= 564);
  }
});
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
test("AI chooses relevant authorized code without a manual scope", () => {
  for (const type of [...visual.diagramTypes, ...visual.mermaidDiagramTypes]) {
    const request = visual.buildVisualizationMessage("分析", type.id);
    assert.match(request, /自行判断是否需要分析代码/);
    assert.match(request, /当前项目已授权的仓库/);
    assert.match(request, /尊重用户问题中明确指定的范围/);
    assert.match(request, /不修改项目文件/);
    assert.equal(visual.buildVisualizationMessage(request, null), request);
  }
});
