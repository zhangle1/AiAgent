"use client";

import type { LucideIcon } from "lucide-react";

export function ProviderIcon({
  provider,
  fallback: Fallback,
  className = "",
}: {
  provider?: string | null;
  fallback: LucideIcon;
  className?: string;
}) {
  if (provider === "deepseek") {
    return <img src="/provider-icons/deepseek-color.svg" alt="DeepSeek" className={className || "h-4 w-4"} />;
  }

  return <Fallback size={16} className={className} />;
}
