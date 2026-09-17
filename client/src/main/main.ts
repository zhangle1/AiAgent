import { app, BrowserWindow, dialog, ipcMain } from 'electron';
import path from 'node:path';
import { BackendClient } from '../core/backend';
import { TerminalManager } from '../core/terminal';
import { Workspace } from '../core/workspace';
import { analyzeSchema, loginSchema, pathSchema, sizeSchema, sshSchema } from '../shared/contracts';

const backend = new BackendClient();
const workspace = new Workspace();
const terminals = new TerminalManager();
let mainWindow: BrowserWindow | undefined;
const invoke = <T>(channel: string, handler: (event: Electron.IpcMainInvokeEvent, value: unknown) => Promise<T> | T) => ipcMain.handle(channel, handler);

function createWindow() {
  mainWindow = new BrowserWindow({ width: 1280, height: 820, minWidth: 980, minHeight: 650, show: false,
    webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, sandbox: true, nodeIntegration: false } });
  mainWindow.removeMenu();
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  mainWindow.webContents.on('will-navigate', event => event.preventDefault());
  mainWindow.once('ready-to-show', () => mainWindow?.show());
  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));
}

app.whenReady().then(() => {
  invoke('auth:login', (_event, value) => backend.login(loginSchema.parse(value)));
  invoke('auth:logout', () => { backend.logout(); return undefined; });
  invoke('analysis:start', (event, value) => backend.analyze(analyzeSchema.parse(value), text => { if (!event.sender.isDestroyed()) event.sender.send('analysis:delta', text); }));
  invoke('analysis:cancel', () => { backend.cancelAnalysis(); return undefined; });
  invoke('workspace:choose', async () => {
    const choice = await dialog.showOpenDialog(mainWindow!, { properties: ['openDirectory', 'createDirectory'] });
    return choice.canceled || !choice.filePaths[0] ? null : workspace.grant(choice.filePaths[0]);
  });
  invoke('workspace:revoke', () => { workspace.revoke(); return undefined; });
  invoke('workspace:list', (_event, value) => workspace.list(pathSchema.parse(value)));
  invoke('workspace:read', (_event, value) => workspace.read(pathSchema.parse(value)));
  invoke('terminal:local', () => terminals.openLocal());
  invoke('terminal:ssh', (_event, value) => terminals.openSsh(sshSchema.parse(value)));
  invoke('terminal:poll', (_event, value) => terminals.poll(String(value)));
  invoke('terminal:write', (_event, value) => { const item = value as { id: string; data: string }; terminals.write(item.id, item.data); return undefined; });
  invoke('terminal:resize', (_event, value) => { const item = value as { id: string; size: unknown }; terminals.resize(item.id, sizeSchema.parse(item.size)); return undefined; });
  invoke('terminal:close', (_event, value) => { terminals.close(String(value)); return undefined; });
  invoke('client:disconnect', () => { backend.logout(); workspace.revoke(); terminals.closeAll(); return undefined; });
  createWindow();
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
});
app.on('window-all-closed', () => { terminals.closeAll(); if (process.platform !== 'darwin') app.quit(); });
