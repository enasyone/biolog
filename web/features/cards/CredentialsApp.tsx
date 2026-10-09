'use client';

import { useMemo, useState } from 'react';
import { useSession } from '../../context/session-context';
import { cardsRevokePort } from '.';
import { getCardsDataset } from './data-store';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import { CredentialStatusBadge } from './components/CredentialStatusBadge';
import styles from './cards.module.css';

/**
 * `/credentials` (BIO-UX-007-AC01, AC03, AC04, AC05). `route-manifest.ts`'s
 * own note: distinct from `/cards` in `ui-integration-map.json`, but the
 * same underlying `SyntheticCredential` records — this view is a
 * select-then-inspect detail panel (route manifest's "View credential"),
 * where `/cards` is the full sortable registry table. "Revoke credential"
 * reuses the exact same `revokeCard` port `/cards` uses — it is the same
 * lifecycle action under a different route's own name for it.
 */
export function CredentialsApp() {
  const { tenant } = useSession();
  const [refreshTick, setRefreshTick] = useState(0);
  const [selectedId, setSelectedId] = useState('');
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });

  const dataset = useMemo(() => getCardsDataset(), [refreshTick]);
  const credentials = useMemo(() => dataset.credentials.filter((c) => c.tenantId === tenant.id), [dataset, tenant.id]);
  const selected = credentials.find((c) => c.id === selectedId);
  const personName = (personId: string) => dataset.people.find((p) => p.id === personId)?.displayName ?? personId;

  async function onRevoke() {
    if (!selected) return;
    setAction({ status: 'loading' });
    const result = await cardsRevokePort.primaryAction({ tenantId: tenant.id, cardId: selected.id, reason: 'Revoked from Credentials view' });
    setAction({ status: 'done', ...result });
    if (result.outcome === 'success') setRefreshTick((t) => t + 1);
  }

  return (
    <div className={styles.page}>
      <h1>Credentials</h1>
      <p>Issued credential registry for {tenant.name}, independent of physical card production.</p>

      <label className={`biolog-card ${styles.field}`}>
        View credential
        <select value={selectedId} onChange={(e) => setSelectedId(e.target.value)}>
          <option value="">Select a credential…</option>
          {credentials.map((c) => (
            <option key={c.id} value={c.id}>
              {c.cardNumber} — {personName(c.personId)}
            </option>
          ))}
        </select>
      </label>

      {selected && (
        <section className="biolog-card">
          <h2>{selected.cardNumber}</h2>
          <dl>
            <dt>Holder</dt>
            <dd>{personName(selected.personId)}</dd>
            <dt>Status</dt>
            <dd>
              <CredentialStatusBadge status={selected.status} />
            </dd>
            <dt>Issued</dt>
            <dd>{new Date(selected.issuedAt).toLocaleString()}</dd>
            {selected.statusReason && (
              <>
                <dt>Status reason</dt>
                <dd>{selected.statusReason}</dd>
              </>
            )}
          </dl>
          {(selected.status === 'active' || selected.status === 'recovered' || selected.status === 'lost') && (
            <button type="button" className={styles.actionButton} onClick={onRevoke} disabled={action.status === 'loading'}>
              Revoke credential
            </button>
          )}
        </section>
      )}

      <ActionResultBanner state={action} mode={cardsRevokePort.mode} />
    </div>
  );
}
