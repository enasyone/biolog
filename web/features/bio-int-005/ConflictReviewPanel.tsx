'use client';

import { useState } from 'react';
import { useSession } from '../../context/session-context';
import { decideConflictPort, listConflictsPort, scanConflictsPort } from './index';
import type { ImportConflictDecision, ImportConflictReviewSummary } from './index';

type LoadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; reviews: readonly ImportConflictReviewSummary[] };

const DECISIONS: readonly { value: ImportConflictDecision; label: string }[] = [
  { value: 'KEEP_EXISTING', label: 'Keep existing' },
  { value: 'USE_SOURCE', label: 'Use source' },
  { value: 'ESCALATED', label: 'Escalate' },
];

/**
 * BIO-INT-005-AC05: the real "Conflict Review" panel on `/administration/integrations`, mounted
 * ALONGSIDE BIO-INT-002's `ImportConsole`, BIO-INT-003's `ServiceAccountsPanel` and BIO-INT-004's
 * `WebhookSubscriptionsPanel` (never replacing any of them - see
 * `web/app/administration/integrations/page.tsx`). Scan one mapping's own import history for
 * conflicting rows (AC01), let an operator keep the existing BIOLOG value, use the source value,
 * or escalate it (AC02), each decision recorded and audited server-side (AC03), never applying
 * anything itself (AC04 - no PERSON write exists in this codebase, see `ports.ts`'s own header).
 * SYNTHETIC data only.
 */
