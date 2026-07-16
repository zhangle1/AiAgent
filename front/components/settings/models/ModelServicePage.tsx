 "use client";

import { notFound } from "next/navigation";
import { ModelServiceEditor } from "@/components/settings/models/ModelServiceEditor";
import { getModelServiceConfig } from "@/components/settings/models/model-service-config";
import type { ServiceName } from "@/lib/settings-types";

export function ModelServicePage({ service }: { service: ServiceName }) {
  const config = getModelServiceConfig(service);
  if (!config) notFound();
  return <ModelServiceEditor config={config} />;
}
