import { AtlasReviewPage } from "@/components/change-atlas/AtlasReviewPage";

export default async function Page({ searchParams }: { searchParams: Promise<{ project?: string; snapshot?: string; commit?: string; file?: string }> }) {
  const params = await searchParams;
  return <AtlasReviewPage projectId={Number(params.project)} snapshotId={params.snapshot ?? ""} commitId={params.commit ?? ""} fileId={params.file}/>;
}
