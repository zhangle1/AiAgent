import type { AtlasImportGraph } from './change-atlas-types';

export function selectImportNeighborhood(graph: AtlasImportGraph, seed: string, reverse: boolean, depth: number) {
  const nodes = new Set([seed]);
  const edges = new Map<string, AtlasImportGraph['edges'][number]>();
  let frontier = new Set([seed]); let truncated = false;
  for (let hop = 0; hop < Math.min(2, Math.max(1, depth)); hop++) {
    const next = new Set<string>();
    for (const edge of graph.edges) {
      const from = reverse ? edge.target : edge.source, to = reverse ? edge.source : edge.target;
      if (!frontier.has(from)) continue;
      if ((!nodes.has(to) && nodes.size >= 60) || (!edges.has(edge.id) && edges.size >= 120)) { truncated = true; continue; }
      edges.set(edge.id, edge);
      if (!nodes.has(to)) { nodes.add(to); next.add(to); }
    }
    frontier = next;
  }
  return { files: graph.files.filter(file => nodes.has(file.id)), edges: [...edges.values()], truncated };
}
