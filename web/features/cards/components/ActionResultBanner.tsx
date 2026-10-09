import type { CardActionResult } from '../ports';
import styles from '../cards.module.css';

export type CardActionState =
  | { status: 'idle' }
  | { status: 'loading' }
  | ({ status: 'done' } & CardActionResult);

const OUTCOME_CLASS: Readonly<Record<'success' | 'failure' | 'offline', string>> = {
  success: styles.resultBannerSuccess!,
  failure: styles.resultBannerFailure!,
  offline: styles.resultBannerOffline!,
};

/**
 * Shared result display for every Cards/Credentials/NFC-UHF/Mobile Credentials
 * action (BIO-UX-007-AC04: "visibly marked when not live"). `mode` is the
 * resolved port's own `mode` ('mock' | 'live') at import time — every port
 * this feature registers is still `mock` by default (nothing is LIVE until a
 * later story connects a real backend), so the `(SIMULATED)` marker below is
 * accurate today and disappears on its own once a port resolves to `live`,
 * without this component changing.
 */
export function ActionResultBanner({ state, mode }: { state: CardActionState; mode: 'mock' | 'live' }) {
  if (state.status === 'idle') return null;
  if (state.status === 'loading') {
    return (
      <p role="status" className={`${styles.resultBanner} ${styles.resultBannerLoading}`}>
        Working…
      </p>
    );
  }
  return (
    <div role="status" className={`${styles.resultBanner} ${OUTCOME_CLASS[state.outcome]}`}>
      <span>
        {state.message}
        {mode === 'mock' && <span className={styles.simBadge} style={{ marginLeft: '0.5rem' }}>SIMULATED</span>}
      </span>
      {state.evidenceRef && <span className={styles.evidenceRef}>Evidence: {state.evidenceRef}</span>}
    </div>
  );
}
