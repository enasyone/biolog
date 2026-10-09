'use client';

import { useMemo } from 'react';
import { useSession } from '../../context/session-context';
import { generateDataset } from '../../lib/synthetic-data';
import { computeAuditRows } from '../../lib/audit-rows';
import { useModuleAction } from '../../lib/use-module-action';
import { ModuleActionStatus } from '../../components/module-action-status';
import { StatusBadge } from '../../components/status-badge';
import AuditInvestigationPanel from '../../features/bio-rpt-003/AuditInvestigationPanel';

const DECISION_TONE = { granted: 'success', denied: 'error' } as const;

/**
 * Audit (BIO-UX-011-AC01). A synthetic audit trail table from BIO-UX-004's
 * dataset (`lib/audit-rows.ts`, unit-tested), tenant-filtered (AC04), above
 * the `viewAuditTrail` port action BIO-UX-005 already wired to this route.
 */
export default function AuditPage() {
  const session = useSession();
  const action = useModuleAction('ReportingService', 'viewAuditTrail');
  const dataset = useMemo(() => generateDataset(), []);
  const tenantId = session.tenant.id;
  const rows = useMemo(() => computeAuditRows(dataset, tenantId), [dataset, tenantId]);

  return (
    <div className="biolog-placeholder-page">
      <h1>Audit</h1>
      <p className="biolog-placeholder-description">
        {session.tenant.name} — audit trail of security-sensitive actions (SYNTHETIC).
      </p>
      <div className="biolog-placeholder-cards">
        <section className="biolog-card">
          <h2>Recent access events</h2>
          {rows.length === 0 ? (
            <p className="biolog-empty-state">No audit events for this tenant (SYNTHETIC).</p>
          ) : (
            <table className="biolog-table" aria-label="Recent access events (SYNTHETIC)">
              <thead>
                <tr>
                  <th scope="col">Time</th>
                  <th scope="col">Actor</th>
                  <th scope="col">Device</th>
                  <th scope="col">Decision</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>{formatTimestamp(row.occurredAt)}</td>
                    <td>{row.actor}</td>
                    <td>{row.device}</td>
                    <td>
                      <StatusBadge tone={DECISION_TONE[row.decision]}>{row.decision}</StatusBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <button
            type="button"
            className="biolog-button-primary"
            onClick={action.run}
            disabled={action.state.status === 'loading'}
          >
            {action.state.status === 'loading' ? 'Working…' : 'View audit trail'}
          </button>
          <ModuleActionStatus state={action.state} />
        </section>
        <aside className="biolog-card biolog-placeholder-notes">
          <h2>Design principles</h2>
          <ul>
            <li>Security-sensitive actions audited</li>
            <li>Tenant-scoped data and permissions</li>
            <li>Clear status and actionable errors</li>
          </ul>
        </aside>
        <AuditInvestigationPanel />
      </div>
    </div>
  );
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' });
}
