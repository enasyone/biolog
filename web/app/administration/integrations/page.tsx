import { ImportConsole } from '../../../features/integrations/components/import-console';
import { ServiceAccountsPanel } from '../../../features/bio-int-003/ServiceAccountsPanel';
import { WebhookSubscriptionsPanel } from '../../../features/bio-int-004/WebhookSubscriptionsPanel';
import { ConflictReviewPanel } from '../../../features/bio-int-005/ConflictReviewPanel';

export default function AdministrationIntegrationsPage() {
  return (
    <>
      <ImportConsole />
      <ServiceAccountsPanel />
      <WebhookSubscriptionsPanel />
      <ConflictReviewPanel />
    </>
  );
}
