import type { Architecture } from "./chat-architecture";

export const NODE_WIDTH = 184, NODE_HEIGHT = 80;
export type Point = { x: number; y: number };
export type Boundary = Point & { width: number; height: number; label: string };
export type DiagramLayout = { positions: Map<string, Point>; boundaries: Boundary[]; width: number; height: number };

// Collapse strongly connected components before ranking: cycles never stretch a
// workflow indefinitely and disconnected components retain stable input order.
function ranks(graph: Architecture): Map<string, number> {
  const next = new Map(graph.nodes.map((n) => [n.id, graph.edges.filter((e) => e.from === n.id).map((e) => e.to)]));
  const index = new Map<string, number>(), low = new Map<string, number>(), stack: string[] = [], active = new Set<string>();
  const component = new Map<string, number>();
  let cursor = 0, count = 0;
  function visit(id: string) {
    index.set(id, cursor); low.set(id, cursor++); stack.push(id); active.add(id);
    for (const to of next.get(id)!) {
      if (!index.has(to)) { visit(to); low.set(id, Math.min(low.get(id)!, low.get(to)!)); }
      else if (active.has(to)) low.set(id, Math.min(low.get(id)!, index.get(to)!));
    }
    if (low.get(id) === index.get(id)) {
      let member: string;
      do { member = stack.pop()!; active.delete(member); component.set(member, count); } while (member !== id);
      count++;
    }
  }
  graph.nodes.forEach((n) => { if (!index.has(n.id)) visit(n.id); });
  const levels = Array<number>(count).fill(0);
  for (let pass = 0; pass < count; pass++) for (const e of graph.edges) {
    const a = component.get(e.from)!, b = component.get(e.to)!;
    if (a !== b) levels[b] = Math.max(levels[b], levels[a] + 1);
  }
  return new Map(graph.nodes.map((n) => [n.id, levels[component.get(n.id)!]]));
}

export function layoutArchitecture(graph: Architecture, offsets: Record<string, Point> = {}): DiagramLayout {
  const type = graph.diagramType ?? "architecture", positions = new Map<string, Point>();
  const groups = [...new Set(graph.nodes.map((n) => n.group || "未分组"))];
  const boundaries: Boundary[] = [];
  if (type === "sequence") {
    graph.nodes.forEach((n, i) => positions.set(n.id, { x: 64 + i * 264, y: 56 }));
  } else if (type === "lifecycle") {
    const count = graph.nodes.length, columns = Math.ceil(count / 2);
    graph.nodes.forEach((n, i) => positions.set(n.id, {
      x: 80 + (i < columns ? i : count - i - 1) * 280,
      y: i < columns ? 80 : 300,
    }));
  } else if (type === "architecture") {
    // Pack domain boundaries in rows, rather than one unbounded column per group.
    const columns = Math.min(3, Math.ceil(Math.sqrt(groups.length)));
    const sizes = groups.map((g) => {
      const members = graph.nodes.filter((n) => (n.group || "未分组") === g);
      const cols = Math.min(2, members.length);
      return { members, cols, width: cols * 240 + 24, height: Math.ceil(members.length / cols) * 140 + 56 };
    });
    const widths = Array.from({ length: columns }, (_, c) => Math.max(...sizes.filter((_, i) => i % columns === c).map((s) => s.width)));
    let y = 48;
    for (let row = 0; row < Math.ceil(groups.length / columns); row++) {
      let x = 48;
      for (let col = 0; col < columns; col++) {
        const i = row * columns + col, size = sizes[i];
        if (!size) break;
        size.members.forEach((n, j) => positions.set(n.id, { x: x + 28 + (j % size.cols) * 240, y: y + 52 + Math.floor(j / size.cols) * 140 }));
        x += widths[col] + 96;
      }
      y += Math.max(...sizes.slice(row * columns, (row + 1) * columns).map((s) => s.height)) + 96;
    }
  } else {
    const levels = ranks(graph), max = Math.max(...levels.values());
    const layers = Array.from({ length: max + 1 }, (_, i) => graph.nodes.filter((n) => levels.get(n.id) === i));
    const breadth = Math.max(...layers.map((layer) => layer.length));
    layers.forEach((layer, level) => layer.forEach((n, i) => {
      const cross = i + (breadth - layer.length) / 2;
      positions.set(n.id, type === "workflow" ? { x: 80 + cross * 264, y: 64 + level * 180 } : { x: 80 + level * 300, y: 64 + cross * 160 });
    }));
  }
  for (const n of graph.nodes) if (offsets[n.id]) {
    const p = offsets[n.id];
    positions.set(n.id, type === "sequence" ? { x: p.x, y: 56 } : p);
  }
  if (type === "architecture") for (const label of groups) {
    const members = graph.nodes.filter((n) => (n.group || "未分组") === label).map((n) => positions.get(n.id)!);
    const x = Math.min(...members.map((p) => p.x)) - 28, y = Math.min(...members.map((p) => p.y)) - 52;
    boundaries.push({ label, x, y, width: Math.max(...members.map((p) => p.x)) + NODE_WIDTH + 28 - x, height: Math.max(...members.map((p) => p.y)) + NODE_HEIGHT + 28 - y });
  }
  return { positions, boundaries,
    width: Math.max(560, ...[...positions.values()].map((p) => p.x + NODE_WIDTH + 100)),
    height: type === "sequence" ? 240 + graph.edges.length * 76 : Math.max(320, ...[...positions.values()].map((p) => p.y + NODE_HEIGHT + 100)) };
}

