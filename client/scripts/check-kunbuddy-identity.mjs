import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../", import.meta.url));
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const desktop = JSON.parse(read("apps/desktop/package.json"));
const protocol = read("packages/shared/src/protocol.ts");
assert.equal(desktop.build.appId, "com.knowwhy.kunbuddy");
assert.equal(desktop.build.productName, "KunBuddy");
assert.equal(desktop.build.win.executableName, "KunBuddy");
assert.equal(desktop.build.nsis.shortcutName, "KunBuddy");
assert.equal(desktop.desktopName, "kunbuddy.desktop");
assert(protocol.includes(`APP_ID = "${desktop.build.appId}"`));
assert(protocol.includes('APP_NAME = "KunBuddy"'));
assert.equal(desktop.build.publish, null, "No upstream release publisher");
assert(!desktop.build.win.extraResources.some(item => item.to === "app-update.yml"));
assert(!fs.existsSync(path.join(root, ".git")), "Client must belong to the AiAgent repository");
assert(!fs.existsSync(path.join(root, ".github")), "Upstream publishing workflows must not be imported");
assert(read("apps/desktop/electron.vite.config.ts").includes("port: 4382, strictPort: true"));
const dataPaths = read("apps/desktop/electron/main/data-paths.ts");
assert(dataPaths.includes('INSTALLATION_DATA_DIR_NAME = ".kunbuddy"'));
assert(dataPaths.includes('DEVELOPMENT_DATA_DIR_NAME = ".kunbuddy-dev"'));
assert(read("apps/desktop/electron/main/installation.ts").includes("app.requestSingleInstanceLock()"));
assert(read("crates/host-core/Cargo.toml").includes('name = "kunbuddy-host-core"'));
assert(read("apps/desktop/electron/main/updater.ts").includes("KUNBUDDY_UPDATE_FEED_URL"));
for (const directory of ["packages", "apps"]) {
  for (const entry of fs.readdirSync(path.join(root, directory), { withFileTypes: true })) {
    const file = path.join(directory, entry.name, "package.json");
    if (entry.isDirectory() && fs.existsSync(path.join(root, file))) {
      const pkg = JSON.parse(read(file));
      assert(pkg.name.startsWith("@kunbuddy/"), `${file} has an upstream workspace scope`);
    }
  }
}
function scan(directory) {
  for (const entry of fs.readdirSync(path.join(root, directory), { withFileTypes: true })) {
    if (["node_modules", "dist", "dist-bundle", "out", "target", "resources"].includes(entry.name)) continue;
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) scan(file);
    else if (/\.(ts|tsx|mjs|rs|html)$/.test(file)) {
      assert(!/@pi-desktop\/|PI_DESKTOP_|net\.aiuo\.pi-desktop|["']\.pi-desktop(?:-dev)?["']/.test(read(file)), `Legacy executable identity: ${file}`);
    }
  }
}
for (const directory of ["apps/desktop/electron", "apps/desktop/src", "apps/kunbuddy-host/src", "packages", "crates"]) scan(directory);
assert.equal(fs.readFileSync(path.join(root, "apps/desktop/build/icon.icns")).toString("ascii", 0, 4), "icns");
assert.equal(fs.readFileSync(path.join(root, "apps/desktop/build/icon.ico")).readUInt16LE(2), 1);
console.log("PASS KunBuddy identity, workspace scope, process/data isolation, Git boundary, update boundary and platform icons");
