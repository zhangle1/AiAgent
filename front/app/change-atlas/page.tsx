import { ChangeAtlasPage } from "@/components/change-atlas/ChangeAtlasPage";

export default async function Page({ searchParams }: { searchParams: Promise<{ project?: string }> }) {
  const params = await searchParams;
  const projectId = Number(params.project);
  return <ChangeAtlasPage initialProjectId={Number.isSafeInteger(projectId) && projectId > 0 ? projectId : undefined} />;
}
