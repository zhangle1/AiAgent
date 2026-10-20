import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../", import.meta.url));
const snapshotPath = path.join(repository, "docs/kunbuddy-client-migration.changeset.json");
const snapshot = JSON.parse(fs.readFileSync(snapshotPath, "utf8"));
const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
assert.equal(snapshot.kind, "local-review-snapshot");
assert.equal(snapshot.fingerprint, sha256(Buffer.from(JSON.stringify(snapshot.files))));
const seen = new Set();
for (const entry of snapshot.files) {
  assert(!seen.has(entry.path), `Duplicate file: ${entry.path}`);
  seen.add(entry.path);
  const fullPath = path.resolve(repository, entry.path);
  const relative = path.relative(repository, fullPath);
  assert(relative && !relative.startsWith("..") && !path.isAbsolute(relative), "Invalid snapshot path");
  const bytes = fs.readFileSync(fullPath);
  assert.equal(bytes.length, entry.bytes, `Size changed: ${entry.path}`);
  assert.equal(sha256(bytes), entry.sha256, `Content changed: ${entry.path}`);
}
console.log(`PASS KunBuddy migration snapshot: ${snapshot.files.length} files, ${snapshot.fingerprint}`);
