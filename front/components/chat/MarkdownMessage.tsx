"use client";

import { Children, isValidElement, useEffect, useId, useState, type KeyboardEvent, type ReactNode } from "react";
import { Maximize2, X } from "lucide-react";
import ReactMarkdown, { defaultUrlTransform } from "react-markdown";
import remarkGfm from "remark-gfm";
import { resolveProjectCodeFileReference } from "@/lib/code-repository-api";

type MarkdownMessageProps = {
  content: string;
  projectId?: number | null;
  onOpenCodeFile?: (reference: { repositoryName: string; filePath: string; line?: number }) => void;
};

type CodeReferenceCandidate = {
  reference: string;
};

const sourceFilePattern = /(?:^|[\\/])?[^\\/\s]+\.(?:cs|csproj|sln|slnf|ts|tsx|js|jsx|mjs|cjs|vue|py|java|go|rs|php|sql|json|xml|yml|yaml|md|cshtml|razor|config|env)(?:(?::|#L)[1-9]\d{0,8})?$/i;
const fileReferenceInText = /(?:[a-z]:)?(?:[^\s`[\](),]+[\\/])*[^\s`[\](),]+\.(?:cs|csproj|sln|slnf|ts|tsx|js|jsx|mjs|cjs|vue|py|java|go|rs|php|sql|json|xml|yml|yaml|md|cshtml|razor|config|env)(?:(?::|#L)[1-9]\d{0,8})?/gi;

function domProps(props: Record<string, any>) {
  const { node, ...rest } = props;
  void node;
  return rest;
}

function readChildrenText(children: ReactNode): string {
  if (typeof children === "string" || typeof children === "number") return String(children);
  if (Array.isArray(children)) return children.map(readChildrenText).join("");
  return "";
}

function MermaidDiagram({ chart }: { chart: string }) {
  const reactId = useId();
  const [svg, setSvg] = useState("");
  const [error, setError] = useState("");
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setSvg("");
    setError("");
    async function render() {
      try {
        const mermaid = (await import("mermaid")).default;
        mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: "neutral" });
        const id = `aiagent-mermaid-${reactId.replace(/[^a-zA-Z0-9_-]/g, "")}`;
        const result = await mermaid.render(id, chart);
        if (!cancelled) setSvg(result.svg);
      } catch (value) {
        if (!cancelled) setError(value instanceof Error ? value.message : "Mermaid 图表解析失败。");
      }
    }
    void render();
    return () => { cancelled = true; };
  }, [chart, reactId]);

  useEffect(() => {
    if (!expanded) return;
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") setExpanded(false);
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [expanded]);

  if (error) {
    return <div className="my-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900"><p>时序图解析失败：{error}</p><pre className="mt-2 overflow-x-auto rounded bg-amber-100/70 p-2 text-xs leading-5"><code>{chart}</code></pre></div>;
  }
  if (!svg) return <div className="my-4 flex min-h-24 items-center justify-center rounded-lg border border-zinc-200 bg-zinc-50 text-sm text-zinc-500">正在生成 Mermaid 图表…</div>;
  const expandOnKeyboard = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setExpanded(true);
    }
  };
  return <>
    <div role="button" tabIndex={0} aria-label="点击全屏查看 Mermaid 图表" onClick={() => setExpanded(true)} onKeyDown={expandOnKeyboard} className="group relative my-4 cursor-zoom-in overflow-x-auto rounded-xl border border-zinc-200 bg-white p-3 outline-none transition hover:border-blue-300 focus-visible:ring-2 focus-visible:ring-blue-500">
      <div role="img" aria-label="Mermaid 图表" className="[&_svg]:h-auto [&_svg]:min-w-max [&_svg]:max-w-none" dangerouslySetInnerHTML={{ __html: svg }} />
      <span className="pointer-events-none absolute right-3 top-3 inline-flex items-center gap-1 rounded-md bg-slate-900/75 px-2 py-1 text-[11px] text-white opacity-0 shadow-sm transition group-hover:opacity-100 group-focus-visible:opacity-100"><Maximize2 size={13}/>点击放大</span>
    </div>
    {expanded && <div role="dialog" aria-modal="true" aria-label="Mermaid 图表全屏预览" onMouseDown={() => setExpanded(false)} className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/80 p-4 backdrop-blur-sm">
      <div onMouseDown={(event) => event.stopPropagation()} className="flex h-full w-full max-w-[96vw] flex-col overflow-hidden rounded-2xl border border-white/15 bg-white shadow-2xl">
        <div className="flex shrink-0 items-center justify-between border-b border-zinc-200 px-4 py-3"><span className="text-sm font-semibold text-zinc-800">Mermaid 图表</span><button type="button" onClick={() => setExpanded(false)} className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-zinc-500 transition hover:bg-zinc-100 hover:text-zinc-900" aria-label="关闭全屏预览"><X size={17}/></button></div>
        <div role="img" aria-label="Mermaid 图表全屏预览" className="workspace-scroll min-h-0 flex-1 overflow-auto bg-zinc-50 p-6 [&_svg]:h-auto [&_svg]:min-w-max [&_svg]:max-w-none" dangerouslySetInnerHTML={{ __html: svg }} />
      </div>
    </div>}
  </>;
}

