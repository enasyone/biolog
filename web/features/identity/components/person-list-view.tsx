'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { StatusBadge } from '../../../components/status-badge';
import { useSession } from '../../../context/session-context';
import type { Permission } from '../../../lib/permissions';
import { identityPort } from '../index';
import type { PersonCategory, PersonSummary } from '../types';
import { EmptyState, ErrorState, LoadingState, OfflineState, PermissionDeniedState } from './page-states';

export interface PersonListViewProps {
  title: string;
  description: string;
  category: PersonCategory | 'all';
  requiredPermission: Permission;
  /** Only the People list shows this (route-manifest.ts: People's own primary action). */
  showEnrolmentCta?: boolean;
  /**
   * Only the People list shows this (route-manifest.ts: People's own secondary action). Governs
   * both the per-row Suspend action and its symmetric Reinstate action — a suspended person must
   * be reinstatable from the same list they were suspended from, not only from their detail page
   * (BIO-UX-006 post-acceptance fix: the list originally only ever rendered Suspend, leaving no
   * way back to active without navigating away first).
   */
  showSuspendAction?: boolean;
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; people: readonly PersonSummary[] };

type RowActionState = { personId: string; status: 'working' } | { personId: string; status: 'done'; message: string } | null;

/**
 * Shared list screen for People/Students/Staff/Alumni/Contractors
 * (BIO-UX-006-AC01, AC04, AC05). One component parameterised by
 * `category` rather than five near-duplicate page components — a person
 * record has the same shape regardless of which list surfaced it, and
 * `route-manifest.ts` already differs only by which primary/secondary
 * actions each route shows, which this component's own props express.
 */
export function PersonListView({
  title,
  description,
  category,
  requiredPermission,
  showEnrolmentCta = false,
  showSuspendAction = false,
}: PersonListViewProps) {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [query, setQuery] = useState('');
  const [rowAction, setRowAction] = useState<RowActionState>(null);

  const canView = session.permissions.has(requiredPermission);

  const load = useCallback(() => {
    if (!canView) return;
    setState({ status: 'loading' });
    identityPort
      .listPeople({ tenantId: session.tenant.id, category })
      .then((result) => setState({ status: 'ready', people: result.people }))
      .catch(() => setState({ status: 'error', message: 'Could not load the list — try again.' }));
  }, [canView, session.tenant.id, category]);

  // AC04: switching tenant re-runs this effect (session.tenant.id changed) and re-scopes the list —
  // no page reload, just a React state update.
  useEffect(() => {
    load();
  }, [load]);

  const filtered = useMemo(() => {
    if (state.status !== 'ready') return [];
    const needle = query.trim().toLowerCase();
    if (!needle) return state.people;
    return state.people.filter((p) => p.displayName.toLowerCase().includes(needle));
  }, [state, query]);

  // AC04: switching role re-evaluates this on every render — the Suspend column
  // disappears/reappears immediately, no reload.
  const canSuspend = showSuspendAction && session.role.id === 'admin';

  async function handleLifecycleAction(personId: string, kind: 'suspend' | 'reinstate') {
    setRowAction({ personId, status: 'working' });
    const result =
      kind === 'suspend'
        ? await identityPort.suspendPerson({ tenantId: session.tenant.id, personId })
        : await identityPort.reinstatePerson({ tenantId: session.tenant.id, personId });
    setRowAction({ personId, status: 'done', message: result.message });
    if (result.outcome === 'success') load();
  }

  function handleExport() {
    const rows = filtered.map((p) => [p.id, p.displayName, p.category, p.lifecycleStatus].join(','));
    const csv = ['id,display_name,category,lifecycle_status (SYNTHETIC export)', ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${category}-synthetic-export.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  if (!canView) {
    return (
      <div className="biolog-placeholder-page">
        <h1>{title}</h1>
        <PermissionDeniedState>
          Your current role ({session.role.name}) does not include the &ldquo;{requiredPermission}&rdquo;
          permission required to view this page.
        </PermissionDeniedState>
      </div>
    );
  }

  return (
    <div className="biolog-placeholder-page">
      <h1>{title}</h1>
      <p className="biolog-placeholder-description">{description}</p>

      <section className="biolog-card biolog-identity-list-card">
        <div className="biolog-identity-toolbar">
          {showEnrolmentCta && (
            <Link href="/enrolment" className="biolog-button-primary">
              New Enrolment
            </Link>
          )}
          <input
            type="search"
            placeholder="Filter by name"
            aria-label="Filter by name"
            className="biolog-identity-filter-input"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          <button type="button" className="biolog-button-secondary" onClick={handleExport}>
            Export list
          </button>
        </div>

        {state.status === 'loading' && <LoadingState />}
        {state.status === 'error' && <ErrorState>{state.message}</ErrorState>}
        {state.status === 'offline' && <OfflineState>{state.message}</OfflineState>}
        {state.status === 'ready' && filtered.length === 0 && (
          <EmptyState>No {title.toLowerCase()} match this tenant/filter (SYNTHETIC data).</EmptyState>
        )}
        {state.status === 'ready' && filtered.length > 0 && (
          <table className="biolog-table" aria-label={`${title} directory (SYNTHETIC)`}>
            <thead>
              <tr>
                <th>Name</th>
                <th>Category</th>
                <th>Lifecycle</th>
                <th>Duplicate</th>
                <th>
                  <span className="biolog-visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((person) => (
                <tr key={person.id}>
                  <td>{person.displayName}</td>
                  <td>{person.category}</td>
                  <td>
                    <StatusBadge tone={person.lifecycleStatus === 'active' ? 'success' : 'error'}>
                      {person.lifecycleStatus}
                    </StatusBadge>
                  </td>
                  <td>{person.hasDuplicateFlag ? 'Flagged' : '—'}</td>
                  <td className="biolog-identity-row-actions">
                    <Link href={`/people/${person.id}`}>View profile</Link>
                    {canSuspend && person.lifecycleStatus === 'active' && (
                      <button
                        type="button"
                        className="biolog-button-secondary"
                        disabled={rowAction?.personId === person.id && rowAction.status === 'working'}
                        onClick={() => handleLifecycleAction(person.id, 'suspend')}
                      >
                        {rowAction?.personId === person.id && rowAction.status === 'working'
                          ? 'Working…'
                          : 'Suspend (emergency)'}
                      </button>
                    )}
                    {canSuspend && person.lifecycleStatus === 'suspended' && (
                      <button
                        type="button"
                        className="biolog-button-secondary"
                        disabled={rowAction?.personId === person.id && rowAction.status === 'working'}
                        onClick={() => handleLifecycleAction(person.id, 'reinstate')}
                      >
                        {rowAction?.personId === person.id && rowAction.status === 'working'
                          ? 'Working…'
                          : 'Reinstate'}
                      </button>
                    )}
                    {rowAction?.personId === person.id && rowAction.status === 'done' && (
                      <span role="status" className="biolog-identity-status biolog-identity-status--inline">
                        {rowAction.message}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
