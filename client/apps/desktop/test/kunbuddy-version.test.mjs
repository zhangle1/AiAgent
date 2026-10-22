import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { versionPlan } from "../../../scripts/set-kunbuddy-version.mjs";

test("version plan validates all runtime surfaces and preserves BOM/CRLF without writing", t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "kunbuddy-version-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const dir of ["apps/desktop", "packages/shared/src", "docs"]) fs.mkdirSync(path.join(root, dir), { recursive: true });
  const sources = {
    "package.json": '\ufeff{\r\n  "version": "0.20.1"\r\n}\r\n',
    "apps/desktop/package.json": '{"version":"0.20.1","name":"desktop"}\n',
    "packages/shared/package.json": '{"version":"0.20.1"}\n',
    "docs/package.json": '{"version":"0.20.1"}\n',
    "Cargo.toml": '[workspace.package]\r\nversion = "0.20.1"\r\n',
    "Cargo.lock": '[[package]]\nname = "host-core"\nversion = "0.20.1"\n',
    "packages/shared/src/protocol.ts": 'export const APP_VERSION = "0.20.1";\n',
  };
  for (const [file, value] of Object.entries(sources)) fs.writeFileSync(path.join(root, file), value);
  const plan = versionPlan(root, "10.21.300");
  assert.equal(plan.length, Object.keys(sources).length);
  for (const item of plan) {
    assert.equal(item.after, sources[item.file].replace("0.20.1", "10.21.300"));
    assert.equal(fs.readFileSync(item.full, "utf8"), item.before);
  }
  for (const version of ["1.2", "01.2.3", "1.2.3-beta", "../1.2.3"]) assert.throws(() => versionPlan(root, version));
  fs.writeFileSync(path.join(root, "Cargo.lock"), "missing host version");
  assert.throws(() => versionPlan(root, "1.2.3"), /Unsupported/);
});
