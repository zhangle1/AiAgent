import { homedir } from "node:os";
import { join, resolve } from "node:path";
import { APP_NAME } from "@kunbuddy/shared";

/**
 * The two directories that define an installation, and the development split
 * between them.
 *
 * A packaged KunBuddy and a `pnpm dev` host used to share both: the
 * name-derived `userData` — where Electron keeps the single-instance lock,
 * renderer `localStorage`, and the plugin panel partitions — and
 * `~/.kunbuddy`, where host-core keeps `pi.sqlite` beside the persistence
 * outbox and the log tree. Sharing them meant a shipped app that was already
 * running held the lock, so the development launch quit on arrival; a
 * development host that won the race instead put a second host-core over the
 * same single-writer database, which is the divergence D236 exists to
 * prevent. Neither is workable while someone debugs against the app they use.
 *
 * Only the development side moves, and only these two names differ. A shipped
 * installation keeps `KunBuddy` and `~/.kunbuddy`, so no upgrade relocates
 * a user's database, secrets, plugins, or renderer-local state, and
 * `KUNBUDDY_DATA_DIR` still overrides either profile outright.
 */

/** `userData` directory of a development installation, beside the shipped one. */
export const DEVELOPMENT_INSTALLATION_NAME = `${APP_NAME} Dev`;

/** Data directory of a shipped installation, below the user's home. */
export const INSTALLATION_DATA_DIR_NAME = ".kunbuddy";

/** Data directory of a development installation, below the user's home. */
export const DEVELOPMENT_DATA_DIR_NAME = ".kunbuddy-dev";

export type DataDirInput = {
  /** `KUNBUDDY_DATA_DIR`; an explicit directory wins over either profile. */
  override: string | undefined;
  /** True for a development build. */
  development: boolean;
  /** The user's home directory. */
  home: string;
};

/**
 * The data directory one installation owns.
 *
 * `KUNBUDDY_DATA_DIR` stays the escape hatch it always was: an explicit
 * directory wins, which is how the E2E harnesses, the capture rig, and
 * side-by-side profiles keep choosing their own root. The result is absolute,
 * because it reaches host-core as a child-process environment variable from a
 * working directory that need not be this one, and because `homedir()` is the
 * only other input that could be relative. Without an override the profile
 * picks the name, and that name is the whole difference between the two
 * installations.
 */
export function resolveDataDir({
  override,
  development,
  home,
}: DataDirInput): string {
  const explicit = override?.trim();
  if (explicit) return resolve(explicit);
  return resolve(
    join(
      home,
      development ? DEVELOPMENT_DATA_DIR_NAME : INSTALLATION_DATA_DIR_NAME,
    ),
  );
}

/**
 * The data directory this process owns.
 *
 * Electron main passes its own verdict for `development`, because only it can
 * ask `app.isPackaged`, and it publishes the resolved directory back to
 * `KUNBUDDY_DATA_DIR` at boot. That publication is what keeps the plugin
 * runtime — which resolves this root from the environment rather than taking
 * it as a parameter — on one directory instead of falling back to the shipped
 * default, which would strand a development host's plugin data inside the
 * packaged profile.
 */
export function desktopDataDir(
  development: boolean = process.env.KUNBUDDY_DEV === "1",
): string {
  return resolveDataDir({
    override: process.env.KUNBUDDY_DATA_DIR,
    development,
    home: homedir(),
  });
}

/** The Electron `app` surface this helper needs; kept structural so tests need no Electron. */
export type UserDataApp = {
  commandLine: { hasSwitch(name: string): boolean };
  getPath(name: "appData"): string;
  setPath(name: "userData", path: string): void;
};

/**
 * Give a development build its own `userData` so the single-instance lock
 * does not collide with a shipped app that is already running (D236, ADR 0094).
 * Explicit data profiles also own a Chromium directory in either build mode.
 * An explicit `--user-data-dir` wins, because that is how the E2E harnesses
 * point a build at a throwaway profile.
 */
export function applyDevelopmentUserData(
  app: UserDataApp,
  development: boolean,
  profileDataDir: string | undefined = process.env.KUNBUDDY_DATA_DIR,
): void {
  if (app.commandLine.hasSwitch("user-data-dir")) return;
  if (profileDataDir?.trim()) {
    app.setPath("userData", resolve(profileDataDir.trim(), "electron"));
  } else if (development) {
    app.setPath(
      "userData",
      join(app.getPath("appData"), DEVELOPMENT_INSTALLATION_NAME),
    );
  }
}
