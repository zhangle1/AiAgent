import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import ts from "typescript";

const exports = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-project-search.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(exports);
const projects = [
  { label: "AIAGENT", rootPath: "D:\\AIAGENT\\project\\AIAGENT" },
  { label: "加西亚", rootPath: "D:\\AIAGENT\\project\\加西亚" },
  { label: "研发", rootPath: "D:\\AIAGENT\\project\\engineering\\" },
];
const search = query => projects.filter(project => exports.matchesChatProjectSearch(project, query)).map(project => project.label);

test("project name search excludes matches in shared parent directories", () => {
  assert.deepEqual(search("  aiagent  "), ["AIAGENT"]);
  assert.deepEqual(search("加西"), ["加西亚"]);
  assert.deepEqual(search("ENGINEERING"), ["研发"]);
});
test("explicit paths support Windows and forward slashes", () => {
  assert.deepEqual(search("D:\\AIAGENT\\project\\加"), ["加西亚"]);
  assert.deepEqual(search("d:/aiagent/project/加"), ["加西亚"]);
  assert.deepEqual(search("D:\\AIAGENT\\project\\"), projects.map(project => project.label));
});
test("clearing search restores projects and unmatched queries show no projects", () => {
  assert.deepEqual(search(" "), projects.map(project => project.label));
  assert.deepEqual(search("missing"), []);
  assert.equal(exports.matchesChatProjectSearch({ label: "AIAGENT" }, "aiagent"), true);
});
