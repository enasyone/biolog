'use client';

import { useMemo, useState, type FormEvent } from 'react';
import { useSession } from '../../context/session-context';
import { cardsMarkLostPort, cardsReplacePort } from '.';
import type { ReplaceCardReason } from './ports';
import { getCardsDataset } from './data-store';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import styles from './cards.module.css';

const REASONS: readonly ReplaceCardReason[] = ['lost', 'damaged', 'stolen', 'other'];

/**
 * `/cards/replace` (BIO-UX-007-AC01, AC03, AC04, AC05). Route manifest's own
 * secondary action "Mark lost" is included here too (not just on the
 * registry) so an operator working this one form doesn't need to detour to
 * `/cards` first when the card isn't already marked lost.
 */
export function ReplaceCardApp() {
  const { tenant } = useSession();
  const [refreshTick, setRefreshTick] = useState(0);
  const [cardId, setCardId] = useState('');
  const [reason, setReason] = useState<ReplaceCardReason>('lost');
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });

  const dataset = useMemo(() => getCardsDataset(), [refreshTick]);
  const replaceableCards = useMemo(
    () =>
      dataset.credentials.filter(
        (c) => c.tenantId === tenant.id && (c.status === 'active' || c.status === 'recovered' || c.status === 'lost'),
      ),
    [dataset, tenant.id],
  );
  const personName = (personId: string) => dataset.people.find((p) => p.id === personId)?.displayName ?? personId;

  async function onReplace(event: FormEvent) {
    event.preventDefault();
    if (!cardId) return;
    setAction({ status: 'loading' });
    const result = await cardsReplacePort.primaryAction({ tenantId: tenant.id, cardId, reason });
    setAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setRefreshTick((t) => t + 1);
      setCardId('');
    }
  }

  async function onMarkLost() {
    if (!cardId) return;
    setAction({ status: 'loading' });
    const result = await cardsMarkLostPort.primaryAction({ tenantId: tenant.id, cardId, reason: 'Reported lost' });
    setAction({ status: 'done', ...result });
    if (result.outcome === 'success') setRefreshTick((t) => t + 1);
  }

  return (
    <div className={styles.page}>
      <h1>Replace Card</h1>
      <p>Replace a lost, damaged or stolen card for {tenant.name}. The old card is retired; a new one is issued in its place.</p>

      <form className={`biolog-card ${styles.form}`} onSubmit={onReplace}>
        <label className={styles.field}>
          Card to replace
          <select value={cardId} onChange={(e) => setCardId(e.target.value)} required>
            <option value="" disabled>
              Select a card…
            </option>
            {replaceableCards.map((c) => (
              <option key={c.id} value={c.id}>
                {c.cardNumber} — {personName(c.personId)} ({c.status})
              </option>
            ))}
          </select>
        </label>
        {replaceableCards.length === 0 && <p className={styles.emptyState}>No cards are eligible for replacement.</p>}
        <label className={styles.field}>
          Reason
          <select value={reason} onChange={(e) => setReason(e.target.value as ReplaceCardReason)}>
            {REASONS.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </label>
        <div className={styles.rowActions}>
          <button type="submit" className="biolog-button-primary" disabled={!cardId || action.status === 'loading'}>
            {action.status === 'loading' ? 'Working…' : 'Replace card'}
          </button>
          <button
            type="button"
            className={styles.actionButton}
            disabled={!cardId || action.status === 'loading'}
            onClick={onMarkLost}
          >
            Mark lost
          </button>
        </div>
      </form>

      <ActionResultBanner state={action} mode={cardsReplacePort.mode} />
    </div>
  );
}
