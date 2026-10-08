import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import ts from "typescript";

const source = ts.transpileModule(fs.readFileSync(new URL("../components/chat/MarkdownMessage.tsx", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText;
const module = { exports: {} };
const packaging = { exports: {} };
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-packaging.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(packaging.exports);
new Function("require", "module", "exports", source)((name) => {
  if (name === "@/lib/chat-packaging") return packaging.exports;
  if (name === "@/lib/chat-runtime") return { runtimeTestFromHref: () => null };
  if (name === "react/jsx-runtime") return { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }) };
  if (name === "react-markdown") return { defaultUrlTransform: (url) => /^[a-z]:/i.test(url) ? "" : url };
  return {};
}, module, module.exports);

for (const path of ["D:/AiAgent/Project/AIAGENT/ai-agents/doc/方案.md:12", "D:\\AiAgent\\Project\\AIAGENT\\ai-agents\\doc\\设计.html", "file:///D:/Project/doc/design%20notes.md", "doc/report.pdf"]) {
  test(`document link opens inspector: ${path}`, () => {
    let opened;
    const rendered = module.exports.MarkdownMessage({ content: "", projectId: 1, onOpenProjectMarkdownDocument: (reference) => { opened = reference; } });
    const href = rendered.props.urlTransform(path);
    assert.match(href, /^aiagent:\/\/code-file\?/);
    const link = rendered.props.components.a({ href, children: "功能与技术方案" });
    assert.equal(link.type, "button");
    assert.equal(link.props.href, undefined);
    link.props.onClick();
    assert.ok(opened.endsWith(".md:12") || opened.endsWith(".html") || opened.endsWith("design notes.md") || opened.endsWith(".pdf"));
  });
}

test("external document URL remains a web link", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", onOpenProjectMarkdownDocument: () => assert.fail("external link must not open project file") });
  const href = rendered.props.urlTransform("https://example.com/design.md");
  const link = rendered.props.components.a({ href, children: "设计说明" });
  assert.equal(link.type, "a");
  assert.equal(link.props.href, "https://example.com/design.md");
});
