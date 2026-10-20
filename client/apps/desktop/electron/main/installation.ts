import { app } from "electron";
import { APP_ID, APP_NAME } from "@kunbuddy/shared";
import { applyDevelopmentUserData, desktopDataDir } from "./data-paths";
import { ignoreBrokenStdio } from "./logger";
import { installMainProcessErrorHandlers } from "./main-process-errors";

// Complete identity and locking synchronously, before the ready promise or any writer.
ignoreBrokenStdio();
installMainProcessErrorHandlers();
export const isDevelopmentBuild = process.env.KUNBUDDY_DEV === "1" || !app.isPackaged;
app.setName(APP_NAME);
// A managed restart inherits the root published for children, not an explicit profile override.
if (process.argv.includes("--kunbuddy-managed-storage")) delete process.env.KUNBUDDY_DATA_DIR;
applyDevelopmentUserData(app, isDevelopmentBuild);
if (process.platform === "win32") app.setAppUserModelId(APP_ID);

export const singleInstanceRequired = !process.env.KUNBUDDY_DATA_DIR;
export const hasSingleInstanceLock = singleInstanceRequired ? app.requestSingleInstanceLock() : true;
export const defaultDataDir = desktopDataDir(isDevelopmentBuild);
if (!hasSingleInstanceLock) app.quit();

// Preserve the existing Chromium accessibility crash workaround before ready.
app.commandLine.appendSwitch("disable-renderer-accessibility");
