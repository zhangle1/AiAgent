"use client";

import { useSearchParams } from "next/navigation";
import { PublicPrototypePreview } from "@/components/prototype-studio/PublicPrototypePreview";

export function PublicPrototypeShare() {
  const search = useSearchParams();
  const prototypeId = Number(search.get("prototype")) || null;
  return <PublicPrototypePreview prototypeId={prototypeId} />;
}
