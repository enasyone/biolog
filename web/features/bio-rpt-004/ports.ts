import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-RPT-004's own dedicated connection: a single read-only operational-health snapshot across
 * identity/card/device/sync/access/production (AC01), each panel explicitly labelled live,
 * stale or unavailable (AC03). `primaryAction`/`viewAuditTrail`/`generateReport`/etc. stay their
 * own owning stories' domains (see `web/features/reporting/ports.ts`'s own header) - this is a
 * new, dedicated key, following the same multi-port precedent BIO-RPT-002/BIO-RPT-003
 * established for this exact module.
 */
export type OperationalHealthPort = ModulePort;

/**
 * Three distinct states (Seyi round-1, BIO-RPT-004-F02 - "unavailable data is not stale data"):
 * `live` - a real source read just succeeded. `stale` - a real source exists but its most recent
 * read FAILED (an offline/degraded signal, never hard-coded away). `unavailable` - no source
 * exists for this domain at all (Card/Device/Sync), a permanent, structural fact, never
 * relabelled `stale`.
 */
export type PanelFreshness = 'live' | 'stale' | 'unavailable';

/**
 * Counts are nullable (Seyi round-2, BIO-RPT-004-F04 - "do not manufacture zero metrics on
 * failure"): `null` only when `freshness: 'stale'` AND no successful read has EVER happened for
 * this tenant yet. A `'stale'` panel that HAS succeeded before carries its retained last-known
 * counts (non-null) alongside the TRUE time they were observed in `observedAt` - never the
 * failed attempt's own time.
 */
export interface IdentityHealthPanel {
  total: number | null;
  active: number | null;
  suspended: number | null;
  other: number | null;
  freshness: PanelFreshness;
  observedAt: string;
  reason?: string;
}

export interface AccessHealthPanel {
  total: number | null;
  allowed: number | null;
  denied: number | null;
  freshness: PanelFreshness;
  observedAt: string;
  reason?: string;
}

export interface ProductionHealthPanel {
  total: number | null;
  passed: number | null;
  failed: number | null;
  rejected: number | null;
  inProgress: number | null;
  cancelled: number | null;
  freshness: PanelFreshness;
  observedAt: string;
  reason?: string;
}

/** Card/Device/Sync (BIO-RPT-004-AC03/AC05): no tenant-wide read exists yet for these three domains - see docs/features/bio-rpt-004/README.md "Which sources are wired, and why". Always `freshness: 'unavailable'`, never `'stale'` and never a fabricated number. */
export interface UnavailableHealthPanel {
  freshness: PanelFreshness;
  reason: string;
  observedAt: string;
}

export interface OperationalHealthSnapshot {
  tenantId: string;
  computedAt: string;
  identity: IdentityHealthPanel;
  card: UnavailableHealthPanel;
  device: UnavailableHealthPanel;
  sync: UnavailableHealthPanel;
  access: AccessHealthPanel;
  production: ProductionHealthPanel;
}

export interface OperationalHealthResult extends PrimaryActionResult {
  snapshot?: OperationalHealthSnapshot;
}

export interface GetOperationalHealthPort extends ModulePort {
  primaryAction(): Promise<OperationalHealthResult>;
}
