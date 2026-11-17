import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";
const source = ts.transpileModule(fs.readFileSync(new URL("../lib/chat-prototype.ts", import.meta.url), "utf8"), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
const mod = { exports: {} }; new Function("exports", source)(mod.exports);
const { chatPrototypeOutputs } = mod.exports;
test("extract multiple complete documents without prose and preserve source IDs", () => {
  const items = chatPrototypeOutputs([{ id: "one", content: '解释\n```html\n<html><head><title>甲</title></head><body>A</body></html>\n```\n说明\n```html\n<html><body>B</body></html>\n```\n尾部' }]);
  assert.equal(items.length, 2); assert.equal(items[0].id, "one:0"); assert.equal(items[1].id, "one:1");
  assert.equal(items[0].name, "甲"); assert.doesNotMatch(items[0].html, /解释|说明|尾部/);
});
test("ignore partial output, other languages and oversized documents", () => {
  for (const content of ['```html\n<html>partial\n```', '```js\n<html></html>\n```', '```html\n<html></html>', '```html\n<html>' + 'x'.repeat(2_000_000) + '</html>\n```']) {
    assert.equal(chatPrototypeOutputs([{ id: "x", content }]).length, 0);
  }
});
test("support CRLF HTML and do not combine distinct messages", () => {
  assert.equal(chatPrototypeOutputs([{ id: "a", content: '```html\r\n<html></html>\r\n```' }, { id: "b", content: '普通聊天' }]).length, 1);
});
