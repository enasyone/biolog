import type {
  AccessHealthPanel,
  GetOperationalHealthPort,
  IdentityHealthPanel,
  OperationalHealthResult,
  OperationalHealthSnapshot,
  PanelFreshness,
  ProductionHealthPanel,
  UnavailableHealthPanel,
} from './ports';

/**
 * Hand-written `ModulePort`, not `createLiveAdapter` (this is a `GET` with no body) - the backend
 * returns an explicit snake_case wire contract (`api/features/bio-rpt-004/Api/DashboardDtos.cs`),
 * mapped to camelCase here field by field rather than trusting a blind re-serialization - the
 * same discipline every other hand-written live port in this epic applies from the start
 * (BIO-CRD-008's own `transitionAction` drift is the prior bug class this avoids).
 */
const BASE_PATH = '/api/v1/dashboard';

/**
 * No authentication story has landed in the frontend yet (BIO-INT-001 is W12) - the same
 * placeholder actor every other live port in this epic sends. This endpoint now runs BOTH a
 * cryptographic provenance check (BIO-RPT-004-F03, ADR-0006 Decision 3) and a real permission
 * check against it (BIO-RPT-004-F01) - a demo session has no way to produce a valid
 * `X-Actor-Provenance` proof (the shared secret is out-of-band only, never shipped to the
 * browser - verified directly: no live port anywhere in this codebase sends one), so it
 * genuinely, honestly gets 401 `actor_provenance_required` until a real auth edge is wired
 * through, the same disclosed consequence BIO-RPT-003's own LIVE export proof already
 * demonstrates for its own permission gate (see
 * tests/frontend/playwright/bio-rpt-004-operational-health-live.spec.ts and
 * docs/features/bio-rpt-004/README.md "Proving AC05").
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui' } as const;

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { message?: string };
    return problem.message ?? fallback;
  } catch {
    return fallback;
  }
}

/** `null`/`undefined` stays `null` (BIO-RPT-004-F04: a never-observed count is genuinely absent, never coerced to 0/NaN) - never a blind `Number(...)`. */
function mapNullableNumber(value: unknown): number | null {
  return value === null || value === undefined ? null : Number(value);
}

function mapIdentity(raw: Record<string, unknown>): IdentityHealthPanel {
  return {
    total: mapNullableNumber(raw.total),
    active: mapNullableNumber(raw.active),
    suspended: mapNullableNumber(raw.suspended),
    other: mapNullableNumber(raw.other),
    freshness: raw.freshness as PanelFreshness,
    observedAt: String(raw.observed_at),
    reason: raw.reason !== undefined ? String(raw.reason) : undefined,
  };
}

function mapAccess(raw: Record<string, unknown>): AccessHealthPanel {
  return {
    total: mapNullableNumber(raw.total),
    allowed: mapNullableNumber(raw.allowed),
    denied: mapNullableNumber(raw.denied),
    freshness: raw.freshness as PanelFreshness,
    observedAt: String(raw.observed_at),
    reason: raw.reason !== undefined ? String(raw.reason) : undefined,
  };
}

function mapProduction(raw: Record<string, unknown>): ProductionHealthPanel {
  return {
    total: mapNullableNumber(raw.total),
    passed: mapNullableNumber(raw.passed),
    failed: mapNullableNumber(raw.failed),
    rejected: mapNullableNumber(raw.rejected),
    inProgress: mapNullableNumber(raw.in_progress),
    cancelled: mapNullableNumber(raw.cancelled),
    freshness: raw.freshness as PanelFreshness,
    observedAt: String(raw.observed_at),
    reason: raw.reason !== undefined ? String(raw.reason) : undefined,
  };
}

function mapUnavailable(raw: Record<string, unknown>): UnavailableHealthPanel {
  return { freshness: raw.freshness as PanelFreshness, reason: String(raw.reason), observedAt: String(raw.observed_at) };
}

function mapSnapshot(raw: Record<string, unknown>): OperationalHealthSnapshot {
  return {
    tenantId: String(raw.tenant_id),
    computedAt: String(raw.computed_at),
    identity: mapIdentity(raw.identity as Record<string, unknown>),
    card: mapUnavailable(raw.card as Record<string, unknown>),
    device: mapUnavailable(raw.device as Record<string, unknown>),
    sync: mapUnavailable(raw.sync as Record<string, unknown>),
    access: mapAccess(raw.access as Record<string, unknown>),
    production: mapProduction(raw.production as Record<string, unknown>),
  };
}

export const operationalHealthLivePort: GetOperationalHealthPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(): Promise<OperationalHealthResult> {
    try {
      const response = await fetch(`${BASE_PATH}/operational-health`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Operational health request failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as Record<string, unknown>;
      return { outcome: 'success', message: 'Operational health snapshot computed (LIVE).', snapshot: mapSnapshot(body) };
    } catch {
      return { outcome: 'offline', message: `No live operational-health service reachable at ${BASE_PATH}/operational-health yet.` };
    }
  },
};
