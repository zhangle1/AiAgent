import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";
import { createRequire } from "node:module";

const module = { exports: {} };
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../lib/chat-packaging.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(module.exports);
const { buildPackagePrompt, packageDownloadFromHref } = module.exports;
const { normalizePackageTarget, normalizePackageTargets, suggestPackageTargets } = module.exports;

test("selected solution scopes packaging and preserves extra requirements", () => {
  const prompt = buildPackagePrompt(7, "repo name", { targetPath: "src\\生产 服务.slnx", instructions: "  Release，win-x64\n附部署说明  " });
  assert.ok(prompt.includes('"src/生产 服务.slnx"'));
  assert.ok(prompt.includes("仅围绕这些入口及其必要依赖构建"));
  assert.ok(prompt.includes("不要静默改用其他入口"));
  assert.ok(prompt.includes("Release，win-x64\n附部署说明"));
  assert.ok(buildPackagePrompt(7, "repo", { targetPath: "web/package.json" }).includes("先确认其构建用途"));
  assert.ok(!buildPackagePrompt(7, "repo").includes("本次唯一打包入口"));
});

test("AI mode accepts multiple candidate entries without treating them as final truth", () => {
  const prompt = buildPackagePrompt(7, "repo", { targetPaths: ["A.sln", "web/package.json", "A.sln"], automatic: true });
  assert.deepEqual(normalizePackageTargets(["A.sln", "web\\package.json", "A.sln"]), ["A.sln", "web/package.json"]);
  assert.match(prompt, /候选入口/);
  assert.match(prompt, /"A\.sln"/);
  assert.match(prompt, /"web\/package\.json"/);
  assert.match(prompt, /自主决定使用一个或多个入口/);
  assert.doesNotMatch(prompt, /不要改选其他解决方案/);
});

test("repository probe ranks the saved target and build entries first", () => {
  assert.deepEqual(suggestPackageTargets([
    "appsettings.json",
    "Web/Web.csproj",
    "Solution.sln",
    "web/package.json",
    "Solution.sln",
    "Properties/PublishProfiles/FolderProfile.pubxml",
  ], "Web/Web.csproj"), ["Web/Web.csproj", "Solution.sln", "web/package.json", "appsettings.json"]);
});

test("packaging rejects absolute paths, traversal and unsupported targets", () => {
  for (const path of ["/a.sln", "C:\\a.sln", "../a.sln", "a/../b.json", "a//b.json", "a\n.json", "a.txt", "\\\\host\\a.json"]) {
    assert.throws(() => normalizePackageTarget(path), undefined, path);
  }
  for (const path of ["App.sln", "App.slnx", "a/A.csproj", "A.fsproj", "A.vbproj", "web/package.json"]) assert.equal(normalizePackageTarget(path), path);
});
const url = (path, project = 7) => `/api/v1/code-repositories/projects/${project}/markdown-documents/download?${new URLSearchParams({ repository_name: "repo name", path })}`;

test("Markdown renderer preserves package URLs and emits a download card", () => {
  const require = createRequire(import.meta.url);
  const compiled = ts.transpileModule(fs.readFileSync(new URL("../components/chat/MarkdownMessage.tsx", import.meta.url), "utf8"), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const exports = {};
  new Function("require", "exports", compiled)((name) => {
    if (name === "@/lib/chat-packaging") return module.exports;
    if (name === "@/lib/chat-runtime") return { runtimeTestFromHref: () => null };
    if (name === "react" || name === "react/jsx-runtime") return require(name);
    if (name === "react-markdown") return { default: "markdown", defaultUrlTransform: (url) => url };
    return {};
  }, exports);
  const href = url("artifacts/aiagent-packages/v1/app.zip");
  const markdown = exports.MarkdownMessage({ content: `[Download](${href})`, projectId: 7 });
  assert.equal(markdown.props.urlTransform(href), href);
  const card = markdown.props.components.a({ href, children: "Download" });
  assert.equal(card.type, "a");
  assert.equal(card.props.href, href);
  assert.equal(card.props.download, true);
  assert.ok(card.props.className.includes("rounded-xl"));
});

test("prompt carries project/repository context, iteration, verification and Git exclusion", () => {
  const prompt = buildPackagePrompt(7, "repo name");
  for (const fragment of ["projects/7/", "repository_name=repo%20name", "git check-ignore", "artifacts/aiagent-packages/", "ZIP", "原子", "密钥", "新版本"]) assert.ok(prompt.includes(fragment), fragment);
});

test("download card accepts encoded Unicode ZIP paths for the current project", () => {
  const href = url("artifacts/aiagent-packages/v1/交付 包.zip");
  assert.deepEqual(packageDownloadFromHref(href, 7), { href, name: "交付 包.zip" });
});

test("download card rejects cross-project, external, traversal and non-package links", () => {
  for (const href of [url("artifacts/aiagent-packages/v1/a.zip", 8), "https://evil.invalid" + url("artifacts/aiagent-packages/a.zip"), "//evil.invalid/a.zip", url("artifacts/aiagent-packages/../secret.zip"), url("artifacts/aiagent-packages/a\\b.zip"), url("other/a.zip"), url("artifacts/aiagent-packages/a.md"), url("artifacts/aiagent-packages//a.zip")]) assert.equal(packageDownloadFromHref(href, 7), null, href);
  assert.equal(packageDownloadFromHref(url("artifacts/aiagent-packages/a.zip"), null), null);
});
