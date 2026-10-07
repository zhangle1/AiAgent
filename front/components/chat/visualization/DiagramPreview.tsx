import type { ReactNode } from "react";
import type { DiagramType } from "@/lib/chat-visualization";

const blue = "#3b82f6";
const purple = "#8b5cf6";
const green = "#10b981";

function Box({ x, y, w = 56, label, color = blue }: { x: number; y: number; w?: number; label: string; color?: string }) {
  return <g><rect x={x} y={y} width={w} height={24} rx={5} fill="white" stroke={color} /><text x={x + w / 2} y={y + 15} fill="#334155" textAnchor="middle" fontSize={10}>{label}</text></g>;
}

function Line({ d, color = blue, dashed = false }: { d: string; color?: string; dashed?: boolean }) {
  return <path d={d} fill="none" stroke={color} strokeWidth={1.5} strokeDasharray={dashed ? "4 3" : undefined} strokeLinecap="round" strokeLinejoin="round" />;
}

function Dot({ x, y, color = blue }: { x: number; y: number; color?: string }) {
  return <circle cx={x} cy={y} r={4} fill="white" stroke={color} strokeWidth={2} />;
}

// Fixed, local examples: no model requests or Mermaid runtime needed in the picker.
const examples: Record<DiagramType, ReactNode> = {
  auto: <>
    <Line d="M 100 45 V 57 H 38 V 68 M 100 57 V 68 M 100 57 H 162 V 68" />
    <Box x={66} y={17} w={68} label="你的问题" />
    <Box x={12} y={68} w={52} label="流程" /><Box x={74} y={68} w={52} label="关系" color={purple} /><Box x={136} y={68} w={52} label="演进" color={green} />
  </>,
  architecture: <>
    <rect x={10} y={43} width={180} height={60} rx={8} fill="#eff6ff" stroke="#bfdbfe" strokeDasharray="4 3" />
    <Line d="M 100 34 V 51 M 100 75 V 84 H 52 M 100 84 H 148" />
    <Box x={72} y={8} label="客户端" /><Box x={72} y={51} label="API" />
    <Box x={21} y={78} w={62} label="数据库" color={purple} /><Box x={117} y={78} w={62} label="缓存" color={green} />
  </>,
  flowchart: <>
    <Line d="M 60 30 V 42 M 60 76 V 87 M 86 59 H 147 V 87 M 56 38 L 60 42 L 64 38 M 56 83 L 60 87 L 64 83" />
    <Box x={32} y={6} label="提交" />
    <path d="M 60 41 L 88 59 L 60 77 L 32 59 Z" fill="#f5f3ff" stroke={purple} />
    <text x={60} y={63} textAnchor="middle" fill="#334155" fontSize={10}>通过？</text>
    <text x={69} y={86} fill="#64748b" fontSize={9}>是</text><text x={111} y={54} fill="#64748b" fontSize={9}>否</text>
    <Box x={32} y={88} label="完成" color={green} /><Box x={119} y={88} label="修改" />
  </>,
  sequence: <>
    <Box x={12} y={8} w={50} label="用户" /><Box x={75} y={8} w={50} label="服务" /><Box x={138} y={8} w={50} label="数据" />
    <Line d="M 37 32 V 111 M 100 32 V 111 M 163 32 V 111" dashed />
    <Line d="M 37 51 H 100 L 95 47 M 100 73 H 163 L 158 69" />
    <Line d="M 163 96 H 37 L 42 92" color={green} dashed />
    <text x={68} y={47} textAnchor="middle" fill="#64748b" fontSize={9}>请求</text><text x={132} y={69} textAnchor="middle" fill="#64748b" fontSize={9}>查询</text>
  </>,
  class: <>
    <rect x={55} y={7} width={90} height={46} rx={5} fill="white" stroke={purple} />
    <Line d="M 55 27 H 145" color={purple} />
    <text x={100} y={21} textAnchor="middle" fill="#334155" fontSize={10}>动物</text><text x={65} y={43} fill="#64748b" fontSize={9}>+ 移动()</text>
    <Line d="M 49 87 V 69 H 151 V 87 M 100 69 V 61" />
    <path d="M 100 53 L 95 61 H 105 Z" fill="white" stroke={blue} />
    <Box x={21} y={87} label="猫" /><Box x={123} y={87} label="狗" />
  </>,
  er: <>
    <Box x={8} y={25} w={66} label="用户" /><Box x={126} y={25} w={66} label="订单" color={purple} />
    <Line d="M 74 62 H 126 M 82 56 V 68 M 118 62 L 126 54 M 118 62 L 126 70" />
    <text x={40} y={67} textAnchor="middle" fill="#64748b" fontSize={9}>id · 姓名</text><text x={160} y={67} textAnchor="middle" fill="#64748b" fontSize={9}>id · 用户ID</text>
    <text x={100} y={49} textAnchor="middle" fill="#64748b" fontSize={9}>1 : N</text><text x={100} y={99} textAnchor="middle" fill="#64748b" fontSize={10}>一个用户拥有多个订单</text>
  </>,
  state: <>
    <circle cx={16} cy={39} r={5} fill={blue} /><Line d="M 21 39 H 36 M 92 39 H 126 L 121 35 M 154 51 V 87 H 64 V 51 M 60 55 L 64 51 L 68 55" />
    <Box x={36} y={27} label="待处理" /><Box x={126} y={27} label="已完成" color={green} />
    <text x={109} y={31} textAnchor="middle" fill="#64748b" fontSize={9}>执行</text><text x={109} y={82} textAnchor="middle" fill="#64748b" fontSize={9}>重新打开</text>
  </>,
  mindmap: <>
    <Line d="M 80 60 C 110 60 96 21 128 21 M 80 60 H 128 M 80 60 C 110 60 96 99 128 99" color={purple} />
    <Box x={12} y={48} w={68} label="项目主题" /><Box x={128} y={9} label="目标" color={purple} /><Box x={128} y={48} label="方案" color={green} /><Box x={128} y={87} label="行动" />
  </>,
  timeline: <>
    <Line d="M 20 60 H 180 L 175 56" />
    <Line d="M 35 60 V 38 M 100 60 V 83 M 165 60 V 38" />
    <Dot x={35} y={60} /><Dot x={100} y={60} color={purple} /><Dot x={165} y={60} color={green} />
    <text x={35} y={25} textAnchor="middle" fill="#334155" fontSize={10}>立项</text><text x={100} y={100} textAnchor="middle" fill="#334155" fontSize={10}>开发</text><text x={165} y={25} textAnchor="middle" fill="#334155" fontSize={10}>上线</text>
    <text x={35} y={79} textAnchor="middle" fill="#64748b" fontSize={9}>01 月</text><text x={100} y={48} textAnchor="middle" fill="#64748b" fontSize={9}>02 月</text><text x={165} y={79} textAnchor="middle" fill="#64748b" fontSize={9}>03 月</text>
  </>,
  gantt: <>
    <Line d="M 51 27 V 106 M 95 27 V 106 M 139 27 V 106 M 183 27 V 106" color="#cbd5e1" dashed />
    <text x={61} y={18} fill="#64748b" fontSize={9}>第 1 周</text><text x={130} y={18} fill="#64748b" fontSize={9}>第 2 周</text>
    <text x={10} y={46} fill="#334155" fontSize={10}>设计</text><text x={10} y={72} fill="#334155" fontSize={10}>开发</text><text x={10} y={98} fill="#334155" fontSize={10}>测试</text>
    <rect x={52} y={34} width={43} height={16} rx={4} fill={blue} /><rect x={95} y={60} width={64} height={16} rx={4} fill={purple} /><rect x={159} y={86} width={25} height={16} rx={4} fill={green} />
  </>,
  git: <>
    <Line d="M 20 43 H 180" /><Line d="M 52 43 C 66 43 66 84 84 84 H 118 C 143 84 143 43 158 43" color={purple} />
    <Dot x={22} y={43} /><Dot x={52} y={43} /><Dot x={108} y={43} /><Dot x={158} y={43} /><Dot x={180} y={43} /><Dot x={84} y={84} color={purple} /><Dot x={118} y={84} color={purple} />
    <text x={18} y={25} fill="#334155" fontSize={10}>main</text><text x={74} y={107} fill={purple} fontSize={10}>feature</text><text x={150} y={25} fill="#64748b" fontSize={9}>合并</text>
  </>,
};

export function DiagramPreview({ type }: { type: DiagramType }) {
  return <svg viewBox="0 0 200 120" aria-hidden="true" focusable="false" className="mb-3 h-[96px] w-full rounded-lg bg-slate-50 sm:h-[112px]" style={{ fontFamily: "inherit" }}>{examples[type]}</svg>;
}
