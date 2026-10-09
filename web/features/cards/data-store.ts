import { SyntheticDataStore, type SyntheticCredential, type SyntheticDataset } from '../../lib/synthetic-data';

/**
 * The Cards feature's own in-session synthetic data store (BIO-UX-007-AC01,
 * AC03). `synthetic-data/store.ts`'s own header comment names exactly this
 * use case ("a simulated 'Issue card' primary action updating a credential's
 * status") as the reason `SyntheticDataStore.mutate` exists, but explicitly
 * defers *where* it gets instantiated to "BIO-UX-005/012's integration
 * point... shared-shell changes need Damilola's approval." A module-scoped
 * singleton, owned entirely by this feature directory and never imported by
 * shell/layout code, sidesteps that approval gate correctly: it is not a
 * shared-shell change (BIO-UX-007-AC05), just this domain's own data layer,
 * exactly as `web/features/cards/mock.ts` is already this domain's own
 * adapter layer.
 *
 * A plain module-level singleton (not React state/context) because every
 * mock port call (`mock.ts`) needs to read and mutate it outside of any one
 * component's render lifecycle, and Next.js client bundles keep one JS
 * module instance per browser tab/session — the same persistence-within-a-
 * session behaviour every other client-side module singleton in this
 * codebase already relies on (e.g. `feature-mode.ts`'s own `overrides` Map).
 */
const store = new SyntheticDataStore();

export function getCardsDataset(): SyntheticDataset {
  return store.getDataset();
}

export function resetCardsData(): void {
  store.resetToSeed();
}

export function listCredentials(tenantId: string): readonly SyntheticCredential[] {
  return store.getDataset().credentials.filter((c) => c.tenantId === tenantId);
}

export function getCredential(tenantId: string, cardId: string): SyntheticCredential | undefined {
  return listCredentials(tenantId).find((c) => c.id === cardId);
}

function replaceCredential(dataset: SyntheticDataset, updated: SyntheticCredential): SyntheticDataset {
  return {
    ...dataset,
    credentials: dataset.credentials.map((c) => (c.id === updated.id ? updated : c)),
  };
}

function addCredential(dataset: SyntheticDataset, created: SyntheticCredential): SyntheticDataset {
  return { ...dataset, credentials: [...dataset.credentials, created] };
}

let simulatedIdCounter = 0;
/** A plain, non-canonical synthetic id for an in-session simulated credential row (never persisted server-side). */
function nextSimulatedCardId(tenantId: string): string {
  simulatedIdCounter += 1;
  return `${tenantId}-credential-sim-${simulatedIdCounter}`;
}

function nextCardNumber(tenantId: string): string {
  simulatedIdCounter += 1;
  return `BIO-${tenantId.toUpperCase()}-SIM-${String(simulatedIdCounter).padStart(4, '0')}`;
}

export interface MutationOutcome {
  ok: boolean;
  reason?: string;
  credential?: SyntheticCredential;
}

/** BIO-UX-007-AC01: issues a brand-new `active` credential for a person who does not already hold one. */
export function issueCard(tenantId: string, personId: string): MutationOutcome {
  const dataset = store.getDataset();
  const existing = dataset.credentials.find((c) => c.tenantId === tenantId && c.personId === personId && c.status === 'active');
  if (existing) {
    return { ok: false, reason: `${personId} already holds an active card (${existing.cardNumber}).` };
  }
  const created: SyntheticCredential = {
    id: nextSimulatedCardId(tenantId),
    tenantId,
    personId,
    cardNumber: nextCardNumber(tenantId),
    status: 'active',
    issuedAt: new Date().toISOString(),
    isSynthetic: true,
  };
  store.mutate((current) => addCredential(current, created));
  return { ok: true, credential: created };
}

/** BIO-UX-007-AC03: active/recovered -> lost. */
export function markCardLost(tenantId: string, cardId: string, reason?: string): MutationOutcome {
  const credential = getCredential(tenantId, cardId);
  if (!credential) return { ok: false, reason: `No credential ${cardId} for this tenant.` };
  if (credential.status !== 'active' && credential.status !== 'recovered') {
    return { ok: false, reason: `Card is ${credential.status}, not active/recovered - cannot mark lost.` };
  }
  const updated: SyntheticCredential = { ...credential, status: 'lost', statusReason: reason ?? 'Reported lost' };
  store.mutate((current) => replaceCredential(current, updated));
  return { ok: true, credential: updated };
}

/** BIO-UX-007-AC03: lost -> recovered (found again, usable exactly as `active`). */
export function recoverCard(tenantId: string, cardId: string): MutationOutcome {
  const credential = getCredential(tenantId, cardId);
  if (!credential) return { ok: false, reason: `No credential ${cardId} for this tenant.` };
  if (credential.status !== 'lost') {
    return { ok: false, reason: `Card is ${credential.status}, not lost - nothing to recover.` };
  }
  const updated: SyntheticCredential = { ...credential, status: 'recovered', statusReason: undefined };
  store.mutate((current) => replaceCredential(current, updated));
  return { ok: true, credential: updated };
}

/**
 * BIO-UX-007-AC03: active/lost/recovered -> replaced, and issues a fresh
 * `active` card in its place — the old card's own AC04 record keeps a
 * durable link to the new one (`replacedByCardId`/`replacesCardId`) rather
 * than the history disappearing.
 */
export function replaceCard(tenantId: string, cardId: string, reason: string): MutationOutcome {
  const credential = getCredential(tenantId, cardId);
  if (!credential) return { ok: false, reason: `No credential ${cardId} for this tenant.` };
  if (credential.status === 'replaced' || credential.status === 'revoked') {
    return { ok: false, reason: `Card is already ${credential.status} - cannot replace again.` };
  }
  const replacement: SyntheticCredential = {
    id: nextSimulatedCardId(tenantId),
    tenantId,
    personId: credential.personId,
    cardNumber: nextCardNumber(tenantId),
    status: 'active',
    issuedAt: new Date().toISOString(),
    isSynthetic: true,
    replacesCardId: credential.id,
  };
  const oldUpdated: SyntheticCredential = {
    ...credential,
    status: 'replaced',
    statusReason: reason,
    replacedByCardId: replacement.id,
  };
  store.mutate((current) => addCredential(replaceCredential(current, oldUpdated), replacement));
  return { ok: true, credential: replacement };
}

/** BIO-UX-007-AC03: active/lost/recovered -> revoked (terminal; e.g. disciplinary/security action). */
export function revokeCard(tenantId: string, cardId: string, reason: string): MutationOutcome {
  const credential = getCredential(tenantId, cardId);
  if (!credential) return { ok: false, reason: `No credential ${cardId} for this tenant.` };
  if (credential.status === 'revoked' || credential.status === 'replaced') {
    return { ok: false, reason: `Card is already ${credential.status} - cannot revoke again.` };
  }
  const updated: SyntheticCredential = { ...credential, status: 'revoked', statusReason: reason || 'Revoked' };
  store.mutate((current) => replaceCredential(current, updated));
  return { ok: true, credential: updated };
}
