export const PROTOCOL_VERSION = 11 as const;
export const SCHEMA_VERSION = 16 as const;
export const APP_ID = "com.knowwhy.kunbuddy";
export const APP_NAME = "KunBuddy";
export const APP_VERSION = "0.20.3";

export type KunBuddyAccountStatus = {
  state: "signed_out" | "authenticated" | "expired" | "unavailable";
  baseUrl: string;
  username: string;
  expiresAt?: string;
};

export const APP_MENU_COMMANDS = [
  "newTask",
  "openProject",
  "openSettings",
  "openSearch",
  "openCommandPalette",
  "toggleSidebar",
  "openHelp",
  "openLogs",
  "checkForUpdates",
] as const;

export type AppMenuCommand = (typeof APP_MENU_COMMANDS)[number];

export const NATIVE_MENU_ACTIONS = [
  "undo",
  "redo",
  "cut",
  "copy",
  "paste",
  "selectAll",
  "reload",
  "zoomIn",
  "zoomOut",
  "resetZoom",
  "toggleFullScreen",
  "minimize",
  "toggleMaximize",
  "close",
  "restoreMainWindow",
  "toggleMainWindow",
] as const;

export type NativeMenuAction = (typeof NATIVE_MENU_ACTIONS)[number];

export const WINDOW_CONTROL_ACTIONS = [
  "getState",
  "minimize",
  "toggleMaximize",
  "close",
] as const;

export type WindowControlAction = (typeof WINDOW_CONTROL_ACTIONS)[number];

