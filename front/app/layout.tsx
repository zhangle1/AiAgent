import type { Metadata } from "next";
import { AuthGate } from "@/components/auth/AuthGate";
import { ChatStreamProvider } from "@/components/chat/ChatStreamProvider";
import { I18nProvider } from "@/i18n/I18nProvider";
import "./globals.css";

export const metadata: Metadata = {
  title: "AiAgent",
  description: "AiAgent chat, knowledge base, model, and service workspace",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="zh-CN">
      <body>
        <I18nProvider>
          <ChatStreamProvider><AuthGate>{children}</AuthGate></ChatStreamProvider>
        </I18nProvider>
      </body>
    </html>
  );
}