export function diagramRoutes(graph: Architecture, layout: DiagramLayout) {
  const occupied: Point[][] = [];
  return graph.edges.map((_, index) => {
    const route = edgeRoute(graph, layout, index, occupied);
    occupied.push([...route.d.matchAll(/[ML]([\d.]+),([\d.]+)/g)].map((m) => ({ x: +m[1], y: +m[2] })));
    return route;
  });
}

export function edgeRoute(graph: Architecture, layout: DiagramLayout, index: number, occupied: Point[][] = []): { d: string; label: Point } {
  const e = graph.edges[index], a = layout.positions.get(e.from)!, b = layout.positions.get(e.to)!;
  const w = NODE_WIDTH, h = NODE_HEIGHT;
  if (graph.diagramType === "sequence") {
    const y = 184 + index * 76, x = a.x + w / 2, end = b.x + w / 2;
    return e.from === e.to
      ? { d: `M${x},${y} h56 v30 h-56`, label: { x: x + 60, y: y - 10 } }
      : { d: `M${x},${y} H${end}`, label: { x: (x + end) / 2, y: y - 12 } };
  }
  const siblings = graph.edges.slice(0, index).filter((other) => other.from === e.from && other.to === e.to).length;
  const lane = 22 + siblings * 18;
  if (e.from === e.to) return { d: `M${a.x + w},${a.y + 20} h${lane + 18} v40 H${a.x + w}`, label: { x: a.x + w + lane, y: a.y + 12 } };
  const routed = avoidNodes(layout, a, b, lane, occupied);
  if (routed) return routed;
  const vertical = graph.diagramType === "workflow" || Math.abs(a.x - b.x) < w;
  if (vertical) {
    const down = b.y > a.y, sy = a.y + (down ? h : 0), ey = b.y + (down ? 0 : h);
    const x1 = a.x + w / 2, x2 = b.x + w / 2;
    if (Math.abs(a.y - b.y) > h) {
      const mid = (sy + ey) / 2 + siblings * 16;
      return { d: `M${x1},${sy} V${mid} H${x2} V${ey}`, label: { x: (x1 + x2) / 2 + (x1 === x2 ? 42 : 0), y: mid - 9 } };
    }
    const top = Math.min(a.y, b.y) - lane;
    return { d: `M${x1},${a.y} V${top} H${x2} V${b.y}`, label: { x: (x1 + x2) / 2, y: top - 9 } };
  }
  const right = b.x > a.x, sx = a.x + (right ? w : 0), ex = b.x + (right ? 0 : w);
  const y1 = a.y + h / 2, y2 = b.y + h / 2;
  const mid = (sx + ex) / 2 + siblings * 16;
  return { d: `M${sx},${y1} H${mid} V${y2} H${ex}`, label: { x: mid, y: (y1 + y2) / 2 - 10 } };
}

