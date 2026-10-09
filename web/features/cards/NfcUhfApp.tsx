'use client';

import { useMemo, useState, type FormEvent } from 'react';
import { useSession } from '../../context/session-context';
import { cardsEncodeTagPort, cardsVerifyReadPort } from '.';
import type { TagType } from './ports';
import { getCardsDataset } from './data-store';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import styles from './cards.module.css';

const TAG_TYPES: readonly TagType[] = ['NFC', 'UHF'];

/** `/nfc-uhf` (BIO-UX-007-AC01, AC04, AC05, AC06): encode a tag for an active card, then verify the read. */
export function NfcUhfApp() {
  const { tenant } = useSession();
  const [cardId, setCardId] = useState('');
  const [tagType, setTagType] = useState<TagType>('NFC');
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });
  const [lastMode, setLastMode] = useState<'mock' | 'live'>(cardsEncodeTagPort.mode);

  const dataset = useMemo(() => getCardsDataset(), []);
  const encodableCards = useMemo(
    () => dataset.credentials.filter((c) => c.tenantId === tenant.id && (c.status === 'active' || c.status === 'recovered')),
    [dataset, tenant.id],
  );
  const personName = (personId: string) => dataset.people.find((p) => p.id === personId)?.displayName ?? personId;

  async function onEncode(event: FormEvent) {
    event.preventDefault();
    if (!cardId) return;
    setLastMode(cardsEncodeTagPort.mode);
    setAction({ status: 'loading' });
    const result = await cardsEncodeTagPort.primaryAction({ tenantId: tenant.id, cardId, tagType });
    setAction({ status: 'done', ...result });
  }

  async function onVerifyRead() {
    if (!cardId) return;
    setLastMode(cardsVerifyReadPort.mode);
    setAction({ status: 'loading' });
    const result = await cardsVerifyReadPort.primaryAction({ tenantId: tenant.id, cardId });
    setAction({ status: 'done', ...result });
  }

  return (
    <div className={styles.page}>
      <h1>NFC / UHF</h1>
      <p>Encode an NFC or UHF tag for an active card, then verify the read, for {tenant.name}.</p>

      <form className={`biolog-card ${styles.form}`} onSubmit={onEncode}>
        <label className={styles.field}>
          Card
          <select value={cardId} onChange={(e) => setCardId(e.target.value)} required>
            <option value="" disabled>
              Select a card…
            </option>
            {encodableCards.map((c) => (
              <option key={c.id} value={c.id}>
                {c.cardNumber} — {personName(c.personId)}
              </option>
            ))}
          </select>
        </label>
        {encodableCards.length === 0 && <p className={styles.emptyState}>No active cards available to encode.</p>}
        <label className={styles.field}>
          Tag type
          <select value={tagType} onChange={(e) => setTagType(e.target.value as TagType)}>
            {TAG_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <div className={styles.rowActions}>
          <button type="submit" className="biolog-button-primary" disabled={!cardId || action.status === 'loading'}>
            {action.status === 'loading' ? 'Working…' : 'Encode tag'}
          </button>
          <button type="button" className={styles.actionButton} disabled={!cardId || action.status === 'loading'} onClick={onVerifyRead}>
            Verify read
          </button>
        </div>
      </form>

      <ActionResultBanner state={action} mode={lastMode} />
    </div>
  );
}
