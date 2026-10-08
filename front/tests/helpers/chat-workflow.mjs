import fs from "node:fs";
import ts from "typescript";

export const workflow = {};
new Function("exports", ts.transpileModule(fs.readFileSync(new URL("../../lib/chat-workflow.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(workflow);
