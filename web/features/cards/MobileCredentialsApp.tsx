'use client';

import { useMemo, useState } from 'react';
import { useSession } from '../../context/session-context';
import { cardsRevokeMobilePort } from '.';
import { getCardsDataset } from './data-store';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import { CredentialStatusBadge } from './components/CredentialStatusBadge';
import styles from './cards.module.css';

/**
 * `/mobile-credentials` (BIO-UX-007-AC01, AC04, AC05). "View status" is a
 * read straight off the same tenant-scoped credential registry every other
 * page in this feature already reads — no port call, exactly like the
 * registry table itself (see `route-manifest.ts`'s own note on this route:
 * provisioning stays on the mobile app; this page is status/revoke only).
 * "Revoke mobile credential" is the one real action here.
 */
export function MobileCredentialsApp() {
  const { tenant } = useSession();
  const [refreshTick, setRefreshTick] = useState(0);
  const [personId, setPersonId] = useState('');
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });

  const dataset = useMemo(() => getCardsDataset(), [refreshTick]);
  const peopleWithCredentials = useMemo(() => {
    const withCredential = new Set(dataset.credentials.filter((c) => c.tenantId === tenant.id).map((c) => c.personId));
    return dataset.people.filter((p) => p.tenantId === tenant.id && withCredential.has(p.id));
  }, [dataset, tenant.id]);
  const credential = dataset.credentials.find(
    (c) => c.tenantId === tenant.id && c.personId === personId && (c.status === 'active' || c.status === 'recovered' || c.status === 'lost'),
  );

  async function onRevoke() {
    if (!personId) return;
    setAction({ status: 'loading' });
    const result = await cardsRevokeMobilePort.primaryAction({ tenantId: tenant.id, personId });
    setAction({ status: 'done', ...result });
    if (result.outcome === 'success') setRefreshTick((t) => t + 1);
  }

  return (
    <div className={styles.page}>
      <h1>Mobile Credentials</h1>
      <p>Mobile credential status for {tenant.name}. Provisioning itself happens on the BIOLOG mobile app.</p>

      <label className={`biolog-card ${styles.field}`}>
        View status
        <select value={personId} onChange={(e) => setPersonId(e.target.value)}>
          <option value="">Select a person…</option>
          {peopleWithCredentials.map((p) => (
            <option key={p.id} value={p.id}>
              {p.displayName}
            </option>
          ))}
        </select>
      </label>

      {personId && (
        <section className="biolog-card">
          {credential ? (
            <>
              <p>
                Mobile credential status: <CredentialStatusBadge status={credential.status} />
              </p>
              <button type="button" className={styles.actionButton} onClick={onRevoke} disabled={action.status === 'loading'}>
                Revoke mobile credential
              </button>
            </>
          ) : (
            <p className={styles.emptyState}>No active/recoverable mobile credential for this person.</p>
          )}
        </section>
      )}

      <ActionResultBanner state={action} mode={cardsRevokeMobilePort.mode} />
    </div>
  );
}
