export const architectureTypes = { architecture: "架构", workflow: "工作流", sequence: "时序图", dataflow: "数据流", lifecycle: "生命周期" } as const;
export type ArchitectureType = keyof typeof architectureTypes;
export type ArchitectureNode = { id: string; label: string; group: string; description: string; source: string; kind?: "component" | "decision" | "store" | "start" | "end" };
export type ArchitectureEdge = { from: string; to: string; label: string; style?: "solid" | "dashed" };
export type Architecture = { version: 1; title: string; diagramType?: ArchitectureType; nodes: ArchitectureNode[]; edges: ArchitectureEdge[] };

function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("图形对象格式错误");
  return value as Record<string, unknown>;
}
function text(value: unknown, max: number, required = false): string {
  if (value === undefined && !required) return "";
  if (typeof value !== "string" || value.length > max || (required && !value.trim())) throw new Error(`图形文字须为 ${max} 字以内的字符串`);
  return value;
}
export function parseArchitecture(source: string): Architecture {
  if (source.length > 100_000) throw new Error("图形超过 10 万字符，请拆分为多个图");
  const data = record(JSON.parse(source));
  if (data.version !== 1) throw new Error("不支持的图形版本，需要 version: 1");
  if (data.diagramType !== undefined && (typeof data.diagramType !== "string" || !Object.hasOwn(architectureTypes, data.diagramType))) throw new Error("图形类型须为 architecture / workflow / sequence / dataflow / lifecycle");
  if (!Array.isArray(data.nodes) || !data.nodes.length || data.nodes.length > 40) throw new Error("图形需要 1–40 个节点");
  if (!Array.isArray(data.edges) || data.edges.length > 100) throw new Error("图形最多支持 100 条关系");
  const ids = new Set<string>();
  const nodes = data.nodes.map((item) => {
    const node = record(item), id = text(node.id, 64, true);
    if (!/^[a-zA-Z][a-zA-Z0-9_-]*$/.test(id) || ids.has(id)) throw new Error("节点 ID 必须唯一且为英文标识符");
    ids.add(id);
    if (node.kind !== undefined && !["component", "decision", "store", "start", "end"].includes(String(node.kind))) throw new Error("不支持的节点形状");
    return { id, label: text(node.label, 80, true), group: text(node.group, 80), description: text(node.description, 2000), source: text(node.source, 1000), ...(node.kind ? { kind: node.kind as ArchitectureNode["kind"] } : {}) };
  });
  const edges = data.edges.map((item) => {
    const edge = record(item), from = text(edge.from, 64, true), to = text(edge.to, 64, true);
    if (!ids.has(from) || !ids.has(to)) throw new Error("关系引用了不存在的节点");
    if (edge.style !== undefined && edge.style !== "solid" && edge.style !== "dashed") throw new Error("不支持的关系线型");
    return { from, to, label: text(edge.label, 100), ...(edge.style ? { style: edge.style as ArchitectureEdge["style"] } : {}) };
  });
  return { version: 1, title: text(data.title, 120, true), ...(data.diagramType ? { diagramType: data.diagramType as ArchitectureType } : {}), nodes, edges };
}

export function relatedNodes(graph: Architecture, start: string, direction: "upstream" | "downstream"): Set<string> {
  const visited = new Set([start]), queue = [start];
  for (let i = 0; i < queue.length; i++) {
    for (const edge of graph.edges) {
      const from = direction === "downstream" ? edge.from : edge.to;
      const to = direction === "downstream" ? edge.to : edge.from;
      if (from === queue[i] && !visited.has(to)) { visited.add(to); queue.push(to); }
    }
  }
  return visited;
}

export function architecturePath(graph: Architecture, start: string, end: string): string[] {
  const queue = [[start]], visited = new Set([start]);
  for (let i = 0; i < queue.length; i++) {
    const path = queue[i], last = path[path.length - 1];
    if (last === end) return path;
    for (const edge of graph.edges) if (edge.from === last && !visited.has(edge.to)) {
      visited.add(edge.to); queue.push([...path, edge.to]);
    }
  }
  return [];
}
