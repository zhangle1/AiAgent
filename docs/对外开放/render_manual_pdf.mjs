import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { copyFileSync } from "node:fs";
import path from "node:path";

const root = path.dirname(fileURLToPath(import.meta.url));
// Share the full build's portable renderer and two-page validation.
const result = spawnSync(process.env.PYTHON || "python", [
  "-c", "from build_manual_assets import render_pdf; render_pdf()",
], { cwd: root, stdio: "inherit", windowsHide: true });
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status || 1);
if (process.env.MANUAL_PDF_OUTPUT) {
  copyFileSync(path.join(root, "坤伴Agent协同研发平台介绍手册.pdf"), path.resolve(process.env.MANUAL_PDF_OUTPUT));
}
