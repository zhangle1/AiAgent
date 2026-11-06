import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const exports = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/admin-api.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText)(exports);

test("Excel download preserves inclusive UTC dates, aggregation and user/project filters", async () => {
  const previous = { fetch: globalThis.fetch, document: globalThis.document, window: globalThis.window };
  const links = [];
  let downloaded = false;
  globalThis.document = { createElement: () => ({ click() { downloaded = true; }, remove() {} }), body: { appendChild(link) { links.push(link); } } };
  globalThis.window = { setTimeout(fn) { fn(); } };
  globalThis.fetch = async url => {
    const query = new URL(url, "https://example.test");
    assert.equal(query.pathname, "/api/v1/admin/usage/export");
    assert.deepEqual(Object.fromEntries(query.searchParams), {
      period: "week", start_date: "2026-10-20", end_date: "2026-11-06", user_id: "001", project_id: "0",
    });
    return new Response(new Blob(["xlsx"]));
  };
  try {
    await exports.exportAdminUsage({ period: "week", from: "2026-10-20T00:00:00Z", to: "2026-11-07T00:00:00Z" }, "001", "0");
    assert.equal(downloaded, true);
    assert.equal(links[0].download, "用户流量_2026-10-20_2026-11-06.xlsx");
    globalThis.fetch = async () => new Response(JSON.stringify({ message: "没有管理权限" }), { status: 403 });
    await assert.rejects(exports.exportAdminUsage({ period: "day", from: "2026-11-06", to: "2026-11-07" }), /没有管理权限/);
    assert.equal(links.length, 1);
  } finally {
    Object.assign(globalThis, previous);
  }
});
