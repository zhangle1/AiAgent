import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import type { UpdateState } from "@kunbuddy/shared";
import { api } from "../lib/api";
import { useUpdateState } from "../hooks/use-update-state";
import { Button, TooltipButton } from "./ui";
import { IconClose, IconCloudDown, IconExternal } from "./icons";

const shownManualReminderVersions = new Set<string>();

/**
 * Ambient update notice in the main pane's top safe area. Appears when an
 * update is ready to install (in-app mode) or newly detected (manual mode);
 * silent otherwise. The Settings → Info tab owns explicit checks and status.
 * When Main attaches localized release notes, they appear under the status
 * message as a compact "What's new" list (D164).
 */
export function UpdateBanner() {
  const { t } = useTranslation();
  const update = useUpdateState();
  const manualReminderVersion =
    update?.status === "available" &&
    update.mode === "manual" &&
    update.manualReminder === true
      ? update.availableVersion
      : undefined;
  const [presentedManualReminder, setPresentedManualReminder] = useState<
    string | null
  >(() =>
    manualReminderVersion && shownManualReminderVersions.has(manualReminderVersion)
      ? manualReminderVersion
      : null,
  );
  useEffect(() => {
    if (!manualReminderVersion) return;
    if (shownManualReminderVersions.has(manualReminderVersion)) {
      setPresentedManualReminder(manualReminderVersion);
      return;
    }
    shownManualReminderVersions.add(manualReminderVersion);
  }, [manualReminderVersion]);
  const [dismissedState, setDismissedState] = useState<string | null>(null);

  if (!update?.availableVersion) return null;
  const stateKey = `${update.sourceKey ?? "legacy"}:${update.availableVersion}:${update.status}`;
  if (stateKey === dismissedState || update.dismissed === true) return null;

  if (update.delivery === "platform-installer") return <div role="status" aria-live="polite" className="update-notice" data-update-status={update.status}>
    <IconCloudDown className="size-4" />
    <div className="update-notice-body"><div className="update-notice-title">发现坤伴新版本 {update.availableVersion}</div>
      <div className="update-notice-message">{update.status === "downloading" ? `正在下载 ${Math.round(update.progressPercent ?? 0)}%` : update.status === "downloaded" ? "安装包已校验。打开后完成安装，Mac 请将应用拖入 Applications 替换。" : update.error ?? "来自当前登录平台的客户端更新。"}</div>
      {update.releaseNotes && <pre className="update-notice-notes-body">{update.releaseNotes}</pre>}
      {update.status === "downloading" && <progress aria-label="更新下载进度" max={100} value={update.progressPercent ?? 0} />}
      <div className="update-notice-actions">
        {update.status === "available" && <Button variant="primary" size="sm" onClick={() => void api.updatesDownload().catch(() => undefined)}>下载安装包</Button>}
        {update.status === "downloaded" && <Button variant="primary" size="sm" disabled={!update.canInstall} onClick={() => void api.updatesInstall().catch(() => undefined)}>打开安装包并退出</Button>}
        {update.status === "error" && <Button size="sm" onClick={() => void api.updatesCheck().catch(() => undefined)}>重新检查</Button>}
        {update.status === "downloaded" && !update.canInstall && <span className="update-notice-message">开发模式仅验证下载，请在打包后的客户端安装。</span>}
      </div>
    </div><TooltipButton tooltip="稍后更新" ariaLabel="稍后更新" className="update-notice-dismiss" onClick={() => void api.updatesDismiss()}><IconClose className="size-3.5" /></TooltipButton>
  </div>;

  const visible =
    update.status === "downloaded" ||
    update.status === "downloading" ||
    (update.status === "available" &&
      update.mode === "manual" &&
      update.manualReminder === true &&
      presentedManualReminder !== update.availableVersion);
  if (!visible) return null;

  const message = bannerMessage(update, t);
  const progress = Math.max(0, Math.min(100, update.progressPercent ?? 0));
  const notes = update.releaseNotes?.trim() || null;

  return (
    <div
      role="status"
      aria-live="polite"
      aria-atomic="true"
      className="update-notice"
      data-update-status={update.status}
    >
      <div className="update-notice-icon" aria-hidden="true">
        <IconCloudDown className="size-4" />
      </div>

      <div className="update-notice-body">
        <div className="update-notice-title">{t("updates.title")}</div>
        <div className="update-notice-message">{message}</div>

        {notes ? (
          <div className="update-notice-notes">
            <div className="update-notice-notes-label">{t("updates.whatsNew")}</div>
            <pre className="update-notice-notes-body">{notes}</pre>
          </div>
        ) : null}

        {update.status === "downloading" && (
          <div
            className="update-notice-progress"
            role="progressbar"
            aria-label={message}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(progress)}
          >
            <span style={{ width: `${progress}%` }} />
          </div>
        )}

        <div className="update-notice-actions">
          {update.status === "downloaded" && (
            <Button
              variant="primary"
              size="sm"
              onClick={() => void api.updatesInstall().catch(() => undefined)}
            >
              {t("updates.restart")}
            </Button>
          )}
          {update.status === "available" && update.mode === "manual" && (
            <Button
              variant="secondary"
              size="sm"
              onClick={() =>
                void api.updatesOpenReleases().catch(() => undefined)
              }
            >
              <IconExternal className="size-3.5" />
              {t("updates.viewRelease")}
            </Button>
          )}
        </div>
      </div>

      <TooltipButton
        type="button"
        tooltip={t("updates.dismiss")}
        ariaLabel={t("updates.dismiss")}
        className="update-notice-dismiss"
        onClick={() => {
          setDismissedState(stateKey);
          void api.updatesDismiss().catch(() => undefined);
        }}
      >
        <IconClose className="size-3.5" />
      </TooltipButton>
    </div>
  );
}

function bannerMessage(
  update: UpdateState,
  t: (key: string, options?: Record<string, unknown>) => string,
): string {
  if (update.status === "downloading") {
    return t("updates.downloading", { percent: update.progressPercent ?? 0 });
  }
  if (update.status === "downloaded") {
    return t("updates.downloaded", { version: update.availableVersion });
  }
  return t("updates.available", { version: update.availableVersion });
}
