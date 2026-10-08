import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const api = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/code-repository-api.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(api);
const file = { repository_name: "repo", path: "docs/方案.pptx", name: "方案.pptx" };
const json = (data, status = 200) => new Response(JSON.stringify(data), { status });

test("resolves relative and absolute references using authorized catalog", async (t) => {
  const requests = [];
  t.mock.method(globalThis, "fetch", async (url, init) => {
    requests.push({ url, init });
    return json(url.endsWith("resolve-file-reference") ? { repository_name: "repo", file_path: file.path } : [file]);
  });
  for (const ref of ["方案.pptx", "repo/docs/方案.pptx", "./docs/方案.pptx:12", "D:\\work\\repo\\docs\\方案.pptx"]) {
    assert.deepEqual(await api.resolveProjectDocumentReference(7, ref), file);
  }
  assert.ok(requests.every(({ url }) => url.startsWith("/api/v1/code-repositories/projects/7/")));
  assert.equal(requests.filter(({ init }) => init?.method === "POST").length, 1);
});

test("ambiguous, missing and external references never download", async (t) => {
  const requests = [];
  t.mock.method(globalThis, "fetch", async (url) => { requests.push(url); return json([file, { ...file, repository_name: "other" }]); });
  await assert.rejects(api.downloadProjectDocumentReference(7, "方案.pptx"), /多个同名/);
  await assert.rejects(api.downloadProjectDocumentReference(7, "missing.txt"), /找不到/);
  await assert.rejects(api.downloadProjectDocumentReference(7, "https://example.com/方案.pptx"), /当前项目/);
  assert.ok(requests.every((url) => !url.includes("download")));
});

test("download uses server-validated identity and preserves original filename and bytes", async (t) => {
  const bytes = new Uint8Array([80, 75, 3, 4, 0, 255]);
  const requests = [];
  let blob;
  let clicked = false;
  let removed = false;
  let cleanup;
  const anchor = { click() { clicked = true; }, remove() { removed = true; } };
  t.mock.method(globalThis, "fetch", async (url) => {
    requests.push(url);
    return url.includes("/download?") ? new Response(bytes) : json([file]);
  });
  t.mock.method(URL, "createObjectURL", (value) => { blob = value; return "blob:test"; });
  t.mock.method(URL, "revokeObjectURL", (url) => assert.equal(url, "blob:test"));
  globalThis.document = { createElement: () => anchor, body: { appendChild() {} } };
  globalThis.window = { setTimeout: (callback) => { cleanup = callback; } };
  t.after(() => { delete globalThis.document; delete globalThis.window; });
  await api.downloadProjectDocumentReference(7, "方案.pptx");
  assert.equal(anchor.download, file.name);
  assert.equal(anchor.href, "blob:test");
  assert.ok(clicked && removed);
  assert.deepEqual(new Uint8Array(await blob.arrayBuffer()), bytes);
  const params = new URL(requests[1], "https://fixture.test").searchParams;
  assert.equal(params.get("repository_name"), "repo");
  assert.equal(params.get("path"), file.path);
  cleanup();
});

test("permission failure is surfaced instead of saving an error response", async (t) => {
  t.mock.method(globalThis, "fetch", async (url) => url.includes("/download?") ? json({ message: "denied" }, 403) : json([file]));
  await assert.rejects(api.downloadProjectDocumentReference(7, "方案.pptx"), /403/);
});
