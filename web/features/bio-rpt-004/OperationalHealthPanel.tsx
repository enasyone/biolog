'use client';

import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useSession } from '../../context/session-context';
import { StatusBadge } from '../../components/status-badge';
import { isNavItemVisible, type NavItem } from '../../lib/permissions';
import { operationalHealthPort, type OperationalHealthSnapshot, type PanelFreshness, type UnavailableHealthPanel } from '.';

/**
 * Operational health (BIO-RPT-004-AC01-AC05). Six panels - identity, card, device, sync, access,
 * production state - connected through `ReportingService`'s new `operationalHealth` port (AC05),
 * additive to the existing Dashboard page (BIO-UX-011), not a redesign of it.
 *
 * AC01/F01 (authorised tenant scope - Seyi round-1): the backend now gates this read behind a real
 * permission check, not just tenant scoping - a `403`/`400` surfaces here as the component's own
 * `error` state (see `load()` below), the same non-dead-button discipline every live port in this
 * epic already uses for a refused action.
 *
 * AC02 (drill-down links preserve role permissions): each panel's drill-down reuses the SAME
 * `requiredPermission`/`isNavItemVisible` mechanism `app-shell.tsx`'s own sidebar already applies
 * to the identical destination route - a role that cannot see "Devices" in the sidebar cannot see
 * this panel's "View devices" link either, by construction, not by a second, independently
 * maintained permission list. Sync has no dedicated nav item/route in this shell, so it renders no
 * drill-down at all - a metric-only tile has nothing a permission gate would need to hide.
 *
 * AC03/F02 (stale/offline data is labelled - Seyi round-1): every panel renders a `StatusBadge`
 * from its own `freshness` field, now THREE visibly distinct tones/labels so a degraded live
 * source never looks identical to either a fresh one or a permanently-unavailable one -
 * `'live'` (success/green/"Live"), `'stale'` (warning/amber/"Stale", a real source whose most
 * recent read failed, with the server's own stated reason), `'unavailable'` (neutral/grey/
 * "Unavailable", no source exists for this domain at all, also with a stated reason). Every panel
 * also shows its own `observedAt` - no snapshot-level-only timestamp.
 *
 * AC04 (no cross-tenant aggregation): this component never requests or renders a second tenant's
 * data - the port takes no tenant parameter at all (the backend resolves one tenant server-side,
 * see `DashboardService`'s own header), so there is no prop or state here that could even hold a
 * second tenant's numbers.
 */
const DRILL_DOWNS: Readonly<Record<'identity' | 'card' | 'device' | 'access' | 'production', NavItem>> = Object.freeze({
  identity: { id: 'dashboard-drilldown-identity', label: 'View people', href: '/people', requiredPermission: 'nav:people' },
  card: { id: 'dashboard-drilldown-card', label: 'View cards', href: '/cards', requiredPermission: 'nav:cards' },
  device: { id: 'dashboard-drilldown-device', label: 'View devices', href: '/devices', requiredPermission: 'nav:devices' },
  access: { id: 'dashboard-drilldown-access', label: 'View access control', href: '/access-control', requiredPermission: 'nav:access-control' },
  production: { id: 'dashboard-drilldown-production', label: 'View production', href: '/cards', requiredPermission: 'nav:cards' },
});

type HealthState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; message: string; snapshot: OperationalHealthSnapshot }
  | { status: 'error'; message: string };

const FRESHNESS_LABEL: Readonly<Record<PanelFreshness, string>> = Object.freeze({ live: 'Live', stale: 'Stale', unavailable: 'Unavailable' });
const FRESHNESS_TONE: Readonly<Record<PanelFreshness, 'success' | 'warning' | 'neutral'>> = Object.freeze({ live: 'success', stale: 'warning', unavailable: 'neutral' });

function FreshnessBadge({ freshness }: { freshness: PanelFreshness }) {
  return <StatusBadge tone={FRESHNESS_TONE[freshness]}>{FRESHNESS_LABEL[freshness]}</StatusBadge>;
}

function ObservedAt({ observedAt }: { observedAt: string }) {
  return <p className="biolog-activity-time">as of {observedAt}</p>;
}

/**
 * BIO-RPT-004-F04: a `'stale'` panel's counts are the RETAINED last-known values, never a
 * fabricated zero - labelled "Last known" so a viewer never mistakes them for a fresh reading.
 * `total === null` means no successful read has ever happened for this tenant yet - rendered as
 * an explicit empty state, not a silently-missing number.
 */
function CountLine({ freshness, total, children }: { freshness: PanelFreshness; total: number | null; children: ReactNode }) {
  if (total === null) {
    return <p className="biolog-empty-state">No data has ever been successfully observed for this tenant yet.</p>;
  }
  return <p>{freshness === 'stale' ? 'Last known: ' : ''}{children}</p>;
}

function DrillDown({ panelKey }: { panelKey: keyof typeof DRILL_DOWNS }) {
  const session = useSession();
  const item = DRILL_DOWNS[panelKey];
  if (!isNavItemVisible(item, session.permissions)) return null;
  return (
    <Link href={item.href} data-testid={item.id}>
      {item.label}
    </Link>
  );
}

