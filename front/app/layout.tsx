import type { Metadata } from "next";
import { AuthGate } from "@/components/auth/AuthGate";
import { I18nProvider } from "@/i18n/I18nProvider";
import "./globals.css";

export const metadata: Metadata = {
  title: "坤伴",
  description: "坤伴 AI 对话、知识库、模型与服务工作台",
  icons: {
    icon: "/kunbuddy-mark.png",
    apple: "/kunbuddy-mark.png",
  },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="zh-CN" suppressHydrationWarning>
      <body>
        <I18nProvider>
          <AuthGate>{children}</AuthGate>
        </I18nProvider>
      </body>
    </html>
  );
}
