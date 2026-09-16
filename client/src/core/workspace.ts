import { lstat, realpath, opendir, open } from 'node:fs/promises';
import path from 'node:path';
import type { DirectoryPage } from '../shared/contracts';

const forbidden = /^(?:\.env(?:\..*)?|\.ssh|\.aws|\.azure|\.gnupg|\.git|node_modules|\.next|credentials(?:\..*)?|appsettings(?:\..*)?\.json|id_(?:rsa|ed25519|ecdsa)(?:\..*)?|.*\.(?:pem|key|pfx|p12|kdbx))$/i;
export function allowedPath(value: string): string[] {
  if (typeof value !== 'string' || value.length > 4096 || /[\x00-\x1f:]/.test(value) || path.isAbsolute(value) || /^[\\/]/.test(value)) throw new Error('仅允许授权目录中的相对路径');
  const parts = value.split(/[\\/]/).filter(p => p && p !== '.');
  if (parts.some(p => p === '..' || p.endsWith('.') || p.endsWith(' ') || forbidden.test(p))) throw new Error('该路径未获授权或包含敏感文件');
  return parts;
}
export class Workspace {
  private root?: string;
  async grant(selected: string) {
    const canonical = await realpath(selected);
    if (!(await lstat(canonical)).isDirectory()) throw new Error('请选择文件夹');
    if (canonical.split(/[\\/]/).some(p => forbidden.test(p))) throw new Error('不能授权敏感目录');
    this.root = canonical;
    return { name: path.basename(canonical) || canonical };
  }
  revoke() { this.root = undefined; }
  async resolve(relative: string) {
    const root = this.root;
    if (!root) throw new Error('请先授权目录');
    let target = root;
    for (const part of allowedPath(relative)) {
      target = path.join(target, part);
      if ((await lstat(target)).isSymbolicLink()) throw new Error('不允许符号链接或 junction');
    }
    const canonical = await realpath(target);
    const rel = path.relative(root, canonical);
    if (rel === '..' || rel.startsWith(`..${path.sep}`) || path.isAbsolute(rel) || this.root !== root) throw new Error('路径超出授权范围');
    return canonical;
  }
  async list(relative: string): Promise<DirectoryPage> {
    const directory = await opendir(await this.resolve(relative));
    const entries: DirectoryPage['entries'] = [];
    let truncated = false;
    let scanned = 0;
    for await (const item of directory) {
      if (++scanned > 2000 || entries.length >= 500) { truncated = true; break; }
      if (item.isSymbolicLink() || (!item.isDirectory() && !item.isFile())) continue;
      try { allowedPath(item.name); } catch { continue; }
      entries.push({ name: item.name, path: [...allowedPath(relative), item.name].join('/'), directory: item.isDirectory() });
    }
    entries.sort((a, b) => Number(b.directory) - Number(a.directory) || a.name.localeCompare(b.name));
    return { entries, truncated };
  }
  async read(relative: string) {
    const target = await this.resolve(relative);
    const file = await open(target, 'r');
    try {
      const stat = await file.stat();
      if (!stat.isFile() || stat.nlink > 1 || stat.size > 200000) throw new Error('仅支持不超过 200 KB 的普通文本文件（不支持硬链接）');
      // Fixed-size read also bounds files growing after stat.
      const bytes = Buffer.alloc(200001);
      const { bytesRead } = await file.read(bytes, 0, bytes.length, 0);
      if (bytesRead > 200000 || bytes.subarray(0, bytesRead).includes(0)) throw new Error('文件过大或不是 UTF-8 文本');
      if (await this.resolve(relative) !== target) throw new Error('授权目录已发生变化');
      try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes.subarray(0, bytesRead)); }
      catch { throw new Error('只支持 UTF-8 文本'); }
    } finally { await file.close(); }
  }
}
