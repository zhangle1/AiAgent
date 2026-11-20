import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import ts from "typescript";
import { workflow } from "./helpers/chat-workflow.mjs";

const source = ts.transpileModule(fs.readFileSync(new URL("../components/chat/MarkdownMessage.tsx", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText;
const module = { exports: {} };
const packaging = { exports: {} };
const knowledge = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/knowledge-api.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(knowledge);
new Function("require", "exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-packaging.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)((name) => { if (name === "./chat-workflow") return workflow; throw new Error(`Unexpected import ${name}`); }, packaging.exports);
new Function("require", "module", "exports", source)((name) => {
  if (name === "@/lib/chat-packaging") return packaging.exports;
  if (name === "@/lib/knowledge-api") return knowledge;
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

test("versioned knowledge chapters render a document card even without a code project", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", projectId: null, onOpenKnowledgeChapter: () => {} });
  const uri = "viking://resources/books/_parsed/Manual-41/parts/Cycle%20time.md";
  const href = `/api/v1/knowledge-resources/file?${new URLSearchParams({ uri, parsed_document_id: "57" })}`;
  const card = rendered.props.components.a({ href: rendered.props.urlTransform(href), children: "Cycle time" });
  assert.equal(card.type, "DocumentCard");
  assert.deepEqual(card.props.chapter, { kind: "resource", uri, parsedDocumentId: 57, name: "Cycle time.md" });
  const external = `https://example.com${href}`;
  assert.equal(rendered.props.components.a({ href: rendered.props.urlTransform(external), children: "Cycle time" }).type, "a");
  const readOnly = module.exports.MarkdownMessage({ content: "" });
  assert.equal(readOnly.props.components.a({ href: readOnly.props.urlTransform(href), children: "Cycle time" }).type, "span");
});

test("old staged chapter links route to the knowledge library instead of the project", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenProjectMarkdownDocument: () => {}, onOpenKnowledgeChapter: () => {} });
  const href = rendered.props.urlTransform("file:///C:/Temp/aiagent-books-random/41-Manual/parts/Cycle%20time.md#L2");
  const card = rendered.props.components.a({ href, children: "Cycle time" });
  assert.deepEqual(card.props.chapter, { kind: "staged", documentId: 41, book: "Manual", path: "parts/Cycle time.md", name: "Cycle time.md" });
});

test("historical book-relative paths open the unique chapter inside the selected knowledge scope", () => {
  const sourceUri = "viking://resources/books/Fixture.pdf";
  const otherSource = "viking://resources/books/Other.pdf";
  const root = "viking://resources/books/_parsed/Fixture-41/";
  const relative = "Part II/Chapter 12/12.3.2 Statistical Process Control_1.md";
  const uri = root + relative.split("/").map(encodeURIComponent).join("/");
  const entry = { uri, kind: "file", extension: ".md", name: relative.split("/").at(-1), source_uri: sourceUri, parsed_document_id: 57 };
  const other = { ...entry, uri: entry.uri.replace("Fixture-41", "Other-42"), source_uri: otherSource, parsed_document_id: 58 };
  const render = (scopes) => module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenProjectMarkdownDocument: () => {}, onOpenKnowledgeChapter: () => {},
    scopedKnowledgeChapters: knowledge.scopedKnowledgeChapterReferences([entry, other], scopes) });
  const expected = { kind: "resource", uri, parsedDocumentId: 57, name: entry.name };
  for (const scope of [root, sourceUri]) {
    const rendered = render([scope]);
    const href = rendered.props.urlTransform(uri.slice(root.length));
    assert.deepEqual(rendered.props.components.a({ href, children: "章节" }).props.chapter, expected);
    assert.deepEqual(rendered.props.components.code({ children: "./" + relative }).props.chapter, expected);
    assert.equal(rendered.props.components.code({ children: entry.name }).props.chapter, undefined);
    assert.equal(rendered.props.components.a({ href: rendered.props.urlTransform(`https://example.test/${uri.slice(root.length)}`), children: "外部链接" }).type, "a");
  }
  for (const scopes of [[], [sourceUri, otherSource]]) {
    assert.equal(render(scopes).props.components.code({ children: relative }).props.chapter, undefined);
  }
});

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


test("slash-prefixed Windows root documents keep preview and download identity", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenProjectMarkdownDocument: () => {} });
  for (const ext of ["html", "doc"]) {
    const link = rendered.props.components.a({ href: rendered.props.urlTransform(`/D:/work/repo/design.${ext}`), children: "Open" });
    assert.equal(link.type, "DocumentCard");
    assert.equal(link.props.reference, `D:/work/repo/design.${ext}`);
  }
});

test("code links preserve the starting line of ranges and column locations", () => {
  const rendered = module.exports.MarkdownMessage({ content: "", projectId: 7, onOpenCodeFile: () => {} });
  for (const reference of ["src/Service.cs:120:8", "src/Service.cs:120-140", "src/Service.cs#L120-L140", "src/Service.cs#L120C8-L140C9", "/D:/work/src/Service.cs:120"]) {
    const href = rendered.props.urlTransform(reference);
    const link = rendered.props.components.a({ href, children: "Service" });
    assert.equal(link.props.candidate.reference, reference.startsWith("/D:") ? "D:/work/src/Service.cs:120" : "src/Service.cs:120");
  }
});