function mermaidSourceFromPre(children: ReactNode): string | null {
  const child = Children.toArray(children)[0];
  if (!isValidElement(child)) return null;
  const props = child.props as { className?: string; children?: ReactNode };
  if (!props.className?.split(" ").includes("language-mermaid")) return null;
  const chart = readChildrenText(props.children).trim();
  return chart || null;
}

function decodeReference(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function codeReferenceFromHref(href?: string): CodeReferenceCandidate | null {
  if (!href) return null;
  if (href.startsWith("aiagent://code-file?")) {
    try {
      const url = new URL(href);
      const path = url.searchParams.get("path");
      const line = url.searchParams.get("line");
      if (!path) return null;
      return { reference: line && /^[1-9]\d{0,8}$/.test(line) ? `${path}:${line}` : path };
    } catch {
      return null;
    }
  }
  if (/^[a-z][a-z\d+.-]*:/i.test(href) && !/^[a-z]:[\\/]/i.test(href)) return null;

  const reference = decodeReference(href).trim();
  return sourceFilePattern.test(reference) ? { reference } : null;
}

function codeReferenceFromText(value: string): CodeReferenceCandidate | null {
  const reference = value.trim();
  return !reference.includes("\n") && sourceFilePattern.test(reference) ? { reference } : null;
}

function linkifyAgentFileReferences(markdown: string): string {
  return markdown.split(/(```[\s\S]*?```|`[^`]*`)/g).map((segment) => {
    if (!segment || segment.startsWith("`")) return segment;
    return segment.split("\n").map((line) => {
      if (line.includes("](")) return line;
      return line.replace(fileReferenceInText, (reference) => {
        if (reference.includes("://")) return reference;
        return `[${reference}](aiagent://code-file?path=${encodeURIComponent(reference)})`;
      });
    }).join("\n");
  }).join("");
}

function OpenCodeReference({ candidate, projectId, onOpenCodeFile, children, className }: {
  candidate: CodeReferenceCandidate;
  projectId: number;
  onOpenCodeFile: NonNullable<MarkdownMessageProps["onOpenCodeFile"]>;
  children: ReactNode;
  className: string;
}) {
  const open = async () => {
    try {
      const resolved = await resolveProjectCodeFileReference(projectId, candidate.reference);
      onOpenCodeFile({
        repositoryName: resolved.repository_name,
        filePath: resolved.file_path,
        line: resolved.line ?? undefined,
      });
    } catch {
      // The backend deliberately rejects unknown, ambiguous, or out-of-project paths.
    }
  };

  return (
    <button type="button" onClick={() => void open()} className={className} title="Open in the right file panel">
      {children}
    </button>
  );
}

