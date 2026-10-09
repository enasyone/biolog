'use client';

import { useCallback, useEffect, useState } from 'react';
import { useSession } from '../../../context/session-context';
import { identityPort } from '../index';
import type { DuplicateGroup } from '../types';
import { EmptyState, ErrorState, LoadingState, PermissionDeniedState } from './page-states';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; groups: readonly DuplicateGroup[] };

/**
 * Duplicate Review screen (BIO-UX-006-AC03). Reads from the
 * `duplicate-identity` synthetic scenario specifically (`data-store.ts`'s
 * `duplicateReviewStore`) so a candidate is always present on first load —
 * there is no scenario-switcher UI yet (BIO-UX-012, Human Test Console, not
 * built) for a human to opt into that scenario themselves.
 */
export function DuplicateReviewView() {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [resolving, setResolving] = useState<string | null>(null);
  const [lastMessage, setLastMessage] = useState<string | null>(null);

  const canView = session.permissions.has('nav:duplicate-review');

  const load = useCallback(() => {
    if (!canView) return;
    setState({ status: 'loading' });
    identityPort
      .listDuplicates({ tenantId: session.tenant.id })
      .then((result) => setState({ status: 'ready', groups: result.groups }))
      .catch(() => setState({ status: 'error', message: 'Could not load duplicate groups — try again.' }));
  }, [canView, session.tenant.id]);

  useEffect(() => {
    load();
  }, [load]);

  async function handleResolve(group: DuplicateGroup, keepPersonId: string) {
    setResolving(group.groupId);
    const result = await identityPort.resolveDuplicate({
      tenantId: session.tenant.id,
      groupId: group.groupId,
      keepPersonId,
    });
    setLastMessage(result.message);
    setResolving(null);
    if (result.outcome === 'success') load();
  }

  if (!canView) {
    return (
      <div className="biolog-placeholder-page">
        <h1>Duplicate Review</h1>
        <PermissionDeniedState>
          Your current role ({session.role.name}) does not include the &ldquo;nav:duplicate-review&rdquo;
          permission required to view this page.
        </PermissionDeniedState>
      </div>
    );
  }

  return (
    <div className="biolog-placeholder-page">
      <h1>Duplicate Review</h1>
      <p className="biolog-placeholder-description">
        Possible duplicate identities for {session.tenant.name}, from the &ldquo;duplicate-identity&rdquo;
        synthetic scenario (SYNTHETIC data).
      </p>

      {state.status === 'loading' && <LoadingState />}
      {state.status === 'error' && <ErrorState>{state.message}</ErrorState>}
      {state.status === 'ready' && state.groups.length === 0 && (
        <EmptyState>No unresolved duplicate groups for this tenant.</EmptyState>
      )}
      {state.status === 'ready' &&
        state.groups.map((group) => (
          <section key={group.groupId} className="biolog-card biolog-identity-duplicate-group">
            <h2>Possible duplicate</h2>
            <ul className="biolog-identity-duplicate-candidates">
              {group.candidates.map((candidate) => (
                <li key={candidate.personId}>
                  <span>
                    {candidate.displayName} ({candidate.category})
                  </span>
                  <button
                    type="button"
                    className="biolog-button-secondary"
                    disabled={resolving === group.groupId}
                    onClick={() => handleResolve(group, candidate.personId)}
                  >
                    {resolving === group.groupId ? 'Working…' : `Keep this record`}
                  </button>
                </li>
              ))}
            </ul>
          </section>
        ))}
      {lastMessage && (
        <p role="status" className="biolog-identity-status biolog-identity-status--inline">
          {lastMessage}
        </p>
      )}
    </div>
  );
}
