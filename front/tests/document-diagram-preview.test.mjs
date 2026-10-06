import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import { createRequire } from "node:module";
import ts from "typescript";

const require = createRequire(import.meta.url);
const React = require("react");
function loadComponent(file, mocks, extra = "") {
  const source = ts.transpileModule(fs.readFileSync(new URL(file, import.meta.url), "utf8") + extra, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const module = { exports: {} };
  new Function("require", "module", "exports", source)((name) => {
    if (name in mocks) return mocks[name];
    if (name === "react/jsx-runtime") return require(name);
    return {};
  }, module, module.exports);
  return module.exports;
}
function find(node, type) {
  if (!node || typeof node !== "object") return undefined;
  if (node.type === type) return node;
  for (const child of React.Children.toArray(node.props?.children)) {
    const result = find(child, type);
    if (result) return result;
  }
}

test("document renderers keep their identity when the inspector refreshes", () => {
  const Markdown = () => null;
  const Mermaid = () => null;
  const module = loadComponent("../components/chat/ChatInspectorPanel.tsx", {
    react: { ...React, useMemo: (fn) => fn(), useState: () => [false, () => {}] },
    "react-markdown": { default: Markdown },
    "@/components/chat/MermaidDiagram": { MermaidDiagram: Mermaid, mermaidSourceFromPre: () => "graph LR; A-->B" },
  }, "\nexport { ProjectDocumentsTab };\n");
  const props = {
    projectId: 1, documents: [], directories: [], selectedDocument: { path: "doc/design.md", preview_kind: "markdown" },
    selectedDirectory: { repository_name: "test", path: "" }, content: { content: "```mermaid\ngraph LR; A-->B\n```" },
    uploadInputRef: { current: null },
  };
  const first = find(module.ProjectDocumentsTab(props), Markdown);
  const refreshed = find(module.ProjectDocumentsTab({ ...props }), Markdown);
  assert.ok(first);
  assert.equal(first.props.components, refreshed.props.components);
  for (const key of Object.keys(first.props.components)) {
    assert.equal(first.props.components[key], refreshed.props.components[key], `${key} must not remount`);
  }
  assert.equal(first.props.components.pre({ children: "chart" }).type, Mermaid);
});

test("expanded diagram uses a body portal and reuses the existing SVG", () => {
  const svg = '<svg viewBox="0 0 800 100"></svg>';
  let expanded = false;
  let stateIndex = 0;
  const portalType = "test-portal";
  const module = loadComponent("../components/chat/MermaidDiagram.tsx", {
    react: { ...React, useId: () => "diagram", useEffect: () => {}, useState: () => {
      const index = stateIndex++;
      return [index === 0 ? svg : index === 1 ? "" : expanded, (value) => { if (index === 2) expanded = value; }];
    } },
    "react-dom": { createPortal: (children, container) => React.createElement(portalType, { container }, children) },
  });
  const previousDocument = globalThis.document;
  globalThis.document = { body: {} };
  try {
    const collapsed = module.MermaidDiagram({ chart: "graph LR; A-->B" });
    assert.equal(find(collapsed, portalType), undefined);
    find(collapsed, "div").props.onClick();
    stateIndex = 0;
    const opened = module.MermaidDiagram({ chart: "graph LR; A-->B" });
    const portal = find(opened, portalType);
    assert.equal(portal.props.container, document.body);
    assert.equal(portal.props.children.props.role, "dialog");
    const inner = React.Children.toArray(portal.props.children.props.children)[0];
    const image = React.Children.toArray(inner.props.children)[1];
    assert.equal(image.props.dangerouslySetInnerHTML.__html, svg);
    portal.props.children.props.onMouseDown();
    assert.equal(expanded, false);
  } finally {
    if (previousDocument === undefined) delete globalThis.document;
    else globalThis.document = previousDocument;
  }
});
