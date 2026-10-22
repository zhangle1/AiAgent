import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export function versionPlan(root, version) {
  if (!/^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$/.test(version)) throw Error("Use a stable major.minor.patch version.");
  const packageFiles = ["package.json", "docs/package.json"];
  for (const group of ["apps", "packages"]) for (const dir of fs.readdirSync(path.join(root, group), { withFileTypes: true })) if (dir.isDirectory() && fs.existsSync(path.join(root, group, dir.name, "package.json"))) packageFiles.push(`${group}/${dir.name}/package.json`);
  const replacements = packageFiles.map(file => [file, /("version"\s*:\s*")[^"]+("\s*[,}])/]);
  replacements.push(["Cargo.toml", /(\[workspace\.package\][\s\S]*?\bversion\s*=\s*")[^"]+(")/]);
  replacements.push(["Cargo.lock", /(\[\[package\]\]\s*name\s*=\s*"host-core"\s*version\s*=\s*")[^"]+(")/]);
  replacements.push(["packages/shared/src/protocol.ts", /(APP_VERSION\s*=\s*")[^"]+(")/]);
  return replacements.map(([file, pattern]) => {
    const full = path.join(root, file), bytes = fs.readFileSync(full), before = bytes.toString("utf8");
    if (!Buffer.from(before).equals(bytes) || !pattern.test(before)) throw Error(`Unsupported encoding/version surface: ${file}`);
    return { file, full, before, after: before.replace(pattern, (_match, prefix, suffix) => prefix + version + suffix) };
  });
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const version = process.argv[2], dry = process.argv.includes("--dry-run");
  const plan = versionPlan(process.cwd(), version ?? "");
  if (!dry) for (const item of plan) { if (item.before === item.after) continue; fs.writeFileSync(item.full + ".version.tmp", item.after); fs.renameSync(item.full + ".version.tmp", item.full); }
  console.log(JSON.stringify({ version, dryRun: dry, files: plan.map(item => item.file), message: dry ? "No source changed." : "Runtime version surfaces updated. Add release notes before publishing." }, null, 2));
}
