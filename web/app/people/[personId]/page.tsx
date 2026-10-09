import { PersonDetailView } from '../../../features/identity/components/person-detail-view';

export default async function PersonDetailPage({ params }: { params: Promise<{ personId: string }> }) {
  const { personId } = await params;
  return <PersonDetailView personId={personId} />;
}
