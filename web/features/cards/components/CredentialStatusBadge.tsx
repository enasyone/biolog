import type { CredentialStatus } from '../../../lib/synthetic-data';
import { StatusBadge, type StatusBadgeTone } from '../../../components/status-badge';

const BADGE_TONE: Readonly<Record<CredentialStatus, StatusBadgeTone>> = {
  active: 'success',
  recovered: 'info',
  lost: 'warning',
  revoked: 'error',
  replaced: 'neutral',
};

/** BIO-UX-007-AC03: a small, consistent visual marker for every credential status transition. */
export function CredentialStatusBadge({ status }: { status: CredentialStatus }) {
  return <StatusBadge tone={BADGE_TONE[status]}>{status}</StatusBadge>;
}
