"use client";

import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";
import { MermaidDiagram, mermaidSourceFromPre } from "@/components/chat/MermaidDiagram";
import { knowledgeResourceFileUrl } from "@/lib/knowledge-api";
import { isEmbeddedChapterImage } from "@/lib/knowledge-chapter-copy";
import styles from "./markdown-document-preview.module.css";

const documentComponents: Components = {
  a: ({ href, children }) => <a href={href} target="_blank" rel="noreferrer">{children}</a>,
  pre: ({ children }) => {
    const chart = mermaidSourceFromPre(children);
    return chart ? <MermaidDiagram chart={chart}/> : <pre>{children}</pre>;
  },
  table: ({ children }) => <div className={styles.tableScroll}><table>{children}</table></div>,
  img: ({ src, alt, title }) => <img src={src} alt={alt ?? "文档图片"} title={title} loading="lazy" />,
};

export function MarkdownDocumentPreview({ content, className = "", knowledge }: {
  content: string;
  className?: string;
  knowledge?: { parsedDocumentId?: number | null; onOpen?: (uri: string) => void };
}) {
  const components: Components = knowledge ? {
    ...documentComponents,
    a: ({ href, children }) => href?.startsWith("viking://") && knowledge.onOpen ?
      <button type="button" className={styles.resourceLink} onClick={() => knowledge.onOpen?.(href)}>{children}</button> : <span>{children}</span>,
    img: ({ src, alt }) => typeof src === "string" && src ?
      <img src={src.startsWith("viking://") ? knowledgeResourceFileUrl(src, false, knowledge.parsedDocumentId) : src} alt={alt ?? "解析图片"} loading="lazy" /> : null,
  } : documentComponents;

  return <article className={`${styles.content} ${className}`}>
    <ReactMarkdown remarkPlugins={[remarkGfm]} components={components} urlTransform={knowledge ? (url, key) =>
      url.startsWith("viking://") || (key === "src" && isEmbeddedChapterImage(url)) ? url : "" : undefined}>{content}</ReactMarkdown>
  </article>;
}