export function MarkdownMessage({ content, projectId, onOpenCodeFile }: MarkdownMessageProps) {
  const renderedContent = linkifyAgentFileReferences(content);
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      urlTransform={(url) => url.startsWith("aiagent://code-file?") ? url : defaultUrlTransform(url)}
      components={{
        h1: (props) => <h1 className="mb-3 mt-5 text-2xl font-semibold leading-tight" {...domProps(props)} />,
        h2: (props) => <h2 className="mb-3 mt-5 text-xl font-semibold leading-tight" {...domProps(props)} />,
        h3: (props) => <h3 className="mb-2 mt-4 text-lg font-semibold leading-tight" {...domProps(props)} />,
        h4: (props) => <h4 className="mb-2 mt-4 text-base font-semibold leading-tight" {...domProps(props)} />,
        p: (props) => <p className="my-3 leading-7" {...domProps(props)} />,
        ul: (props) => <ul className="my-3 ml-5 list-disc space-y-1" {...domProps(props)} />,
        ol: (props) => <ol className="my-3 ml-5 list-decimal space-y-1" {...domProps(props)} />,
        li: (props) => <li className="pl-1 leading-7" {...domProps(props)} />,
        blockquote: (props) => (
          <blockquote className="my-4 border-l-4 border-zinc-300 pl-4 text-zinc-700" {...domProps(props)} />
        ),
        hr: () => <div className="my-5 h-px bg-zinc-200" />,
        table: (props) => (
          <div className="my-4 overflow-x-auto rounded-lg border border-zinc-200">
            <table className="min-w-full border-collapse text-left text-[13px]" {...domProps(props)} />
          </div>
        ),
        thead: (props) => <thead className="bg-zinc-100" {...domProps(props)} />,
        th: (props) => <th className="border-b border-zinc-200 px-3 py-2 font-semibold" {...domProps(props)} />,
        td: (props) => <td className="border-b border-zinc-100 px-3 py-2 align-top" {...domProps(props)} />,
        code: ({ className, children, ...props }) => {
          const sourceReference = !className ? codeReferenceFromText(readChildrenText(children)) : null;
          const code = <code className={`${className ?? ""} rounded bg-zinc-100 px-1 py-0.5 font-mono text-[0.92em]`} {...domProps(props)}>{children}</code>;
          return sourceReference && projectId && onOpenCodeFile
            ? <OpenCodeReference candidate={sourceReference} projectId={projectId} onOpenCodeFile={onOpenCodeFile} className="cursor-pointer text-left text-blue-700 underline decoration-blue-300 underline-offset-2 hover:text-blue-900">{code}</OpenCodeReference>
            : code;
        },
        pre: ({ children, ...props }) => {
          const chart = mermaidSourceFromPre(children);
          return chart
            ? <MermaidDiagram chart={chart} />
            : <pre className="my-4 overflow-x-auto rounded-lg bg-zinc-950 p-3 text-[12px] leading-6 text-zinc-50 [&>code]:bg-transparent [&>code]:p-0 [&>code]:text-inherit" {...domProps(props)}>{children}</pre>;
        },
        a: ({ href, children, ...props }) => {
          // Agents sometimes turn a source file name into an ordinary http link.
          // Prefer the displayed source-file reference so it opens in the right inspector.
          const sourceReference = codeReferenceFromHref(href) ?? codeReferenceFromText(readChildrenText(children));
          if (sourceReference && projectId && onOpenCodeFile) {
            return <OpenCodeReference candidate={sourceReference} projectId={projectId} onOpenCodeFile={onOpenCodeFile} className="text-blue-600 underline underline-offset-2 hover:text-blue-700">{children}</OpenCodeReference>;
          }
          return <a className="text-blue-600 underline underline-offset-2 hover:text-blue-700" target="_blank" rel="noreferrer" href={href} {...domProps(props)}>{children}</a>;
        },
      }}
    >
      {renderedContent}
    </ReactMarkdown>
  );
}
