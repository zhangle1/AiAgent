import { homedir } from "node:os";
import { join, resolve } from "node:path";

/** First-party configuration stays inside the active KunBuddy installation. */
export function kunBuddyAgentDir(
  env: NodeJS.ProcessEnv = process.env,
  home: string = homedir(),
): string {
  const dataDir = env.KUNBUDDY_DATA_DIR?.trim()
    || join(home, env.KUNBUDDY_DEV === "1" ? ".kunbuddy-dev" : ".kunbuddy");
  return join(resolve(dataDir), "agent");
}
