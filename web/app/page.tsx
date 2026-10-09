'use client';

import { useMemo } from 'react';
import { useSession } from '../context/session-context';
import { generateDataset } from '../lib/synthetic-data';
import { computeDashboardMetrics, computeRecentActivity } from '../lib/dashboard-metrics';
import { useModuleAction } from '../lib/use-module-action';
import { ModuleActionStatus } from '../components/module-action-status';
import { OperationalHealthPanel } from '../features/bio-rpt-004/OperationalHealthPanel';

/**
 * Dashboard (BIO-UX-011-AC01, AC03, AC04). Tenant-wide metrics and recent
 * activity, both deterministic synthetic data from BIO-UX-004's dataset
 * (`generateDataset` — a fixed seed, never `Math.random()`/`Date.now()`;
 * aggregation itself lives in `lib/dashboard-metrics.ts`, unit-tested),
 * filtered to the currently selected tenant so switching the header's
 * Tenant selector visibly changes every number on this page (AC04).
 */
export default function DashboardPage() {
  const session = useSession();
  const refresh = useModuleAction('ReportingService');

  const dataset = useMemo(() => generateDataset(), []);
  const tenantId = session.tenant.id;
  const metrics = useMemo(() => computeDashboardMetrics(dataset, tenantId), [dataset, tenantId]);
  const recentActivity = useMemo(() => computeRecentActivity(dataset, tenantId), [dataset, tenantId]);

  return (
    <div className="biolog-placeholder-page">
      <h1>Dashboard</h1>
      <p className="biolog-placeholder-description">
        {session.tenant.name} — metrics and recent activity (SYNTHETIC).
      </p>

      <div className="biolog-metrics-grid" data-testid="dashboard-metrics">
        <MetricTile label="People enrolled" value={metrics.peopleEnrolled} />
        <MetricTile label="Active credentials" value={metrics.activeCredentials} />
        <MetricTile label="Devices online" value={`${metrics.devicesOnline}/${metrics.devicesTotal}`} />
        <MetricTile label="Duplicate reviews pending" value={metrics.duplicateReviewsPending} />
      </div>

      <OperationalHealthPanel />

      <div className="biolog-placeholder-cards">
        <section className="biolog-card">
          <h2>Recent activity</h2>
          {recentActivity.length === 0 ? (
            <p className="biolog-empty-state">No recent activity for this tenant (SYNTHETIC).</p>
          ) : (
            <ul className="biolog-activity-list">
              {recentActivity.map((row) => (
                <li key={row.id}>
                  <span className="biolog-activity-label">{row.label}</span>
                  <span className="biolog-activity-detail">{row.detail}</span>
                  <span className="biolog-activity-time">{formatTimestamp(row.occurredAt)}</span>
                </li>
              ))}
            </ul>
          )}
          <button
            type="button"
            className="biolog-button-primary"
            onClick={refresh.run}
            disabled={refresh.state.status === 'loading'}
          >
            {refresh.state.status === 'loading' ? 'Working…' : 'Refresh dashboard'}
          </button>
          <ModuleActionStatus state={refresh.state} />
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

function MetricTile({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="biolog-metric-tile">
      <span className="biolog-metric-label">{label}</span>
      <span className="biolog-metric-value">{value}</span>
    </div>
  );
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  // TD-UX011-DASHBOARD-TZ-HYDRATION: an implicit local time zone here renders
  // differently on the server (server-process time zone) than on the client
  // (browser time zone), which is a real React hydration mismatch (#418)
  // whenever the two disagree — not just a test-harness artifact. A fixed,
  // explicit zone makes SSR and CSR output identical regardless of either
  // side's local setting. Africa/Lagos matches this tenant-facing app's own
  // established default (see Settings' General section timeZone default).
  return date.toLocaleString('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Africa/Lagos',
  });
}
