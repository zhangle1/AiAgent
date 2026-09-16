import { contextBridge, ipcRenderer } from 'electron';
import type { ClientApi, LoginInput, SshInput } from '../shared/contracts';

const api: ClientApi = {
  login: input => ipcRenderer.invoke('auth:login', input as LoginInput), logout: () => ipcRenderer.invoke('auth:logout'),
  analyze: input => ipcRenderer.invoke('analysis:start', input), cancelAnalysis: () => ipcRenderer.invoke('analysis:cancel'),
  chooseWorkspace: () => ipcRenderer.invoke('workspace:choose'), revokeWorkspace: () => ipcRenderer.invoke('workspace:revoke'),
  list: value => ipcRenderer.invoke('workspace:list', value), read: value => ipcRenderer.invoke('workspace:read', value),
  openLocal: () => ipcRenderer.invoke('terminal:local'), openSsh: input => ipcRenderer.invoke('terminal:ssh', input as SshInput),
  terminalPoll: id => ipcRenderer.invoke('terminal:poll', id), terminalWrite: (id, data) => ipcRenderer.invoke('terminal:write', { id, data }),
  terminalResize: (id, size) => ipcRenderer.invoke('terminal:resize', { id, size }), terminalClose: id => ipcRenderer.invoke('terminal:close', id),
  disconnect: () => ipcRenderer.invoke('client:disconnect'),
};
contextBridge.exposeInMainWorld('aiagent', Object.freeze(api));
