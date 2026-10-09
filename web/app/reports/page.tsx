'use client';

import { useMemo, useState } from 'react';
import { useSession } from '../../context/session-context';
import { generateDataset } from '../../lib/synthetic-data';
import { computeReportRows, REPORT_LABELS, REPORT_TYPES, type ReportType } from '../../lib/report-rows';
import { useModuleAction } from '../../lib/use-module-action';
import { ModuleActionStatus } from '../../components/module-action-status';
import { ReportBuilderCard } from '../../features/reporting/ReportBuilderCard';

/**
 * Reports (BIO-UX-011-AC01). "Run a report" (below, unchanged since BIO-UX-011) calls
 * `ReportingService`'s generic `primaryAction` port — BIO-RPT-001's own real notification-demo
 * endpoint, not this page's own reporting domain (see `web/features/reporting/ports.ts`'s own
 * header for why) — and separately renders a deterministic synthetic summary table for the
 * selected report type (`lib/report-rows.ts`, unit-tested), from BIO-UX-004's dataset, unrelated
 * to that button's own live/mock outcome. `ReportBuilderCard` (BIO-RPT-002-AC01/AC04/AC06) is
 * this route's own real governed-report connection, added alongside without touching the
 * existing card above.
 */
export default function ReportsPage() {
  const session = useSession();
  const [reportType, setReportType] = useState<ReportType>('enrolment-summary');
  const action = useModuleAction('ReportingService');
  const dataset = useMemo(() => generateDataset(), []);
  const tenantId = session.tenant.id;

  const table = useMemo(
    () => computeReportRows(reportType, dataset, tenantId),
    [reportType, dataset, tenantId],
  );

  return (
    <div className="biolog-placeholder-page">
      <h1>Reports</h1>
      <p className="biolog-placeholder-description">
        {session.tenant.name} — operational and compliance reporting.
      </p>
      <div className="biolog-placeholder-cards">
        <ReportBuilderCard />
        <section className="biolog-card">
          <h2>Run a report</h2>
          <div className="biolog-field-row">
            <label htmlFor="report-type">Report type</label>
            <select
              id="report-type"
              value={reportType}
              onChange={(e) => setReportType(e.target.value as ReportType)}
            >
              {REPORT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {REPORT_LABELS[type]}
                </option>
              ))}
            </select>
          </div>
          <button
            type="button"
            className="biolog-button-primary"
            onClick={action.run}
            disabled={action.state.status === 'loading'}
          >
            {action.state.status === 'loading' ? 'Running…' : 'Run report'}
          </button>
          <ModuleActionStatus state={action.state} />
          {action.state.status === 'done' && action.state.outcome === 'success' && (
            <table className="biolog-table" aria-label={`${REPORT_LABELS[reportType]} results (SYNTHETIC)`}>
              <thead>
                <tr>
                  <th scope="col">{table.columns[0]}</th>
                  <th scope="col">{table.columns[1]}</th>
                </tr>
              </thead>
              <tbody>
                {table.rows.map(([left, right]) => (
                  <tr key={left}>
                    <td>{left}</td>
                    <td>{right}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <aside className="biolog-card biolog-placeholder-notes">
          <h2>Design principles</h2>
          <ul>
            <li>Simple default view with progressive disclosure</li>
            <li>Tenant-scoped data and permissions</li>
            <li>Clear status and actionable errors</li>
          </ul>
        </aside>
      </div>
    </div>
  );
}
