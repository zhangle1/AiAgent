export function matchesChatProjectSearch(project: { label: string; rootPath?: string }, query: string): boolean {
  const term = query.trim().toLocaleLowerCase().replace(/\\/g, "/");
  if (!term) return true;
  const path = (project.rootPath ?? "").toLocaleLowerCase().replace(/\\/g, "/").replace(/\/+$/, "");
  // Shared parent directories must not make every project match a name search.
  const searchablePath = /[/:]/.test(term) ? path : path.split("/").at(-1) ?? "";
  return project.label.toLocaleLowerCase().includes(term) || searchablePath.includes(term);
}
