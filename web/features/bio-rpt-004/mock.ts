import type { GetOperationalHealthPort, OperationalHealthResult, OperationalHealthSnapshot } from './ports';

/**
 * SIMULATION-mode fixture (BIO-RPT-004-AC01/AC03). Deterministic, not `Math.random()`/`Date.now()`
 * - the same discipline every other mock in this epic follows.
 *
 * Freshness here mirrors what LIVE mode's happy path looks like, not what port MODE is active:
 * whether a real tenant-wide data source exists for a domain (Identity/Access/Production: yes;
 * Card/Device/Sync: no) is a permanent, structural fact independent of SIMULATION vs LIVE - see
 * docs/features/bio-rpt-004/README.md "Which sources are wired, and why". So Identity/Access/
 * Production are `'live'` here (with plausible synthetic numbers, same as every other mock port
 * in this app), and Card/Device/Sync are `'unavailable'` with the identical reason text LIVE mode
 * returns - never `'stale'` (that state is reserved for a real source whose most recent read
 * genuinely failed, which this mock never simulates).
 */
const UNAVAILABLE_OBSERVED_AT = '2026-09-20T09:00:00.000Z';

function buildSnapshot(tenantId: string): OperationalHealthSnapshot {
  return {
    tenantId,
    computedAt: '2026-09-20T09:00:00.000Z',
    identity: { total: 42, active: 36, suspended: 4, other: 2, freshness: 'live', observedAt: '2026-09-20T09:00:00.000Z' },
    card: {
      freshness: 'unavailable',
      reason: "No tenant-wide read exists yet over BIO-CRD-005/006's own card repositories - ICardIssueRepository/ICardLifecycleRepository expose no ListByTenant method. Disclosed HYBRID gap (BIO-RPT-004-AC05), not a fabricated number.",
      observedAt: UNAVAILABLE_OBSERVED_AT,
    },
    device: {
      freshness: 'unavailable',
      reason: "BIO-DEV-001's IDeviceRepository has no tenant-wide list method and Device.LastSeenAt is never populated by any existing code path. Disclosed HYBRID gap (BIO-RPT-004-AC05), not a fabricated number.",
      observedAt: UNAVAILABLE_OBSERVED_AT,
    },
    sync: {
      freshness: 'unavailable',
      reason: "BIO-MOB-003's offline-sync feature exposes no tenant-wide read of pending/failed mutations. Disclosed HYBRID gap (BIO-RPT-004-AC05), not a fabricated number.",
      observedAt: UNAVAILABLE_OBSERVED_AT,
    },
    access: { total: 18, allowed: 15, denied: 3, freshness: 'live', observedAt: '2026-09-20T09:00:00.000Z' },
    production: { total: 9, passed: 6, failed: 1, rejected: 1, inProgress: 1, cancelled: 0, freshness: 'live', observedAt: '2026-09-20T09:00:00.000Z' },
  };
}

export const operationalHealthMockPort: GetOperationalHealthPort = {
  module: 'ReportingService',
  mode: 'mock',
  async primaryAction(): Promise<OperationalHealthResult> {
    return { outcome: 'success', message: 'Operational health snapshot computed (SIMULATION).', snapshot: buildSnapshot('demo-tenant') };
  },
};
