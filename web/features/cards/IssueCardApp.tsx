'use client';

import { useMemo, useState, type FormEvent } from 'react';
import { useSession } from '../../context/session-context';
import { cardsIssuePort } from '.';
import { getCardsDataset } from './data-store';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import styles from './cards.module.css';

/** `/cards/issue` (BIO-UX-007-AC01, AC04, AC05): issue a new card for a person who does not already hold an active one. */
export function IssueCardApp() {
  const { tenant } = useSession();
  const [refreshTick, setRefreshTick] = useState(0);
  const [personId, setPersonId] = useState('');
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });

  const dataset = useMemo(() => getCardsDataset(), [refreshTick]);
  const eligiblePeople = useMemo(() => {
    const activeByPerson = new Set(
      dataset.credentials.filter((c) => c.tenantId === tenant.id && (c.status === 'active' || c.status === 'recovered')).map((c) => c.personId),
    );
    return dataset.people.filter((p) => p.tenantId === tenant.id && !activeByPerson.has(p.id));
  }, [dataset, tenant.id]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!personId) return;
    setAction({ status: 'loading' });
    const result = await cardsIssuePort.primaryAction({ tenantId: tenant.id, personId });
    setAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setRefreshTick((t) => t + 1);
      setPersonId('');
    }
  }

  return (
    <div className={styles.page}>
      <h1>Issue Card</h1>
      <p>Issue a new card for {tenant.name}. Only people without an already-active card are eligible.</p>

      <form className={`biolog-card ${styles.form}`} onSubmit={onSubmit}>
        <label className={styles.field}>
          Person
          <select value={personId} onChange={(e) => setPersonId(e.target.value)} required>
            <option value="" disabled>
              Select a person…
            </option>
            {eligiblePeople.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName} ({p.category})
              </option>
            ))}
          </select>
        </label>
        {eligiblePeople.length === 0 && (
          <p className={styles.emptyState}>Every person in this tenant already holds an active card.</p>
        )}
        <button type="submit" className="biolog-button-primary" disabled={!personId || action.status === 'loading'}>
          {action.status === 'loading' ? 'Working…' : 'Issue card'}
        </button>
      </form>

      <ActionResultBanner state={action} mode={cardsIssuePort.mode} />
    </div>
  );
}
