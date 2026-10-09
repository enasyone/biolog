'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { StatusBadge } from '../../../components/status-badge';
import { useSession } from '../../../context/session-context';
import { identityPort } from '../index';
import type { PersonDetail } from '../types';
import { EmptyState, ErrorState, LoadingState, PermissionDeniedState } from './page-states';

export interface PersonDetailViewProps {
  personId: string;
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'not-found' }
  | { status: 'ready'; person: PersonDetail };

type ActionState = { status: 'idle' } | { status: 'working' } | { status: 'done'; message: string };

/**
 * Shared person detail screen (BIO-UX-006-AC01, AC03). One route
 * (`/people/[personId]`) serves every category — a Students/Staff/Alumni/
 * Contractors list's "View profile" action links here directly rather than
 * this story building four near-identical detail routes.
 */
export function PersonDetailView({ personId }: PersonDetailViewProps) {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [action, setAction] = useState<ActionState>({ status: 'idle' });

  const canView = session.permissions.has('nav:people');
  const canActOnLifecycle = session.role.id === 'admin';

  const load = useCallback(() => {
    if (!canView) return;
    setState({ status: 'loading' });
    identityPort
      .getPerson({ tenantId: session.tenant.id, personId })
      .then((result) => setState(result.person ? { status: 'ready', person: result.person } : { status: 'not-found' }))
      .catch(() => setState({ status: 'error', message: 'Could not load this profile — try again.' }));
  }, [canView, session.tenant.id, personId]);

  useEffect(() => {
    load();
  }, [load]);

  async function runLifecycleAction(kind: 'suspend' | 'reinstate') {
    setAction({ status: 'working' });
    const result =
      kind === 'suspend'
        ? await identityPort.suspendPerson({ tenantId: session.tenant.id, personId })
        : await identityPort.reinstatePerson({ tenantId: session.tenant.id, personId });
    setAction({ status: 'done', message: result.message });
    if (result.outcome === 'success') load();
  }

  if (!canView) {
    return (
      <div className="biolog-placeholder-page">
        <h1>Profile</h1>
        <PermissionDeniedState>
          Your current role ({session.role.name}) does not include the &ldquo;nav:people&rdquo; permission
          required to view profiles.
        </PermissionDeniedState>
      </div>
    );
  }

  return (
    <div className="biolog-placeholder-page">
      <Link href="/people" className="biolog-identity-back-link">
        ← Back to People
      </Link>
      {state.status === 'loading' && <LoadingState label="Loading profile…" />}
      {state.status === 'error' && <ErrorState>{state.message}</ErrorState>}
      {state.status === 'not-found' && <EmptyState>No person with this id exists in the current tenant.</EmptyState>}
      {state.status === 'ready' && (
        <>
          <h1>{state.person.displayName}</h1>
          <div className="biolog-identity-detail-grid">
            <section className="biolog-card">
              <h2>Identity</h2>
              <dl className="biolog-identity-dl">
                <dt>Category</dt>
                <dd>{state.person.category}</dd>
                <dt>Date of birth</dt>
                <dd>{state.person.dateOfBirth}</dd>
                <dt>Lifecycle status</dt>
                <dd>
                  <StatusBadge tone={state.person.lifecycleStatus === 'active' ? 'success' : 'error'}>
                    {state.person.lifecycleStatus}
                  </StatusBadge>
                </dd>
                {state.person.hasDuplicateFlag && (
                  <>
                    <dt>Duplicate review</dt>
                    <dd>
                      Flagged as a possible duplicate —{' '}
                      <Link href="/duplicate-review">review in Duplicate Review</Link>.
                    </dd>
                  </>
                )}
                {state.person.credential && (
                  <>
                    <dt>Credential</dt>
                    <dd>
                      {state.person.credential.cardNumber} ({state.person.credential.status})
                    </dd>
                  </>
                )}
                {state.person.photo && (
                  <>
                    <dt>Photo quality</dt>
                    <dd>
                      {state.person.photo.qualityScore}/100
                      {state.person.photo.belowQualityThreshold ? ' — below threshold' : ''}
                    </dd>
                  </>
                )}
              </dl>
            </section>

            <aside className="biolog-card">
              <h2>Lifecycle actions</h2>
              {!canActOnLifecycle && (
                <p className="biolog-identity-status biolog-identity-status--empty">
                  Emergency suspension/reinstatement requires the System Administrator role. Your current
                  role is {session.role.name}.
                </p>
              )}
              {canActOnLifecycle && state.person.lifecycleStatus === 'active' && (
                <button
                  type="button"
                  className="biolog-button-primary"
                  disabled={action.status === 'working'}
                  onClick={() => runLifecycleAction('suspend')}
                >
                  {action.status === 'working' ? 'Working…' : 'Suspend (emergency)'}
                </button>
              )}
              {canActOnLifecycle && state.person.lifecycleStatus === 'suspended' && (
                <button
                  type="button"
                  className="biolog-button-primary"
                  disabled={action.status === 'working'}
                  onClick={() => runLifecycleAction('reinstate')}
                >
                  {action.status === 'working' ? 'Working…' : 'Reinstate'}
                </button>
              )}
              {action.status === 'done' && (
                <p role="status" className="biolog-identity-status biolog-identity-status--inline">
                  {action.message}
                </p>
              )}
            </aside>
          </div>
        </>
      )}
    </div>
  );
}
