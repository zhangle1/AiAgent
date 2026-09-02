import { Suspense } from "react";
import { PublicPrototypeShare } from "@/components/prototype-studio/PublicPrototypeShare";

export default function Page() {
  return <Suspense fallback={<main className="grid min-h-screen place-items-center bg-slate-100 text-sm text-slate-500">正在打开分享原型…</main>}><PublicPrototypeShare /></Suspense>;
}
