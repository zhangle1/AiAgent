import { z } from 'zod';

export const loginSchema = z.object({ address: z.string().min(1).max(2048), username: z.string().min(1).max(200), password: z.string().min(1).max(2000), allowHttp: z.boolean() }).strict();
export const analyzeSchema = z.object({ prompt: z.string().trim().min(1).max(20000), context: z.string().max(200000) }).strict();
export const pathSchema = z.string().max(4096);
export const idSchema = z.string().uuid();
export const sizeSchema = z.object({ cols: z.number().int().min(10).max(500), rows: z.number().int().min(2).max(200) }).strict();
export const sshSchema = z.object({ host: z.string().regex(/^[a-zA-Z0-9.:[\]-]+$/).max(253), port: z.number().int().min(1).max(65535), username: z.string().min(1).max(200), password: z.string().min(1).max(2000), fingerprint: z.string().regex(/^SHA256:[A-Za-z0-9+/]{43}=?$/) }).strict();
export type LoginInput = z.infer<typeof loginSchema>;
export type SshInput = z.infer<typeof sshSchema>;
export interface WorkspaceInfo { name: string }
export interface FileEntry { name: string; path: string; directory: boolean }
export interface DirectoryPage { entries: FileEntry[]; truncated: boolean }
export interface TerminalOutput { data: string; ended: boolean; reason?: string }
export interface ClientApi {
  login(input: LoginInput): Promise<{ username: string; codexAvailable: boolean }>;
  logout(): Promise<void>;
  analyze(input: { prompt: string; context: string }): Promise<{ answer: string }>;
  onAnalysisDelta(callback: (text: string) => void): () => void;
  cancelAnalysis(): Promise<void>;
  chooseWorkspace(): Promise<WorkspaceInfo | null>;
  revokeWorkspace(): Promise<void>;
  list(path: string): Promise<DirectoryPage>;
  read(path: string): Promise<string>;
  openLocal(): Promise<string>;
  openSsh(input: SshInput): Promise<string>;
  terminalPoll(id: string): Promise<TerminalOutput>;
  terminalWrite(id: string, data: string): Promise<void>;
  terminalResize(id: string, size: { cols: number; rows: number }): Promise<void>;
  terminalClose(id: string): Promise<void>;
  disconnect(): Promise<void>;
}
declare global { interface Window { aiagent: ClientApi } }
