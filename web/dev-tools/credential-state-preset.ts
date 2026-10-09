import { issueCard, listCredentials, markCardLost, recoverCard, replaceCard, revokeCard } from '../features/cards/data-store';
import type { CredentialStatus } from '../lib/synthetic-data';

/**
 * BIO-UX-012-AC03 (Human Test Console): deterministically ensures a
 * credential in exactly `target` status exists for `personId`, so choosing
 * a credential state has a real, observable effect on `/cards` — not just
 * navigation. Calls the SAME pure, already-exported mutation functions
 * `CardsRegistryApp.tsx`'s own "Mark lost"/"Recover"/"Revoke card" buttons
 * call (`data-store.ts`) — not the async `cardsMarkLostPort`/etc, which
 * wraps them in a success/failure/offline scenario cycle that would make
 * this preset itself non-deterministic. This does not add a new mutation
 * or change any existing one; it only orchestrates the existing ones.
 *
 * Idempotent: if a credential in `target` status already exists for this
 * person, does nothing (repeated visits with the same target don't grow
 * the registry). Every other target issues one fresh card and transitions
 * it — always reachable regardless of the person's existing credential
 * history, since a freshly issued card always starts `active`.
 */
export function ensureCredentialState(tenantId: string, personId: string, target: CredentialStatus): void {
  const already = listCredentials(tenantId).some((credential) => credential.personId === personId && credential.status === target);
  if (already) {
    return;
  }

  if (target === 'active') {
    // issueCard only fails if the person already holds an active card, which the check above ruled out.
    issueCard(tenantId, personId);
    return;
  }

  const issued = issueCard(tenantId, personId);
  const cardId =
    issued.ok && issued.credential
      ? issued.credential.id
      : listCredentials(tenantId).find((credential) => credential.personId === personId && credential.status === 'active')?.id;
  if (!cardId) {
    return;
  }

  const REASON = 'SIMULATED preset (Human Test Console)';
  if (target === 'lost') {
    markCardLost(tenantId, cardId, REASON);
  } else if (target === 'recovered') {
    markCardLost(tenantId, cardId, REASON);
    recoverCard(tenantId, cardId);
  } else if (target === 'revoked') {
    revokeCard(tenantId, cardId, REASON);
  } else if (target === 'replaced') {
    replaceCard(tenantId, cardId, REASON);
  }
}
