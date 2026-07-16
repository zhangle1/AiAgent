"use client";

import { SettingsHub } from "@/components/settings/SettingsHub";
import { useSettings } from "@/components/settings/SettingsProvider";

export default function SettingsPage() {
  const { catalog } = useSettings();
  return <SettingsHub catalog={catalog} />;
}
