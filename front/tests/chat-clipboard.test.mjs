import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import ts from "typescript";
const exports = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-clipboard.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(exports);
const image = { name: "image.png", type: "image/png" };
function clipboard(files, text = "", html = "", fallback = false) {
  return { items: fallback ? [] : files.map(file => ({ kind: "file", getAsFile: () => file })), files,
    getData: type => type === "text/plain" ? text : type === "text/html" ? html : "" };
}
test("Excel cell paste keeps TSV text instead of uploading its bitmap", () => {
  assert.deepEqual(exports.getChatClipboardFiles(clipboard([image], "物料\t\t数量\r\n测试零件\t\t12")), []);
});
test("single Excel cell with table HTML keeps native text paste", () => {
  assert.deepEqual(exports.getChatClipboardFiles(clipboard([image], "中文", "<table><tr><td>中文</td></tr></table>")), []);
});
test("screenshots still upload and null clipboard items are ignored", () => {
  assert.deepEqual(exports.getChatClipboardFiles(clipboard([image])), [image]);
  const data = clipboard([image], "", "", true);
  data.items = [{kind: "file", getAsFile: () => null}];
  assert.deepEqual(exports.getChatClipboardFiles(data), [image]);
});
test("all Excel file extensions paste as documents with either browser file API", () => {
  for (const extension of ["xls", "xlt", "xlsx", "xlsm", "xlsb", "xltx", "xltm", "csv"]) {
    const file = {name: `test.${extension}`, type: ""};
    for (const fallback of [true, false]) assert.deepEqual(exports.getChatClipboardFiles(clipboard([file], "a\tb", "", fallback)), [file]);
  }
});
test("plain text paste remains native", () => {
  assert.deepEqual(exports.getChatClipboardFiles(clipboard([], "普通文本")), []);
});

test("clipboard falls back to readable files when items only expose an empty placeholder", () => {
  const file = new File(["中文正文"], "notes.txt", { type: "text/plain" });
  const data = clipboard([file]);
  data.items = [{ kind: "file", getAsFile: () => new File([], file.name, { type: file.type }) }];
  assert.deepEqual(exports.getChatClipboardFiles(data), [file]);
});

test("TXT, Markdown, Word and screenshots retain their original bytes", async () => {
  const files = [
    new File(["中文正文"], "日志.txt", { type: "text/plain" }),
    new File(["# 中文标题"], "说明.md", { type: "text/markdown" }),
    new File(["word fixture"], "表单.docx"),
    new File([Uint8Array.of(137, 80, 78, 71)], "截图.png", { type: "image/png" }),
  ];
  for (const fallback of [true, false]) {
    const selected = exports.getChatClipboardFiles(clipboard(files, "", "", fallback));
    assert.deepEqual(selected, files);
    assert.equal(await selected[0].text(), "中文正文");
    assert.equal(await selected[1].text(), "# 中文标题");
  }
});
