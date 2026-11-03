import type { AtlasCommit, AtlasFile, AtlasFilters, AtlasModule, AtlasSnapshot } from "./change-atlas-types";

export const atlasDay = (value: string) => value.slice(0, 10);
export type AtlasTreeNode = { name: string; path: string; file?: AtlasFile; children: AtlasTreeNode[] };
export function atlasFileTree(files: AtlasFile[]): AtlasTreeNode[] {
  const roots: AtlasTreeNode[] = [];
  for (const file of files) {
    let children = roots;
    const parts = file.path.split("/").filter(Boolean);
    parts.forEach((name, index) => {
      const path = parts.slice(0, index + 1).join("/");
      let node = children.find(item => item.path === path);
      if (!node) { node = { name, path, children: [] }; children.push(node); }
      if (index === parts.length - 1) node.file = file;
      children = node.children;
    });
  }
  function sort(nodes: AtlasTreeNode[]) {
    nodes.sort((a, b) => Number(!!a.file) - Number(!!b.file) || a.name.localeCompare(b.name));
    nodes.forEach(node => sort(node.children));
  }
  sort(roots);
  return roots;
}
export function selectAtlasCommits(snapshot: AtlasSnapshot, filters: AtlasFilters): AtlasCommit[] {
  return snapshot.commits.filter(commit => atlasDay(commit.committed_at) <= filters.until && (!filters.daily || atlasDay(commit.committed_at) === filters.until) && (!filters.authorId || commit.author_id === filters.authorId));
}
export function atlasDays(snapshot: AtlasSnapshot): string[] {
  const start = Date.parse(snapshot.start_at);
  return Array.from({ length: snapshot.days }, (_, i) => new Date(start + i * 86_400_000).toISOString().slice(0, 10));
}
export type AtlasModuleStat = AtlasModule & { commits: AtlasCommit[]; changedFiles: AtlasFile[]; authors: number; totalFiles: number };
export function atlasModuleStats(snapshot: AtlasSnapshot, commits: AtlasCommit[]): AtlasModuleStat[] {
  const fileMap = new Map(snapshot.files.map(file => [file.id, file]));
  const moduleCommits = new Map<string, Set<AtlasCommit>>();
  const moduleFiles = new Map<string, Set<string>>();
  const totals = new Map<string, number>();
  snapshot.files.forEach(file => { if (file.at_head) totals.set(file.module_id, (totals.get(file.module_id) || 0) + 1); });
  for (const commit of commits) {
    for (const change of commit.files) {
      const file = fileMap.get(change.file_id); if (!file) continue;
      if (!moduleCommits.has(file.module_id)) moduleCommits.set(file.module_id, new Set());
      if (!moduleFiles.has(file.module_id)) moduleFiles.set(file.module_id, new Set());
      moduleCommits.get(file.module_id)!.add(commit); moduleFiles.get(file.module_id)!.add(file.id);
    }
  }
  return snapshot.modules.map(module => {
    const related = Array.from(moduleCommits.get(module.id) || []);
    return { ...module, commits: related, changedFiles: Array.from(moduleFiles.get(module.id) || []).map(id => fileMap.get(id)!), authors: new Set(related.map(x => x.author_id)).size, totalFiles: totals.get(module.id) || 0 };
  }).sort((a, b) => b.commits.length - a.commits.length || a.directory.localeCompare(b.directory));
}
export function atlasCochange(snapshot: AtlasSnapshot, commits: AtlasCommit[], moduleId: string) {
  const files = new Map(snapshot.files.map(file => [file.id, file.module_id]));
  const counts = new Map<string, number>();
  for (const commit of commits) {
    if (commit.is_merge) continue;
    const modules = new Set(commit.files.map(file => files.get(file.file_id)).filter((id): id is string => !!id));
    if (!modules.has(moduleId)) continue;
    for (const id of modules) if (id !== moduleId) counts.set(id, (counts.get(id) || 0) + 1);
  }
  return Array.from(counts, ([id, count]) => ({ module: snapshot.modules.find(module => module.id === id)!, count })).filter(item => item.module).sort((a, b) => b.count - a.count);
}
