"use client";

import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

type MarkdownMessageProps = {
  content: string;
};

function domProps(props: Record<string, any>) {
  const { node, ...rest } = props;
  void node;
  return rest;
}

export function MarkdownMessage({ content }: MarkdownMessageProps) {
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
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
        code: ({ className, children, ...props }) => (
          <code className={`${className ?? ""} rounded bg-zinc-100 px-1 py-0.5 font-mono text-[0.92em]`} {...domProps(props)}>
            {children}
          </code>
        ),
        pre: (props) => (
          <pre className="my-4 overflow-x-auto rounded-lg bg-zinc-950 p-3 text-[12px] leading-6 text-zinc-50 [&>code]:bg-transparent [&>code]:p-0 [&>code]:text-inherit" {...domProps(props)} />
        ),
        a: (props) => (
          <a className="text-blue-600 underline underline-offset-2 hover:text-blue-700" target="_blank" rel="noreferrer" {...domProps(props)} />
        ),
      }}
    >
      {content}
    </ReactMarkdown>
  );
}
