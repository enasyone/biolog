'use client';

import { useState, type FormEvent } from 'react';
import { cardsSaveDesignPort } from '.';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import styles from './cards.module.css';

/**
 * BIO-UX-007-AC06: the stable "save design" seam for Card Designer. Per
 * Kemi's ruling (see `docs/features/bio-ux-007/README.md`), the canvas
 * itself (BIO-CRD-002/003/004's own `CardDesignerApp`/`CardTypographyApp`/
 * `CardObjectsApp`) stays pure client-side with zero I/O — this component
 * does not read or touch their internal design state, it only gives the
 * page a real, working "Save design" action (route-manifest.ts's own
 * primaryAction for `/card-designer`) so a later story can wire a real save
 * without this one redesigning the canvas or reopening those owned files.
 */
export function SaveDesignBar() {
  const [name, setName] = useState('');
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;
    setAction({ status: 'loading' });
    const result = await cardsSaveDesignPort.primaryAction({ name });
    setAction({ status: 'done', ...result });
  }

  return (
    <section className={`biolog-card ${styles.page}`} aria-label="Save design">
      <form className={styles.rowActions} onSubmit={onSubmit}>
        <label className={styles.field} style={{ flex: 1 }}>
          Design name
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Staff ID v2" required />
        </label>
        <button type="submit" className="biolog-button-primary" disabled={!name.trim() || action.status === 'loading'}>
          {action.status === 'loading' ? 'Working…' : 'Save design'}
        </button>
      </form>
      <ActionResultBanner state={action} mode={cardsSaveDesignPort.mode} />
    </section>
  );
}
