import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = ts.transpileModule(readFileSync(new URL("../lib/change-atlas.ts", import.meta.url), "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText;
const module = { exports: {} }; new Function("module", "exports", source)(module, module.exports);
const { selectAtlasCommits, atlasModuleStats, atlasCochange, atlasDays, atlasFileTree } = module.exports;
const commit = (id, author, date, fileIds, merge = false) => ({ id, repository_id: 1, sha: id, author_id: author, author, committed_at: date + "T12:00:00Z", files: fileIds.map(file_id => ({ file_id })), is_merge: merge });
const snapshot = {
  start_at: "2026-10-31T00:00:00Z", created_at: "2026-11-02T12:00:00Z", days: 3,
  modules: [{ id: "m", repository_id: 1, name: "领料", directory: "Material" }, { id: "i", repository_id: 1, name: "库存", directory: "Inventory" }],
  files: [{ id: "a", module_id: "m", at_head: true }, { id: "b", module_id: "m", at_head: true }, { id: "c", module_id: "i", at_head: true }],
  commits: [commit("one", "chen", "2026-10-31", ["a", "b", "c"]), commit("two", "lin", "2026-11-01", ["a"]), commit("three", "lin", "2026-11-02", ["a", "c"], true)],
};
test("module totals deduplicate commits and files across multiple changed files", () => {
  const stats = atlasModuleStats(snapshot, snapshot.commits);
  assert.equal(stats[0].commits.length, 3); assert.equal(stats[0].changedFiles.length, 2); assert.equal(stats[0].authors, 2);
  assert.equal(snapshot.commits.length, 3); assert.equal(stats.reduce((sum, item) => sum + item.commits.length, 0), 5);
});
test("time and person filters preserve full snapshot structure", () => {
  const selected = selectAtlasCommits(snapshot, { until: "2026-11-02", daily: false, authorId: "chen" });
  assert.deepEqual(selected.map(item => item.id), ["one"]); assert.equal(snapshot.files.length, 3); assert.equal(snapshot.commits.length, 3);
  assert.deepEqual(selectAtlasCommits(snapshot, { until: "2026-11-01", daily: true, authorId: "" }).map(item => item.id), ["two"]);
  assert.equal(selectAtlasCommits(snapshot, { until: "2026-10-31", daily: true, authorId: "lin" }).length, 0);
});
test("cochange excludes merge diffs and counts each pair once per commit", () => {
  assert.deepEqual(atlasCochange(snapshot, snapshot.commits, "m").map(item => [item.module.id, item.count]), [["i", 1]]);
  assert.deepEqual(atlasDays(snapshot), ["2026-10-31", "2026-11-01", "2026-11-02"]);
});
test("file tree preserves nested directories, duplicate basenames and deleted evidence", () => {
  const files = [
    { id: "a", path: "src/api/item.cs", at_head: true },
    { id: "b", path: "src/ui/item.cs", at_head: false },
    { id: "c", path: "README.md", at_head: true },
  ];
  const tree = atlasFileTree(files);
  assert.equal(tree[0].name, "src");
  assert.deepEqual(tree[0].children.map(node => node.name), ["api", "ui"]);
  assert.equal(tree[0].children[0].children[0].file.id, "a");
  assert.equal(tree[0].children[1].children[0].file.at_head, false);
  assert.equal(tree[1].file.id, "c");
});

const apiSource = ts.transpileModule(readFileSync(new URL("../lib/change-atlas-api.ts", import.meta.url), "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText;
const apiModule = { exports: {} }; new Function("module", "exports", apiSource)(apiModule, apiModule.exports);
const streamResponse = chunks => new Response(new ReadableStream({ start(controller) { for (const chunk of chunks) controller.enqueue(new TextEncoder().encode(chunk)); controller.close(); } }), { headers: { "Content-Type": "text/event-stream" } });
test("analysis stream handles fragmented status, heartbeat, result and diagnostic failures", async () => {
  const statuses = [];
  const result = await apiModule.exports.readAtlasAnalysisStream(streamResponse(['event: sta', 'tus\ndata: {"message":"working"}\n\nevent: heartbeat\ndata: {}\n\n', 'event: result\ndata: {"summary":"verified"}\n\n']), status => statuses.push(status));
  assert.deepEqual(statuses, ["working"]); assert.equal(result.summary, "verified");
  await assert.rejects(apiModule.exports.readAtlasAnalysisStream(streamResponse(['event: error\ndata: {"message":"codex_timeout diagnostic-id"}\n\n'])), /codex_timeout diagnostic-id/);
  await assert.rejects(apiModule.exports.readAtlasAnalysisStream(streamResponse(['event: heartbeat\ndata: {}\n\n'])), /连接已中断/);
});
