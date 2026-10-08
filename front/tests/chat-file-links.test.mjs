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
  if (name === "@/components/chat/ChatDocumentCard") return { ChatDocumentCard: "DocumentCard" };
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
    assert.equal(link.type, "DocumentCard");
    assert.equal(link.props.href, undefined);
    link.props.onOpen(link.props.reference);
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

for (const extension of ["md", "markdown", "html", "txt", "pdf", "docx", "pptx", "xlsx", "csv", "json", "yaml", "doc", "ppt", "xls", "rtf"]) {
  test(`generated ${extension} uses a preview and download card`, () => {
    const rendered = module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenProjectMarkdownDocument: () => {} });
    const reference = `repo/docs/设计.${extension}`;
    const link = rendered.props.components.a({ href: rendered.props.urlTransform(reference), children: "生成结果" });
    assert.equal(link.type, "DocumentCard");
    assert.equal(link.props.reference, reference);
    const inline = rendered.props.components.code({ children: reference });
    assert.equal(inline.type, "DocumentCard");
  });
}

test("external URLs with document labels never open local files", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenProjectMarkdownDocument: () => {}, onOpenCodeFile: () => {} });
  for (const href of ["https://example.com/report.md", "//example.com/report.md"]) {
    const link = rendered.props.components.a({ href: rendered.props.urlTransform(href), children: "report.md" });
    assert.equal(link.type, "a");
    assert.equal(link.props.href, href);
  }
});

test("current project download links become document cards", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenProjectMarkdownDocument: () => {} });
  const href = "/api/v1/code-repositories/projects/7/markdown-documents/download?repository_name=repo&path=docs%2Fslides.pptx";
  const link = rendered.props.components.a({ href: rendered.props.urlTransform(href), children: "幻灯片" });
  assert.equal(link.type, "DocumentCard");
  assert.equal(link.props.reference, "repo/docs/slides.pptx");
  assert.equal(rendered.props.components.a({ href: rendered.props.urlTransform(href.replace("/7/", "/8/")), children: "slides.pptx" }).type, "a");
});
