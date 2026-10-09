'use client';

import { useEffect, useMemo, useState } from 'react';
import { useSession } from '../../context/session-context';
import { cardsMarkLostPort, cardsRecoverPort, cardsRevokePort } from '.';
import type { CardTransitionPort } from './ports';
import { getCardsDataset } from './data-store';
import type { CredentialStatus } from '../../lib/synthetic-data';
import { readTestScenarioParam } from '../../dev-tools/test-scenario-params';
import { ensureCredentialState } from '../../dev-tools/credential-state-preset';
import { ActionResultBanner, type CardActionState } from './components/ActionResultBanner';
import { CredentialStatusBadge } from './components/CredentialStatusBadge';
import { RelatedLinksNav } from './components/RelatedLinksNav';
import styles from './cards.module.css';

const CREDENTIAL_STATE_PRESET_VALUES: readonly CredentialStatus[] = ['active', 'lost', 'recovered', 'revoked', 'replaced'];

/**
 * `/cards` (BIO-UX-007-AC01, AC03, AC04, AC05): the credential registry
 * across issued/lost/recovered/replaced/revoked cards for the current
 * tenant, replacing the `PlaceholderPage` shell BIO-UX-002 shipped here.
 * Uses only shared design tokens via `cards.module.css` (AC05); the shell
 * itself (`web/app/layout.tsx`, `components/*`) is untouched.
 */
export function CardsRegistryApp() {
  const { tenant } = useSession();
  const [refreshTick, setRefreshTick] = useState(0);
  const [action, setAction] = useState<CardActionState>({ status: 'idle' });
  const [lastMode, setLastMode] = useState<'mock' | 'live'>(cardsMarkLostPort.mode);
  const [busyCardId, setBusyCardId] = useState<string | null>(null);

  const dataset = useMemo(() => getCardsDataset(), [refreshTick]);
  const credentials = useMemo(
    () => dataset.credentials.filter((c) => c.tenantId === tenant.id).sort((a, b) => a.cardNumber.localeCompare(b.cardNumber)),
    [dataset, tenant.id],
  );
  const personName = (personId: string) => dataset.people.find((p) => p.id === personId)?.displayName ?? personId;

  // BIO-UX-012-AC03 (Human Test Console): a `?credentialState=` preset deterministically ensures
  // a credential in that exact status exists for this tenant's first person, so a chosen value
  // has a real, observable effect — not just navigation. See credential-state-preset.ts's own
  // header comment for why this calls data-store.ts's mutation functions directly (deterministic)
  // rather than the async ports (which wrap them in a success/failure/offline scenario cycle).
  // A post-mount useEffect, not a useState initializer — see use-test-scenario-preset.ts.
  useEffect(() => {
    // Literal check here too, not only inside readTestScenarioParam: `process.env.NODE_ENV ===
    // 'production'` is inlined to a literal at build time, so this early return (and everything
    // after it, including the ensureCredentialState import) is dead-code-eliminated from a
    // production bundle entirely - verified by grepping .next/ output, not just asserted.
    if (process.env.NODE_ENV === 'production') return;
    const preset = readTestScenarioParam('credentialState', CREDENTIAL_STATE_PRESET_VALUES, '' as const);
    if (preset === '') return;
    const firstPerson = dataset.people.find((p) => p.tenantId === tenant.id);
    if (!firstPerson) return;
    ensureCredentialState(tenant.id, firstPerson.id, preset);
    setRefreshTick((t) => t + 1);
    // Only the initial URL matters (a one-time preset) — intentionally not re-run on prop changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function run(cardId: string, port: CardTransitionPort, reason?: string) {
    setBusyCardId(cardId);
    setLastMode(port.mode);
    setAction({ status: 'loading' });
    const result = await port.primaryAction({ tenantId: tenant.id, cardId, reason });
    setAction({ status: 'done', ...result });
    if (result.outcome === 'success') setRefreshTick((t) => t + 1);
    setBusyCardId(null);
  }

  return (
    <div className={styles.page}>
      <h1>Cards</h1>
      <p>Card registry across issued, lost, recovered, replaced and revoked cards for {tenant.name}.</p>

      <RelatedLinksNav
        title="Cards"
        links={[
          { label: 'Issue Card', href: '/cards/issue' },
          { label: 'Replace Card', href: '/cards/replace' },
        ]}
      />

      <ActionResultBanner state={action} mode={lastMode} />

      <section className={`biolog-card ${styles.tableWrap}`}>
        {credentials.length === 0 ? (
          <p className={styles.emptyState}>No cards issued yet for this tenant.</p>
        ) : (
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Card number</th>
                <th scope="col">Holder</th>
                <th scope="col">Status</th>
                <th scope="col">Issued</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {credentials.map((credential) => {
                const busy = busyCardId === credential.id;
                return (
                  <tr key={credential.id}>
                    <td>{credential.cardNumber}</td>
                    <td>{personName(credential.personId)}</td>
                    <td>
                      <CredentialStatusBadge status={credential.status} />
                      {credential.statusReason && <div className={styles.evidenceRef}>{credential.statusReason}</div>}
                    </td>
                    <td>{new Date(credential.issuedAt).toLocaleDateString()}</td>
                    <td>
                      <div className={styles.rowActions}>
                        {(credential.status === 'active' || credential.status === 'recovered') && (
                          <button
                            type="button"
                            className={styles.actionButton}
                            disabled={busy}
                            onClick={() => run(credential.id, cardsMarkLostPort, 'Reported lost')}
                          >
                            Mark lost
                          </button>
                        )}
                        {credential.status === 'lost' && (
                          <button
                            type="button"
                            className={styles.actionButton}
                            disabled={busy}
                            onClick={() => run(credential.id, cardsRecoverPort)}
                          >
                            Recover
                          </button>
                        )}
                        {(credential.status === 'active' || credential.status === 'recovered' || credential.status === 'lost') && (
                          <button
                            type="button"
                            className={styles.actionButton}
                            disabled={busy}
                            onClick={() => run(credential.id, cardsRevokePort, 'Revoked from registry')}
                          >
                            Revoke card
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
