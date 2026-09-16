import crypto from 'node:crypto';
import os from 'node:os';
import { randomUUID } from 'node:crypto';
import * as pty from 'node-pty';
import { Client } from 'ssh2';
import { idSchema, sizeSchema, sshSchema, type SshInput, type TerminalOutput } from '../shared/contracts';

const MAX_BUFFER_BYTES = 1_000_000;
const MAX_WRITE_BYTES = 32_768;
type Close = () => void;

interface Session {
  chunks: string[];
  bytes: number;
  ended: boolean;
  reason?: string;
  write(data: string): void;
  resize(cols: number, rows: number): void;
  close: Close;
}

function append(session: Session, data: string) {
  if (!data) return;
  const bytes = Buffer.byteLength(data);
  session.chunks.push(data);
  session.bytes += bytes;
  while (session.bytes > MAX_BUFFER_BYTES && session.chunks.length > 1) {
    session.bytes -= Buffer.byteLength(session.chunks.shift()!);
  }
}

export class TerminalManager {
  private sessions = new Map<string, Session>();

  openLocal() {
    const shell = process.platform === 'win32' ? (process.env.COMSPEC || 'cmd.exe') : (process.env.SHELL || '/bin/sh');
    const processHandle = pty.spawn(shell, [], { name: 'xterm-256color', cols: 100, rows: 30, cwd: os.homedir(), env: { ...process.env, TERM: 'xterm-256color' } });
    const session: Session = {
      chunks: [], bytes: 0, ended: false,
      write: data => processHandle.write(data), resize: (cols, rows) => processHandle.resize(cols, rows),
      close: () => { try { processHandle.kill(); } catch { /* already closed */ } },
    };
    processHandle.onData(data => append(session, data));
    processHandle.onExit(({ exitCode }) => { session.ended = true; session.reason = `Local shell exited (${exitCode}).`; });
    return this.store(session);
  }

  async openSsh(input: SshInput) {
    const values = sshSchema.parse(input);
    const client = new Client();
    const channel = await new Promise<any>((resolve, reject) => {
      let settled = false;
      const fail = (error: Error) => { if (!settled) { settled = true; client.end(); reject(error); } };
      client.once('error', fail);
      client.on('ready', () => client.shell({ term: 'xterm-256color', cols: 100, rows: 30 }, (error, stream) => {
        if (error) return fail(error);
        settled = true; resolve(stream);
      }));
      client.connect({ host: values.host, port: values.port, username: values.username, password: values.password, readyTimeout: 30_000,
        hostVerifier: (key: Buffer) => crypto.createHash('sha256').update(key).digest('base64') === values.fingerprint.slice('SHA256:'.length) });
    });
    const session: Session = {
      chunks: [], bytes: 0, ended: false,
      write: data => channel.write(data), resize: (cols, rows) => channel.setWindow(rows, cols, 0, 0),
      close: () => { channel.close(); client.end(); },
    };
    channel.on('data', (data: Buffer) => append(session, data.toString('utf8')));
    channel.stderr.on('data', (data: Buffer) => append(session, data.toString('utf8')));
    channel.on('close', () => { session.ended = true; session.reason = 'SSH terminal disconnected.'; client.end(); });
    return this.store(session);
  }

  poll(id: string): TerminalOutput {
    const session = this.get(id);
    const data = session.chunks.join(''); session.chunks = []; session.bytes = 0;
    return { data, ended: session.ended, reason: session.reason };
  }
  write(id: string, data: string) {
    if (typeof data !== 'string' || Buffer.byteLength(data) > MAX_WRITE_BYTES) throw new Error('Terminal input exceeds 32 KB.');
    const session = this.get(id); if (session.ended) throw new Error('Terminal session has ended.'); session.write(data);
  }
  resize(id: string, size: { cols: number; rows: number }) { const value = sizeSchema.parse(size); this.get(id).resize(value.cols, value.rows); }
  close(id: string) { const session = this.get(id); this.sessions.delete(id); session.close(); }
  closeAll() { for (const id of [...this.sessions.keys()]) this.close(id); }
  private store(session: Session) { const id = randomUUID(); this.sessions.set(id, session); return id; }
  private get(id: string) { return this.sessions.get(idSchema.parse(id)) ?? (() => { throw new Error('Terminal session was not found.'); })(); }
}
