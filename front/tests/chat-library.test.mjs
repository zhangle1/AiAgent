import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const library = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-library.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(library);
const doc = (path, updated_at = "2026-10-10T10:00:00Z") => ({ repository_name: "repo", path, name: path.split(/[\\/]/).at(-1), updated_at, source: "repository" });
const upload = (file_name, kind = "document") => ({ id: file_name, file_name, kind, created_at: "2026-10-09T10:00:00Z" });

test("includes root HTML, legacy Office and uploads, sorted by actual modification time", () => {
  const items = library.buildChatLibrary([upload("photo.png", "image"), upload("report.pdf")], [doc("prototype.html"), doc("docs/legacy.doc", "2026-10-08T10:00:00Z")]);
  assert.deepEqual(items.map(item => item.name), ["prototype.html", "photo.png", "report.pdf", "legacy.doc"]);
  assert.equal(items[0].source, "project");
  assert.equal(items[1].source, "upload");
});

test("excludes source, templates in source folders, build products and duplicate text extractions", () => {
  const items = library.buildChatLibrary([upload("main.ts"), upload("extract.txt", "extracted_text")], [
    ...["main.cs", "package.json", "src/index.html", "front/app/index.html", "dist/index.html", "node_modules/a/readme.md", "backed\\bin\\report.html"].map(path => doc(path)),
    doc("artifacts/report.html"), { ...doc("index.md"), source: "agent_index" },
  ]);
  assert.deepEqual(items.map(item => item.name), ["report.html"]);
});

test("same names retain distinct identities and unknown timestamps sort last", () => {
  const items = library.buildChatLibrary([], [doc("a/report.md", ""), doc("b/report.md")]);
  assert.equal(items.length, 2);
  assert.notEqual(items[0].id, items[1].id);
  assert.equal(items[0].location, "repo/b/report.md");
});
