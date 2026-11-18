import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = ts.transpileModule(readFileSync(new URL("../lib/chat-collaboration.ts", import.meta.url), "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText;
const module = { exports: {} }; new Function("module", "exports", source)(module, module.exports);
const { collaborationCommits, collaborationLink, collaborationPrompt } = module.exports;
const snapshot = { id: "snapshot/1", project_id: 2, files: [{ id: "f1", path: "src/库存.ts" }, { id: "f2", path: "src/login.ts" }], commits: [
  { id: "c1", author_id: "a", author: "甲", committed_at: "2026-11-17T23:00:00Z", subject: "修复库存", sha: "abc123", files: [{ file_id: "f1" }] },
  { id: "c2", author_id: "b", author: "乙", committed_at: "2026-11-18T01:00:00Z", subject: "Login", sha: "def456", files: [{ file_id: "f2" }] },
] };
test("person, inclusive UTC dates, path and commit search intersect without changing snapshot", () => {
  assert.deepEqual(collaborationCommits(snapshot, "a", "2026-11-17", "2026-11-17", "库存"), [snapshot.commits[0]]);
  assert.equal(collaborationCommits(snapshot, "a", "2026-11-18", "", "").length, 0);
  assert.deepEqual(collaborationCommits(snapshot, "", "", "", "LOGIN"), [snapshot.commits[1]]);
  assert.deepEqual(collaborationCommits(snapshot, "", "", "", "ABC"), [snapshot.commits[0]]);
  assert.equal(collaborationCommits(snapshot, "", "2026-11-18", "2026-11-17", "").length, 0);
  assert.equal(snapshot.commits.length, 2);
});
test("links encode opaque IDs and preserve project, snapshot, commit and file", () => {
  const url = new URL(collaborationLink("https://example.test", snapshot, { id: "c?&1" }, "文件/1"));
  assert.equal(url.pathname, "/change-atlas/review");
  assert.deepEqual(Object.fromEntries(url.searchParams), { project: "2", snapshot: "snapshot/1", commit: "c?&1", file: "文件/1" });
  assert.equal(new URL(collaborationLink("https://example.test", snapshot, snapshot.commits[0])).searchParams.has("file"), false);
});
test("prompt includes chosen references, preservation guidance and evidence limits", () => {
  const prompt = collaborationPrompt("示例项目", ["- ref1\n  https://example.test/1", "- ref2\n  https://example.test/2"]);
  assert.ok(prompt.includes("示例项目"));
  assert.ok(prompt.includes("保留已有有效行为"));
  assert.ok(prompt.includes("不把摘要当成已审阅代码"));
  assert.ok(prompt.indexOf("https://example.test/1") < prompt.indexOf("https://example.test/2"));
});
