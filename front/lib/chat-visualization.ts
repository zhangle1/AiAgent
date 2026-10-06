export const diagramTypes = [
  { id: "auto", label: "自动选择", syntax: "根据问题选择 flowchart 或 sequenceDiagram" },
  { id: "architecture", label: "架构图", syntax: "使用 flowchart 和 subgraph 表达模块与依赖" },
  { id: "flowchart", label: "流程图", syntax: "使用 flowchart 表达步骤、条件与分支" },
  { id: "sequence", label: "时序图", syntax: "使用 sequenceDiagram 表达参与者与调用顺序" },
] as const;

export type DiagramType = typeof diagramTypes[number]["id"];

/** Compose once at submit time. The ordinary message is the persisted retry snapshot. */
export function buildVisualizationMessage(query: string, type: DiagramType | null): string {
  if (type === null || !query.trim()) return query;
  const diagram = diagramTypes.find((item) => item.id === type);
  if (!diagram) return query;
  return `${query}\n\n【可视化输出：${diagram.label}】
请基于当前对话及本轮已选资料回答上述需求，${diagram.syntax}。
先给出简短结论，再输出一个完整的 mermaid 代码块，随后说明关键关系和来源。
使用简洁中文标签；流程图节点使用稳定的英文字母 ID，中文标签用双引号包裹；优先控制在 20 个节点以内。
仅使用已有且有权访问的上下文；来源不足时说明缺失信息，不要编造代码、提交记录或已核实的关系。区分代码证据、对话方案和推测。
本轮仅分析和输出图形，不修改项目文件，不执行 Git 切换或写操作，不输出 HTML、脚本、Mermaid click 指令或初始化配置。
如果是修改之前的图，请在本次回复中给出完整新图，保留历史消息中的原图。`;
}
