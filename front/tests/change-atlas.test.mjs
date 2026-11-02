import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = ts.transpileModule(readFileSync(new URL("../lib/change-atlas.ts", import.meta.url), "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText;
const module = { exports: {} }; new Function("module", "exports", source)(module, module.exports);
const { selectAtlasCommits, atlasModuleStats, atlasCochange, atlasDays } = module.exports;
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