function UnavailableTile({ title, panelKey, panel }: { title: string; panelKey: 'card' | 'device' | 'sync'; panel: UnavailableHealthPanel }) {
  return (
    <div className="biolog-card" data-testid={`operational-health-panel-${panelKey}`}>
      <h3>{title}</h3>
      <FreshnessBadge freshness={panel.freshness} />
      <p className="biolog-empty-state biolog-health-reason">{panel.reason}</p>
      <ObservedAt observedAt={panel.observedAt} />
      {panelKey !== 'sync' && <DrillDown panelKey={panelKey} />}
    </div>
  );
}

export function OperationalHealthPanel() {
  const session = useSession();
  const [state, setState] = useState<HealthState>({ status: 'idle' });

  async function load() {
    setState({ status: 'loading' });
    const result = await operationalHealthPort.primaryAction();
    if (result.outcome === 'success' && result.snapshot) {
      setState({ status: 'success', message: result.message, snapshot: result.snapshot });
    } else {
      setState({ status: 'error', message: result.message });
    }
  }

  useEffect(() => {
    void load();
    // Re-fetch whenever the selected tenant changes (BIO-RPT-004-AC01/AC04: the shell's tenant
    // selector must visibly change this panel's numbers too, the same discipline BIO-UX-011's
    // own synthetic dashboard metrics above already follow).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.tenant.id]);

  const snapshot = state.status === 'success' ? state.snapshot : undefined;

  return (
    <section className="biolog-card" data-testid="operational-health">
      <h2>Operational health</h2>
      <p className="biolog-health-description">
        {session.tenant.name} — identity, card, device, sync, access and production state.
      </p>

      {snapshot && (
        <div className="biolog-metrics-grid" data-testid="operational-health-panels">
          <div className="biolog-card" data-testid="operational-health-panel-identity">
            <h3>Identity</h3>
            <FreshnessBadge freshness={snapshot.identity.freshness} />
            <CountLine freshness={snapshot.identity.freshness} total={snapshot.identity.total}>
              {snapshot.identity.total} people ({snapshot.identity.active} active, {snapshot.identity.suspended} suspended)
            </CountLine>
            {snapshot.identity.reason && <p className="biolog-empty-state biolog-health-reason">{snapshot.identity.reason}</p>}
            <ObservedAt observedAt={snapshot.identity.observedAt} />
            <DrillDown panelKey="identity" />
          </div>

          <UnavailableTile title="Card" panelKey="card" panel={snapshot.card} />
          <UnavailableTile title="Device" panelKey="device" panel={snapshot.device} />
          <UnavailableTile title="Sync" panelKey="sync" panel={snapshot.sync} />

          <div className="biolog-card" data-testid="operational-health-panel-access">
            <h3>Access</h3>
            <FreshnessBadge freshness={snapshot.access.freshness} />
            <CountLine freshness={snapshot.access.freshness} total={snapshot.access.total}>
              {snapshot.access.total} decisions ({snapshot.access.allowed} allowed, {snapshot.access.denied} denied)
            </CountLine>
            {snapshot.access.reason && <p className="biolog-empty-state biolog-health-reason">{snapshot.access.reason}</p>}
            <ObservedAt observedAt={snapshot.access.observedAt} />
            <DrillDown panelKey="access" />
          </div>

          <div className="biolog-card" data-testid="operational-health-panel-production">
            <h3>Production state</h3>
            <FreshnessBadge freshness={snapshot.production.freshness} />
            <CountLine freshness={snapshot.production.freshness} total={snapshot.production.total}>
              {snapshot.production.total} jobs ({snapshot.production.passed} passed, {snapshot.production.failed} failed, {snapshot.production.inProgress} in progress)
            </CountLine>
            {snapshot.production.reason && <p className="biolog-empty-state biolog-health-reason">{snapshot.production.reason}</p>}
            <ObservedAt observedAt={snapshot.production.observedAt} />
            <DrillDown panelKey="production" />
          </div>
        </div>
      )}

      <button type="button" className="biolog-button-primary" onClick={load} disabled={state.status === 'loading'}>
        {state.status === 'loading' ? 'Working…' : 'Refresh operational health'}
      </button>
      {state.status === 'error' && (
        <p role="status" className="biolog-placeholder-status biolog-placeholder-status--failure">
          {state.message}
        </p>
      )}
      {/* No redundant "success" status banner on auto-load (unlike the click-triggered
          ModuleActionStatus pattern above): the panels rendering IS the success indicator, and
          BIO-UX-002's own axe baseline scans "/" with no user action at all - a status banner
          visible by default, rather than only after an explicit click, is what first exposed a
          pre-existing color-contrast shortfall in the shared `.biolog-placeholder-status--success`
          class (4.14:1 vs the required 4.5:1). Fixing that shared class is a design-system change
          outside this story's scope (Damilola ownership) - not rendering a banner that was never
          load-bearing for any AC here avoids it entirely. */}
    </section>
  );
}
