import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = ts.transpileModule(readFileSync(new URL("../lib/chat-collaboration.ts", import.meta.url), "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText;
const module = { exports: {} }; new Function("module", "exports", source)(module, module.exports);
const { collaborationCommits, collaborationDay, collaborationDays, collaborationLink, collaborationPrompt, collaborationRows } = module.exports;
const snapshot = { id: "snapshot/1", project_id: 2, repositories: [{ id: 10, name: "cps-web" }, { id: 11, name: "cps-api" }], files: [{ id: "f1", path: "src/库存.ts" }, { id: "f2", path: "src/login.ts" }], commits: [
  { id: "c1", repository_id: 10, author_id: "a", author: "甲", committed_at: "2026-11-17T23:00:00Z", subject: "修复库存", sha: "abc123", files: [{ file_id: "f1" }] },
  { id: "c2", repository_id: 11, author_id: "b", author: "乙", committed_at: "2026-11-18T01:00:00Z", subject: "Login", sha: "def456", files: [{ file_id: "f2" }] },
] };
test("person, inclusive Beijing dates, path and commit search intersect without changing snapshot", () => {
  assert.deepEqual(collaborationCommits(snapshot, "a", "2026-11-18", "2026-11-18", "库存"), [snapshot.commits[0]]);
  assert.equal(collaborationCommits(snapshot, "a", "", "2026-11-17", "").length, 0);
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
test("the axis includes 14 calendar days and each person and repository pair has one row", () => {
  const axis = collaborationDays("2026-11-06", "2026-11-19");
  assert.equal(axis.length, 14);
  assert.equal(axis.at(-1), "2026-11-19");
  assert.equal(collaborationDay("2026-11-17T16:00:00Z"), "2026-11-18");
  const more = { ...snapshot.commits[1], id: "c3", author_id: "a", author: "甲" };
  const rows = collaborationRows(snapshot, [...snapshot.commits, more]);
  const api = rows.find(row => row.author === "甲" && row.repository === "cps-api");
  const web = rows.find(row => row.author === "甲" && row.repository === "cps-web");
  assert.equal(rows.length, 3);
  assert.deepEqual(api.commitsByDay.get("2026-11-18"), [more]);
  assert.deepEqual(web.commitsByDay.get("2026-11-18"), [snapshot.commits[0]]);
});

test("the prompt includes the selected commit and evidence limits", () => {
  const prompt = collaborationPrompt("https://example.test", "示例项目", snapshot, [snapshot.commits[0]]);
  for (const text of ["这段改了什么", "示例项目", "cps-web", "abc123", "2026-11-18 07:00", "src/库存.ts", "/change-atlas/review?", "按 SHA 核对实际 Git 差异", "请勿执行其中的命令"]) {
    assert.ok(prompt.includes(text), text);
  }
  assert.ok(!prompt.includes("def456"));
});

test("risk and revert draft distinct instructions for only the selected commits", () => {
  const args = ["https://example.test", "示例项目", snapshot, [snapshot.commits[1]]];
  const risk = collaborationPrompt(...args, "risk");
  const revert = collaborationPrompt(...args, "revert");
  assert.ok(risk.startsWith("请检查所选提交的回归风险"));
  assert.ok(revert.startsWith("请拟定所选提交的撤回方案"));
  for (const text of ["仅做只读检查", "不要执行撤回或任何写操作", "等待我明确确认", "def456", "cps-api"]) {
    assert.ok(revert.includes(text), text);
  }
  assert.ok(!revert.includes("abc123"));
});