export function ConflictReviewPanel() {
  const session = useSession();
  const [mappingId, setMappingId] = useState('map_demo-sis-import');
  const [state, setState] = useState<LoadState>({ status: 'idle' });
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<{ message: string; isError: boolean } | null>(null);
  const [decisionDrafts, setDecisionDrafts] = useState<Record<string, { decision: ImportConflictDecision; reasonCode: string }>>({});

  function draftFor(externalKey: string) {
    return decisionDrafts[externalKey] ?? { decision: 'KEEP_EXISTING' as ImportConflictDecision, reasonCode: '' };
  }

  function setDraft(externalKey: string, next: Partial<{ decision: ImportConflictDecision; reasonCode: string }>) {
    setDecisionDrafts((current) => ({ ...current, [externalKey]: { ...draftFor(externalKey), ...next } }));
  }

  async function handleScan() {
    setState({ status: 'loading' });
    setNotice(null);
    const result = await scanConflictsPort.primaryAction({ tenantId: session.tenant.id, mappingId });
    if (result.outcome === 'offline') {
      setState({ status: 'offline', message: result.message });
    } else if (result.outcome === 'failure') {
      setState({ status: 'error', message: result.message });
    } else {
      setState({ status: 'ready', reviews: result.reviews ?? [] });
    }
  }

  async function handleList() {
    // Deliberately does NOT clear `notice` here - handleDecide calls this to refresh the table
    // immediately after a successful decision, and the decision's own success notice must
    // survive that refresh, not be wiped out by it. Call sites that want a clean slate before a
    // fresh explicit load (handleScan) clear `notice` themselves first.
    setState({ status: 'loading' });
    const result = await listConflictsPort.primaryAction({ tenantId: session.tenant.id, mappingId });
    if (result.outcome === 'offline') {
      setState({ status: 'offline', message: result.message });
    } else if (result.outcome === 'failure') {
      setState({ status: 'error', message: result.message });
    } else {
      setState({ status: 'ready', reviews: result.reviews ?? [] });
    }
  }

  async function handleDecide(review: ImportConflictReviewSummary) {
    const draft = draftFor(review.externalKey);
    if (!draft.reasonCode.trim()) {
      setNotice({ message: 'A reason is required to record a decision.', isError: true });
      return;
    }

    setWorking(true);
    setNotice(null);
    try {
      const result = await decideConflictPort.primaryAction({
        tenantId: session.tenant.id,
        mappingId,
        externalKey: review.externalKey,
        decision: draft.decision,
        reasonCode: draft.reasonCode,
        expectedVersion: review.version,
      });
      setNotice({ message: result.message, isError: result.outcome !== 'success' });
      if (result.outcome === 'success') {
        await handleList();
      }
    } finally {
      setWorking(false);
    }
  }

  return (
    <section className="biolog-card" data-testid="conflict-review-panel">
      <h2>Conflict Review</h2>
      <p className="biolog-placeholder-description">
        External import rows that disagree with the current BIOLOG value, routed to human review (BIO-INT-005). SYNTHETIC data only.
      </p>

      <div className="biolog-identity-toolbar">
        <input
          type="text"
          aria-label="Mapping ID"
          data-testid="conflict-review-mapping-id-input"
          value={mappingId}
          onChange={(e) => setMappingId(e.target.value)}
        />
        <button type="button" className="biolog-button-primary" data-testid="conflict-review-scan" disabled={working || !mappingId.trim()} onClick={handleScan}>
          Scan for conflicts
        </button>
        <button type="button" data-testid="conflict-review-refresh" disabled={working || !mappingId.trim()} onClick={handleList}>
          Refresh
        </button>
      </div>

      {state.status === 'loading' && <p role="status">Loading…</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}
      {state.status === 'offline' && <p role="alert">{state.message}</p>}

      {state.status === 'ready' && (
        <table className="biolog-table" aria-label="Conflict reviews" data-testid="conflict-review-table">
          <thead>
            <tr>
              <th>External key</th>
              <th>Conflicting fields</th>
              <th>Status</th>
              <th>Decision</th>
              <th>
                <span className="biolog-visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {state.reviews.length === 0 && (
              <tr>
                <td colSpan={5} data-testid="conflict-review-empty">No conflicts on file for this mapping.</td>
              </tr>
            )}
            {state.reviews.map((review) => {
              const draft = draftFor(review.externalKey);
              const decidable = review.status === 'PENDING_REVIEW';
              return (
                <tr key={review.externalKey} data-testid="conflict-review-row">
                  <td>
                    <code>{review.externalKey}</code>
                  </td>
                  <td>
                    {review.conflicts.map((c) => (
                      <div key={c.personField} data-testid="conflict-review-field">
                        <strong>{c.personField}</strong>: existing <code>{c.existingValue ?? '—'}</code>, incoming <code>{c.incomingValue ?? '—'}</code>
                      </div>
                    ))}
                  </td>
                  <td data-testid="conflict-review-status">{review.status.replaceAll('_', ' ')}</td>
                  <td>
                    {decidable ? (
                      <div className="biolog-identity-toolbar">
                        <select
                          aria-label={`Decision for ${review.externalKey}`}
                          data-testid="conflict-review-decision-select"
                          value={draft.decision}
                          onChange={(e) => setDraft(review.externalKey, { decision: e.target.value as ImportConflictDecision })}
                        >
                          {DECISIONS.map((d) => (
                            <option key={d.value} value={d.value}>
                              {d.label}
                            </option>
                          ))}
                        </select>
                        <input
                          type="text"
                          aria-label={`Reason for ${review.externalKey}`}
                          placeholder="Reason"
                          data-testid="conflict-review-reason-input"
                          value={draft.reasonCode}
                          onChange={(e) => setDraft(review.externalKey, { reasonCode: e.target.value })}
                        />
                      </div>
                    ) : (
                      <span>
                        {review.decisionReasonCode ?? '—'}
                      </span>
                    )}
                  </td>
                  <td>
                    {decidable && (
                      <button type="button" disabled={working} data-testid="conflict-review-decide" onClick={() => handleDecide(review)}>
                        Record decision
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {notice && (
        <p role={notice.isError ? 'alert' : undefined} data-testid="conflict-review-notice">
          {notice.message}
        </p>
      )}
    </section>
  );
}