// Score orthogonal routes against every node, including the endpoints. Short
// stubs enforce outward exits and prevent a return edge crossing its own card.
function avoidNodes(layout: DiagramLayout, a: Point, b: Point, lane: number, occupied: Point[][]) {
  const ports = (p: Point) => [
    [{ x: p.x, y: p.y + 40 }, { x: p.x - lane, y: p.y + 40 }],
    [{ x: p.x + NODE_WIDTH, y: p.y + 40 }, { x: p.x + NODE_WIDTH + lane, y: p.y + 40 }],
    [{ x: p.x + 92, y: p.y }, { x: p.x + 92, y: p.y - lane }],
    [{ x: p.x + 92, y: p.y + 80 }, { x: p.x + 92, y: p.y + 80 + lane }],
  ];
  const obstacles = [...layout.positions.values()];
  let best: Point[] | undefined, score = Infinity;
  for (const [start, s] of ports(a)) for (const [end, t] of ports(b)) {
    const candidates = [
      [s, { x: t.x, y: s.y }, t], [s, { x: s.x, y: t.y }, t],
      ...[(s.x + t.x) / 2, Math.min(a.x, b.x) - lane, Math.max(a.x, b.x) + NODE_WIDTH + lane].map((x) => [s, { x, y: s.y }, { x, y: t.y }, t]),
      ...[(s.y + t.y) / 2, Math.min(a.y, b.y) - lane, Math.max(a.y, b.y) + NODE_HEIGHT + lane].map((y) => [s, { x: s.x, y }, { x: t.x, y }, t]),
    ];
    for (const middle of candidates) {
      const points = [start, ...middle, end].filter((p, i, list) => !i || p.x !== list[i - 1].x || p.y !== list[i - 1].y);
      if (points.some((p) => p.x < 12 || p.y < 12 || p.x > layout.width - 12 || p.y > layout.height - 12)) continue;
      let cost = points.length * 12;
      for (let i = 1; i < points.length; i++) {
        const p = points[i - 1], q = points[i];
        cost += Math.abs(p.x - q.x) + Math.abs(p.y - q.y);
        for (const path of occupied) for (let j = 1; j < path.length; j++) {
          const u = path[j - 1], v = path[j];
          if (p.x === q.x && u.x === v.x && Math.abs(p.x - u.x) < 12) cost += 6 * Math.max(0, Math.min(Math.max(p.y, q.y), Math.max(u.y, v.y)) - Math.max(Math.min(p.y, q.y), Math.min(u.y, v.y)));
          if (p.y === q.y && u.y === v.y && Math.abs(p.y - u.y) < 12) cost += 6 * Math.max(0, Math.min(Math.max(p.x, q.x), Math.max(u.x, v.x)) - Math.max(Math.min(p.x, q.x), Math.min(u.x, v.x)));
        }
        for (const box of obstacles) {
          const crosses = p.x === q.x
            ? p.x > box.x && p.x < box.x + NODE_WIDTH && Math.max(p.y, q.y) > box.y && Math.min(p.y, q.y) < box.y + NODE_HEIGHT
            : p.y > box.y && p.y < box.y + NODE_HEIGHT && Math.max(p.x, q.x) > box.x && Math.min(p.x, q.x) < box.x + NODE_WIDTH;
          if (crosses) cost += 100000;
        }
      }
      if (cost < score) { best = points; score = cost; }
    }
  }
  if (!best) return null;
  let longest = 0, label = best[0];
  for (let i = 1; i < best.length; i++) {
    const p = best[i - 1], q = best[i], length = Math.abs(p.x - q.x) + Math.abs(p.y - q.y);
    if (length > longest) { longest = length; label = { x: (p.x + q.x) / 2 + (p.x === q.x ? 42 : 0), y: (p.y + q.y) / 2 - 10 }; }
  }
  return { d: best.map((p, i) => `${i ? "L" : "M"}${p.x},${p.y}`).join(" "), label };
}

export function fitDiagram(width: number, height: number, viewportWidth: number, viewportHeight: number): number {
  return Math.max(0.03, Math.min(1, (viewportWidth - 32) / width, (viewportHeight - 32) / height));
}

export function wrapDiagramText(text: string, max = 20): string[] {
  const lines: string[] = []; let line = "", size = 0;
  for (const char of text) {
    const weight = /[^\x00-\xff]/.test(char) ? 2 : 1;
    if (size + weight > max) { lines.push(line); line = ""; size = 0; }
    line += char; size += weight;
  }
  if (line) lines.push(line);
  return lines;
}