export const IPC = {
  invoke: {
    storageGet: "kunbuddy/storage/get",
    storageChoose: "kunbuddy/storage/choose",
    storageMigrate: "kunbuddy/storage/migrate",
    storageClearCache: "kunbuddy/storage/clearCache",
    storageRemoveBackup: "kunbuddy/storage/removeBackup",
    appGetVersion: "kunbuddy/app/getVersion",
    appOpenFeedback: "kunbuddy/app/openFeedback",
    appHealth: "kunbuddy/app/health",
    appGetOnboarding: "kunbuddy/app/getOnboarding",
    appDismissOnboarding: "kunbuddy/app/dismissOnboarding",
    /**
     * Quit the whole application through the ordered shutdown. Exposed for the
     * surfaces that own the window while the shell has no data yet — a stuck
     * startup must always be able to exit the app (issue #831).
     */
    appQuit: "kunbuddy/app/quit",
    /** Installed system font families, resolved by Electron main. */
    systemFontsList: "kunbuddy/app/systemFonts",
    updatesGetState: "kunbuddy/updates/getState",
    updatesCheck: "kunbuddy/updates/check",
    updatesDownload: "kunbuddy/updates/download",
    updatesInstall: "kunbuddy/updates/install",
    updatesOpenReleases: "kunbuddy/updates/openReleases",
    /** Persist the user's decision to stop nudging about one version (#1317). */
    updatesDismiss: "kunbuddy/updates/dismiss",
    notificationList: "kunbuddy/notification/list",
    notificationMarkRead: "kunbuddy/notification/markRead",
    notificationMarkAllRead: "kunbuddy/notification/markAllRead",
    notificationClear: "kunbuddy/notification/clear",
    notificationShowNative: "kunbuddy/notification/showNative",
    notificationSetViewingSession: "kunbuddy/notification/setViewingSession",
    agentPrompt: "kunbuddy/agent/prompt",
    agentSteer: "kunbuddy/agent/steer",
    speechTranscribe: "kunbuddy/speech/transcribe",
    speechSynthesize: "kunbuddy/speech/synthesize",
    speechGetStatus: "kunbuddy/speech/getStatus",
    voiceStart: "kunbuddy/voice/start",
    voiceStop: "kunbuddy/voice/stop",
    voiceCancel: "kunbuddy/voice/cancel",
    voiceGetState: "kunbuddy/voice/getState",
    voiceGetDevices: "kunbuddy/voice/getDevices",
    voiceGetModels: "kunbuddy/voice/getModels",
    voiceDownloadModel: "kunbuddy/voice/downloadModel",
    voiceDeleteModel: "kunbuddy/voice/deleteModel",
    voiceUpdateSettings: "kunbuddy/voice/updateSettings",
    voiceCheckPermission: "kunbuddy/voice/checkPermission",
    voiceRequestPermission: "kunbuddy/voice/requestPermission",
    liveVoiceStatus: "kunbuddy/voice/live/status",
    liveVoicePrepare: "kunbuddy/voice/live/prepare",
    liveVoiceConnect: "kunbuddy/voice/live/connect",
    liveVoiceSetMuted: "kunbuddy/voice/live/setMuted",
    liveVoiceReportMedia: "kunbuddy/voice/live/reportMedia",
    liveVoiceReportPlayback: "kunbuddy/voice/live/reportPlayback",
    liveVoiceReportDelegation: "kunbuddy/voice/live/reportDelegation",
    liveVoiceReportControlApplied: "kunbuddy/voice/live/reportControlApplied",
    liveVoiceEnd: "kunbuddy/voice/live/end",
    liveVoiceHeartbeat: "kunbuddy/voice/live/heartbeat",
    liveVoiceResolveWorkSelection: "kunbuddy/voice/live/work/resolveSelection",
    liveVoiceStopWorkOperation: "kunbuddy/voice/live/work/stopOperation",
    liveVoiceCancelQueuedOperation: "kunbuddy/voice/live/work/cancelQueuedOperation",
    liveVoiceWidgetAction: "kunbuddy/voice/live/widget/action",
    liveVoiceWidgetOwnerState: "kunbuddy/voice/live/widget/ownerState",
    liveVoiceWidgetVisibility: "kunbuddy/voice/live/widget/visibility",
    agentCompact: "kunbuddy/agent/compact",
    agentAbort: "kunbuddy/agent/abort",
    agentStopSubagents: "kunbuddy/agent/stop-subagents",
    agentStop: "kunbuddy/agent/stop",
    agentQueuePush: "kunbuddy/agent/queue/push",
    agentQueueList: "kunbuddy/agent/queue/list",
    agentQueueRemove: "kunbuddy/agent/queue/remove",
    agentQueuePrioritize: "kunbuddy/agent/queue/prioritize",
    agentQueueReorder: "kunbuddy/agent/queue/reorder",
    agentGetStatus: "kunbuddy/agent/getStatus",
    agentInstructionsGet: "kunbuddy/agent/instructions/get",
    agentInstructionsSave: "kunbuddy/agent/instructions/save",
    sessionList: "kunbuddy/session/list",
    sessionCreate: "kunbuddy/session/create",
    sessionFork: "kunbuddy/session/fork",
    sessionMoveProject: "kunbuddy/session/moveProject",
    sessionSearch: "kunbuddy/session/search",
    sessionSearchContext: "kunbuddy/session/searchContext",
    sessionGet: "kunbuddy/session/get",
    sessionCollaboration: "kunbuddy/session/collaboration",
    /** Validate and select a durable session from a reviewed host operation. */
    sessionOpen: "kunbuddy/session/open",
    sessionDelete: "kunbuddy/session/delete",
    /** Isolated sessions in git worktrees (ADR 0329). */
    worktreeProbe: "kunbuddy/worktree/probe",
    worktreePreview: "kunbuddy/worktree/preview",
    worktreeMerge: "kunbuddy/worktree/merge",
    worktreePendingWork: "kunbuddy/worktree/pendingWork",
    /** "Sync from project": project commits the session lacks, and the sync. */
    worktreeBehind: "kunbuddy/worktree/behind",
    worktreeSync: "kunbuddy/worktree/sync",
    /** Behind counts of every isolated session, for the sidebar marker. */
    worktreeBehindAll: "kunbuddy/worktree/behindAll",
    worktreeSetupLog: "kunbuddy/worktree/setupLog",
    /** Opens the worktree folder in the OS file manager; no path is returned. */
    worktreeReveal: "kunbuddy/worktree/reveal",
    worktreeSetupCommandGet: "kunbuddy/worktree/setupCommand/get",
    worktreeSetupCommandSet: "kunbuddy/worktree/setupCommand/set",
    sessionRename: "kunbuddy/session/rename",
    /** Deterministic first-prompt title for a session that is still untitled. */
    sessionDeriveTitle: "kunbuddy/session/deriveTitle",
    sessionConfigure: "kunbuddy/session/configure",
    sessionImportScan: "kunbuddy/session/importScan",
    sessionImportRun: "kunbuddy/session/importRun",
    modelConfigImportScan: "kunbuddy/modelConfig/importScan",
    modelConfigImportRun: "kunbuddy/modelConfig/importRun",
    sessionReplaceMessages: "kunbuddy/session/replaceMessages",
    sessionSaveRevision: "kunbuddy/session/saveRevision",
    sessionListRevisions: "kunbuddy/session/listRevisions",
    sessionActivateRevision: "kunbuddy/session/activateRevision",
    sessionGetScratchPath: "kunbuddy/session/getScratchPath",
    sessionOpenScratchPath: "kunbuddy/session/openScratchPath",
    projectOpenFolder: "kunbuddy/project/openFolder",
    settingsGet: "kunbuddy/settings/get",
    settingsSet: "kunbuddy/settings/set",
    configSyncGetState: "kunbuddy/configSync/getState",
    configSyncConfigure: "kunbuddy/configSync/configure",
    configSyncTest: "kunbuddy/configSync/test",
    configSyncSyncNow: "kunbuddy/configSync/syncNow",
    configSyncPause: "kunbuddy/configSync/pause",
    configSyncUnlock: "kunbuddy/configSync/unlock",
    configSyncApprove: "kunbuddy/configSync/approve",
    configSyncReject: "kunbuddy/configSync/reject",
    configSyncMapProject: "kunbuddy/configSync/mapProject",
    configSyncListHistory: "kunbuddy/configSync/listHistory",
    configSyncRestore: "kunbuddy/configSync/restore",
    configSyncChangePassword: "kunbuddy/configSync/changePassword",
    configSyncDisconnect: "kunbuddy/configSync/disconnect",
    networkProxyTest: "kunbuddy/network/testProxy",
    commandShellList: "kunbuddy/commandShell/list",
    secretsSet: "kunbuddy/secrets/set",
    secretsDelete: "kunbuddy/secrets/delete",
    secretsHas: "kunbuddy/secrets/has",
    projectOpen: "kunbuddy/project/open",
    projectPickFolders: "kunbuddy/project/pickFolders",
    projectMemoryGet: "kunbuddy/project/memory/get",
    projectMemorySave: "kunbuddy/project/memory/save",
    projectGroupList: "kunbuddy/project-group/list",
    projectGroupCreateRemote: "kunbuddy/project-group/createRemote",
    projectGroupCreate: "kunbuddy/project-group/create",
    projectGroupRename: "kunbuddy/project-group/rename",
    projectGroupUpdate: "kunbuddy/project-group/update",
    projectGroupMemoryGet: "kunbuddy/project-group/memory/get",
    projectGroupMemorySave: "kunbuddy/project-group/memory/save",
    projectGroupInstructionsGet: "kunbuddy/project-group/instructions/get",
    projectGroupInstructionsSave: "kunbuddy/project-group/instructions/save",
    projectClone: "kunbuddy/project/clone",
    projectCloneCheckout: "kunbuddy/project/cloneCheckout",
    projectGet: "kunbuddy/project/get",
    projectList: "kunbuddy/project/list",
    projectSet: "kunbuddy/project/set",
    projectClear: "kunbuddy/project/clear",
    projectRemove: "kunbuddy/project/remove",
    scheduledList: "kunbuddy/scheduled/list",
    scheduledCreate: "kunbuddy/scheduled/create",
    scheduledUpdate: "kunbuddy/scheduled/update",
    scheduledDelete: "kunbuddy/scheduled/delete",
    scheduledRun: "kunbuddy/scheduled/run",
    scheduledExecute: "kunbuddy/scheduled/execute",
    scheduledListRuns: "kunbuddy/scheduled/listRuns",
    toolResolvePermission: "kunbuddy/tool/resolvePermission",
    todosGet: "kunbuddy/todos/get",
    askToolResolve: "kunbuddy/agent/askTool/resolve",
    plansPending: "kunbuddy/plans/pending",
    pendingInteractive: "kunbuddy/agent/pendingInteractive",
    plansResolve: "kunbuddy/plans/resolve",
    /**
     * List every paired remote `kunbuddy-host` this desktop knows, redacted so no
     * device token reaches the renderer. See ADR 0286 (R2b pairing UX).
     */
    remoteHostList: "kunbuddy/remoteHost/list",
    /**
     * Pair with a `kunbuddy-host` at `url` using a single-use `pairingToken`, mint
     * a device token, persist it encrypted, and open the live connection.
     */
    remoteHostPair: "kunbuddy/remoteHost/pair",
    /** Close the live connection for `hostKey` and drop its persisted record. */
    remoteHostRemove: "kunbuddy/remoteHost/remove",
    /**
     * Install and pair a `kunbuddy-host` on a machine the user reaches over SSH:
     * upload the bootstrap script, download and verify the published bundle
     * there, start the host, forward its loopback port, and exchange the
     * pairing token (spec §5.2). Uses the user's own SSH keys; no credential
     * crosses this channel.
     */
    remoteHostBootstrap: "kunbuddy/remoteHost/bootstrap",
    remoteHostReconnect: "kunbuddy/remoteHost/reconnect",
    remoteHostProjects: "kunbuddy/remoteHost/projects",
    remoteHostBrowseProjects: "kunbuddy/remoteHost/browseProjects",
    remoteHostRegisterProject: "kunbuddy/remoteHost/registerProject",
    remoteHostSessions: "kunbuddy/remoteHost/sessions",
    remoteHostCreateSession: "kunbuddy/remoteHost/createSession",
    remoteHostSyncProviders: "kunbuddy/remoteHost/syncProviders",
    providersList: "kunbuddy/providers/list",
    providersReorder: "kunbuddy/providers/reorder",
    providersCreate: "kunbuddy/providers/create",
    kunBuddyAccountStatus: "kunbuddy/account/status",
    kunBuddySubscriptionLogin: "kunbuddy/subscription/login",
    kunBuddySubscriptionLogout: "kunbuddy/subscription/logout",
    kunBuddySessionSyncStatus: "kunbuddy/subscription/sessionSyncStatus",
    kunBuddySessionSyncRetry: "kunbuddy/subscription/sessionSyncRetry",
    providersUpdate: "kunbuddy/providers/update",
    providersDelete: "kunbuddy/providers/delete",
    /**
     * Set or clear one provider's API key. Separate from `providersUpdate`
     * because a plugin-declared row refuses a generic update while still
     * needing the credential its declaration asks for.
     */
    providersSetSecret: "kunbuddy/providers/setSecret",
    providersTest: "kunbuddy/providers/testConnection",
    /**
     * Check a TypeSafe key before the Jev settings row keeps it.
     *
     * Jev is not a provider row, so `providersTest` cannot answer this: the
     * key is checked by the same System One round trip the Agent's
     * `JevClassify` tool makes, and only a key that answered is stored.
     */
    jevTest: "kunbuddy/jev/test",
    providersListModels: "kunbuddy/providers/listModels",
    /**
     * Look one model id up in the local models.dev snapshot.
     *
     * `providersListModels` cannot answer this: it describes a saved or
     * reached provider's catalogue, and a hand-typed custom id exists nowhere
     * yet when the settings picker needs its published limits. This is a
     * snapshot read — no provider network access and no host call — so the
     * picker can seed a custom row without probing an endpoint that does not
     * know the id.
     */
    providersLookupModel: "kunbuddy/providers/lookupModel",
    providersRefreshModelCatalog: "kunbuddy/providers/refreshModelCatalog",
    providersModelCatalogStatus: "kunbuddy/providers/modelCatalogStatus",
    providersOauthVendors: "kunbuddy/providers/oauth/vendors",
    providersOauthStart: "kunbuddy/providers/oauth/start",
    providersOauthRespond: "kunbuddy/providers/oauth/respond",
    providersOauthCancel: "kunbuddy/providers/oauth/cancel",
    providersOauthDelete: "kunbuddy/providers/oauth/delete",
    pluginList: "kunbuddy/plugin/list",
    /** A renderer slot component asking its own plugin for one JSON answer. */
    pluginRendererCall: "kunbuddy/plugin/rendererCall",
    /** Invoke a declared, user-facing Composer transform action. */
    pluginComposerTransform: "kunbuddy/plugin/composerTransform",
    /** Plugin-contributed agent extensions (D387/D388, ADR 0214). */
    pluginImportExtension: "kunbuddy/plugin/importExtension",
    extensionsCommandRun: "kunbuddy/extensions/commands/run",
    extensionsUiRespond: "kunbuddy/extensions/ui/respond",
    pluginLoadDev: "kunbuddy/plugin/loadDev",
    /**
     * The answer to a development plugin's permission review. Loading a folder
     * is a two-step: `pluginLoadDev` returns the declaration, and this commits
     * the permissions the user accepted.
     */
    pluginLoadDevConfirm: "kunbuddy/plugin/loadDevConfirm",
    pluginReload: "kunbuddy/plugin/reload",
    /** Commits a reviewed widening for an already-loaded development plugin. */
    pluginReloadConfirm: "kunbuddy/plugin/reloadConfirm",
    pluginCreateFromTemplate: "kunbuddy/plugin/createFromTemplate",
    pluginInstallFromPath: "kunbuddy/plugin/installFromPath",
    pluginInstallFromPackage: "kunbuddy/plugin/installFromPackage",
    pluginEnable: "kunbuddy/plugin/enable",
    pluginDisable: "kunbuddy/plugin/disable",
    pluginSetScope: "kunbuddy/plugin/setScope",
    pluginUninstall: "kunbuddy/plugin/uninstall",
    pluginSetAutoUpdate: "kunbuddy/plugin/setAutoUpdate",
    pluginSettingsGet: "kunbuddy/plugin/settings/get",
    pluginSettingsSet: "kunbuddy/plugin/settings/set",
    pluginOpenPanel: "kunbuddy/plugin/openPanel",
    pluginLauncherToggle: "kunbuddy/pluginLauncher/toggle",
    pluginLauncherDismiss: "kunbuddy/pluginLauncher/dismiss",
    pluginThemes: "kunbuddy/plugin/themes",
    pluginProviderCatalog: "kunbuddy/plugin/providerCatalog",
    pluginScenicThemesDestinations: "kunbuddy/plugin/scenicThemes/destinations",
    pluginScenicThemesSetBlur: "kunbuddy/plugin/scenicThemes/setBlur",
    pluginServices: "kunbuddy/plugin/services",
    pluginViews: "kunbuddy/plugin/views",
    pluginViewOpen: "kunbuddy/plugin/view/open",
    pluginViewClose: "kunbuddy/plugin/view/close",
    pluginViewSetBounds: "kunbuddy/plugin/view/setBounds",
    pluginViewSetVisible: "kunbuddy/plugin/view/setVisible",
    mcpList: "kunbuddy/mcp/list",
    mcpUpsert: "kunbuddy/mcp/upsert",
    mcpRemove: "kunbuddy/mcp/remove",
    mcpSetEnabled: "kunbuddy/mcp/setEnabled",
    mcpSetScope: "kunbuddy/mcp/setScope",
    mcpTransfer: "kunbuddy/mcp/transfer",
    mcpTest: "kunbuddy/mcp/test",
    mcpOauthStart: "kunbuddy/mcp/oauth/start",
    mcpOauthCancel: "kunbuddy/mcp/oauth/cancel",
    mcpImport: "kunbuddy/mcp/import",
    mcpImportScan: "kunbuddy/mcp/importScan",
    mcpImportRun: "kunbuddy/mcp/importRun",
    mcpMarketSearch: "kunbuddy/mcp/market/search",
    skillList: "kunbuddy/skill/list",
    skillBuiltinList: "kunbuddy/skill/builtin/list",
    skillBuiltinSetEnabled: "kunbuddy/skill/builtin/setEnabled",
    skillCreate: "kunbuddy/skill/create",
    skillImport: "kunbuddy/skill/import",
    skillImportScan: "kunbuddy/skill/importScan",
    skillImportRun: "kunbuddy/skill/importRun",
    skillMarketSearch: "kunbuddy/skill/market/search",
    skillMarketFetch: "kunbuddy/skill/market/fetch",
    skillUpdate: "kunbuddy/skill/update",
    skillRemove: "kunbuddy/skill/remove",
    skillSetEnabled: "kunbuddy/skill/setEnabled",
    skillSetScope: "kunbuddy/skill/setScope",
    skillTransfer: "kunbuddy/skill/transfer",
    skillRead: "kunbuddy/skill/read",
    skillReveal: "kunbuddy/skill/reveal",
    subagentList: "kunbuddy/subagent/list",
    subagentCatalog: "kunbuddy/subagent/catalog",
    subagentCreate: "kunbuddy/subagent/create",
    subagentUpdate: "kunbuddy/subagent/update",
    subagentRead: "kunbuddy/subagent/read",
    subagentRemove: "kunbuddy/subagent/remove",
    subagentSetEnabled: "kunbuddy/subagent/setEnabled",
    subagentSetScope: "kunbuddy/subagent/setScope",
    subagentSetBuiltinEnabled: "kunbuddy/subagent/setBuiltinEnabled",
    subagentReveal: "kunbuddy/subagent/reveal",
    marketRefresh: "kunbuddy/market/refresh",
    marketSearch: "kunbuddy/market/search",
    marketGetDetail: "kunbuddy/market/getDetail",
    marketInstall: "kunbuddy/market/install",
    marketCheckUpdates: "kunbuddy/market/checkUpdates",
    marketApplyUpdates: "kunbuddy/market/applyUpdates",
    marketCancelInstall: "kunbuddy/market/cancelInstall",
    commandPaletteSearch: "kunbuddy/commandPalette/search",
    commandPaletteExecute: "kunbuddy/commandPalette/execute",
    logOpenFolder: "kunbuddy/log/openFolder",
    devtoolsToggle: "kunbuddy/devtools/toggle",
    composerPickFiles: "kunbuddy/composer/pickFiles",
    composerPickPhotos: "kunbuddy/composer/pickPhotos",
    composerImportFiles: "kunbuddy/composer/importFiles",
    composerPasteFiles: "kunbuddy/composer/pasteFiles",
    clipboardRecordPaste: "kunbuddy/clipboard/recordPaste",
    composerCommands: "kunbuddy/composer/commands",
    workspaceDiff: "kunbuddy/workspace/diff",
    workspaceReviewRollback: "kunbuddy/workspace/review/rollback",
    browserNavigate: "kunbuddy/browser/navigate",
    browserAction: "kunbuddy/browser/action",
    browserSetBounds: "kunbuddy/browser/setBounds",
    browserSetVisible: "kunbuddy/browser/setVisible",
    browserOpenExternal: "kunbuddy/browser/openExternal",
    browserGetState: "kunbuddy/browser/getState",
    fsList: "kunbuddy/fs/list",
    fsRead: "kunbuddy/fs/read",
    fsReadImageDataUrl: "kunbuddy/fs/readImageDataUrl",
    statsGetTokenUsageHistory: "kunbuddy/stats/getTokenUsageHistory",
    fsReveal: "kunbuddy/fs/reveal",
    fsOpen: "kunbuddy/fs/open",
    fsIndex: "kunbuddy/fs/index",
    fsResolveRef: "kunbuddy/fs/resolveRef",
    windowSetWorkPanelReservation:
      "kunbuddy/window/setWorkPanelReservation",
    windowSetWorkPanelChatWidth: "kunbuddy/window/setWorkPanelChatWidth",
    windowSetBackgroundColor: "kunbuddy/window/setBackgroundColor",
    windowControl: "kunbuddy/window/control",
    closeBehaviorGet: "kunbuddy/window/closeBehavior/get",
    closeBehaviorSet: "kunbuddy/window/closeBehavior/set",
    menuRendererReady: "kunbuddy/menu/rendererReady",
    traySetSessionPreferences: "kunbuddy/tray/setSessionPreferences",
    nativeMenuAction: "kunbuddy/menu/nativeAction",
  },
  event: {
    pluginChanged: "kunbuddy/event/pluginChanged",
    /** Progress of an install or update, while it is still running. */
    pluginInstallProgress: "kunbuddy/plugin/event/installProgress",
    /** Host-originated app settings mutation (e.g. plugin `app.setTheme`). */
    settingsChanged: "kunbuddy/app/event/settingsChanged",
    configSyncChanged: "kunbuddy/configSync/event/changed",
    /** What a running sync is doing, while it is still running. */
    configSyncProgress: "kunbuddy/configSync/event/progress",
    extensionsUiPrompt: "kunbuddy/extensions/event/uiPrompt",
    extensionsStatus: "kunbuddy/extensions/event/status",
    pluginLauncherShown: "kunbuddy/pluginLauncher/event/shown",
    agentMessage: "kunbuddy/agent/event/message",
    agentQueueChanged: "kunbuddy/agent/event/queueChanged",
    hostStatus: "kunbuddy/app/event/hostStatus",
    toast: "kunbuddy/app/event/toast",
    /**
     * The first plaintext hop to an endpoint the user typed, sent once and only
     * until the shell records `networkPolicy.insecureNoticeAcknowledged`. The
     * shell owns the wording, because the address is not a secret and the copy
     * is localized.
     */
    insecureEndpointNotice: "kunbuddy/network/event/insecureEndpointNotice",
    browserState: "kunbuddy/browser/event/state",
    browserPreview: "kunbuddy/browser/event/preview",
    windowMaximized: "kunbuddy/window/event/maximized",
    windowFullScreen: "kunbuddy/window/event/fullscreen",
    windowWorkPanelResize: "kunbuddy/window/event/workPanelResize",
    menuCommand: "kunbuddy/menu/event/command",
    traySessionActivated: "kunbuddy/tray/event/sessionActivated",
    notificationChanged: "kunbuddy/notification/event/changed",
    sessionsChanged: "kunbuddy/session/event/changed",
    remoteInteractions: "kunbuddy/remote/event/interactions",
    notificationActivated: "kunbuddy/notification/event/activated",
    notificationSound: "kunbuddy/notification/event/sound",
    plansChanged: "kunbuddy/plans/event/changed",
    todosChanged: "kunbuddy/todos/event/changed",
    providersOauth: "kunbuddy/providers/oauth/event",
    mcpOauth: "kunbuddy/mcp/oauth/event",
    updatesState: "kunbuddy/updates/event/state",
    voiceStateChanged: "kunbuddy/voice/event/stateChanged",
    voiceModelProgress: "kunbuddy/voice/event/modelProgress",
    liveVoiceChanged: "kunbuddy/voice/live/event/changed",
    liveVoicePort: "kunbuddy/voice/live/event/port",
    liveVoiceControl: "kunbuddy/voice/live/event/control",
    liveVoiceTranscript: "kunbuddy/voice/live/event/transcript",
    liveVoiceWidgetState: "kunbuddy/voice/live/event/widgetState",
    liveVoiceWidgetAction: "kunbuddy/voice/live/event/widgetAction",
  },
} as const;

export const IPC_WHITELIST = new Set<string>([
  ...Object.values(IPC.invoke),
  ...Object.values(IPC.event),
]);
