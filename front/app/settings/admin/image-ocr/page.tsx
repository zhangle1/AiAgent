import { redirect } from "next/navigation";

export default function LegacyImageSettingsRoute() {
  redirect("/settings/admin/agents");
}
