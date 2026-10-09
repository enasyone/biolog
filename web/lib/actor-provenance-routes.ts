/**
 * Route-prefix -> per-feature `ActorProvenanceSecret` configuration-key mapping
 * (TD-WEB-PROVENANCE-BRIDGE, Emeka's half).
 *
 * Framework-agnostic (no Next.js/React import) so `web/proxy.ts` (server-only, Edge runtime)
 * and this module's own unit tests share one implementation with no bundle-weight or runtime
 * coupling - same reasoning as `demo-identity.ts`.
 *
 * `route-manifest.ts`'s own `ServicePort` categorisation (BIO-UX-003) is NOT reused here,
 * confirmed by direct inspection before writing this table: it maps a web PAGE route
 * (e.g. `/administration/access`) to a coarse backend-service label, and several entirely
 * distinct `ActorProvenanceSecret`-bearing features share one `ServicePort` (`AccessPolicy`,
 * `AccessDecisions` and `AccessCache` all fall under `AccessService`, for instance) - too
 * coarse to pick the one correct secret a specific gated API route needs. This table instead
 * matches the real `/api/v1/*` BACKEND path every gated endpoint actually maps to, read
 * directly from each feature's own `Api/*Endpoints.cs`/`BioInt003Module.cs`.
 *
 * **Sixteen** distinct `*ActorProvenanceSecret` configuration keys exist as of
 * TD-WEB-PROVENANCE-BRIDGE-REVERIFY/BIO-TEN-003 (confirmed via `grep -rn "ConfigurationKey = "
 * api/features` across the full repo, not assumed from the original task dispatch's stale "12"
 * count - BIO-RPT-004, BIO-BIO-009, BIO-INT-003 and BIO-TEN-003 all landed after that count was
 * made). **BIO-TEN-003's own `TenantFeatureConfig:ActorProvenanceSecret` was missing from this
 * table entirely until TD-WEB-PROVENANCE-BRIDGE-REVERIFY's own BIO-TEN-003 reverify found it**
 * (Tunde, 2026-10-07) - a genuine gap, not a stale count: `/api/v1/tenants/{tenantId}/
 * feature-configuration` (round-2 remediation, `ActorProvenanceGate.Require` on both routes)
 * had no table entry at all, so `computeActorProvenance` silently returned `null` for it and no
 * provenance proof was ever injected, independent of session validity - the same class of
 * second, independent wiring gap BIO-TEN-003's own round-2 remediation already found once for
 * `next.config.mjs`'s rewrite. Added below; confirmed non-colliding with BIO-TEN-001's
 * `/api/v1/control-plane/tenants` or BIO-TEN-002's `/api/v1/tenant-domains` (different prefixes,
 * neither `ActorProvenanceGate`-gated at all). `Authentication:SessionTokenSecret`
 * (BIO-INT-001) and `ApiSecurity:AccessTokenSecret`/`ApiSecurity:SecretHashPepper`
 * (BIO-INT-003's own OAuth2 bearer-token mechanism) are DELIBERATELY excluded - different
 * mechanisms (session establishment; service-account bearer tokens), not the
 * HMAC-over-actor-id `ActorProvenanceGate` pattern this bridge serves.
 *
 * **Non-blocking advisory, fixed (Seyi, re-review of the reverify fix):** the first version of
 * the `TenantFeatureConfig` entry below used a bare `prefix('/api/v1/tenants')`, which also
 * matched `/api/v1/tenants/primary-action` - BIO-UX-005-AC04's own pre-existing, ungated generic
 * simulated-action endpoint (`web/features/tenants/live.ts`'s own `base`), unrelated to
 * feature-configuration entirely. Confirmed harmless (that endpoint has no `ActorProvenanceGate`
 * at all, so it simply ignores the extra, unneeded headers - the same fail-safe "an ungated
 * route given an unused proof is never a false pass" shape this whole table already relies on
 * elsewhere), but imprecise - tightened to a `matches()` regex scoped exactly to the real gated
 * route shape, matching this table's own established precision discipline for every other
 * `{id}`-shaped path (e.g. `/api/v1/visitors/{id}/credential`).
 *
 * **Genuine route-prefix complexity, not a one-liner (Kemi's own framing, confirmed true) -
 * but narrower than first catalogued.** Several routes sit under a path prefix owned by one
 * feature while a SIBLING feature's own doc comments disclose "taking over" that same path -
 * each such case was checked against the actual handler method body (does it take an
 * `ActorProvenanceGate` parameter and call `.Require()`?), not just the doc-comment framing,
 * after Damilola's review (2026-10-07) caught that an earlier version of this table had
 * mapped three UNGATED demo-bridge handoffs as if they were real provenance-gated ones - a
 * route-OWNERSHIP collision (which feature's demo bridge answers this URL) is not the same
 * claim as a route-PROVENANCE-GATING collision (which feature's secret verifies a proof on
 * this URL), and conflating the two produced three wrong entries that happened to be harmless
 * (fail-safe: an ungated route given an unneeded, unused proof is never a false pass) but were
 * not the accurate source of truth this table exists to be:
 *   - `/api/v1/visitors/{id}/credential` -> VisitorCredentials (`GrantAsync`, confirmed
 *     `.Require()`'d) - the ONLY real gated handoff.
 *   - `/api/v1/visitors/{id}/check-out` (`CheckOutAsync`), `/api/v1/attendance/primary-action`
 *     and `/api/v1/attendance/flag-exception` (`PrimaryActionAsync`/`FlagExceptionAsync`,
 *     bio-att-002), `/api/v1/access/primary-action` (`PrimaryActionAsync`, bio-acc-002) and
 *     `/api/v1/access/view-access-events` (`ViewAccessEventsAsync`, bio-acc-003) are all PLAIN
 *     DEMO-BRIDGE handlers with no `ActorProvenanceGate` parameter at all - removed from this
 *     table entirely, correctly falling through to either the surrounding prefix's own
 *     feature (visitors, attendance - harmless either way, nothing there is gated) or to no
 *     match at all (`/api/v1/access/*` has no bare-prefix entry, so both resolve to `null`,
 *     exactly right since neither is gated).
 *
 * `/api/v1/persons/{personId}/*` is NOT given a blanket entry: most of that path space
 * (profiles, contact-point, lifecycle, duplicate-candidates, card-issues - bio-idn-001
 * through bio-idn-005, bio-crd-005/006/007) belongs to features with NO
 * `ActorProvenanceGate` at all (confirmed by the same repo-wide grep above finding no
 * matching `ConfigurationKey` for any of them) - only the `roles`/`permissions` sub-paths are
 * BIO-IDN-006's own Rbac-gated surface, so only those are matched, not the bare prefix.
 */

export interface ActorProvenanceRouteEntry {
  /** Human-readable label only (not matched against); the owning feature for documentation/debugging. */
  feature: string;
  /** The exact `IConfiguration` key the OWNING .NET feature reads, e.g. `"AccessPolicy:ActorProvenanceSecret"`. */
  configurationKey: string;
  test: (pathname: string) => boolean;
}

function prefix(path: string): (pathname: string) => boolean {
  return (pathname) => pathname === path || pathname.startsWith(`${path}/`);
}

function prefixExcluding(path: string, excluded: readonly string[]): (pathname: string) => boolean {
  const base = prefix(path);
  return (pathname) => base(pathname) && !excluded.includes(pathname);
}

function matches(regex: RegExp): (pathname: string) => boolean {
  return (pathname) => regex.test(pathname);
}

/**
 * Checked IN ORDER, first match wins. Handoff/collision entries are listed before the
 * broader prefix they carve out of, so a more specific route is never shadowed by its own
 * feature's own general prefix entry appearing first.
 */
export const ACTOR_PROVENANCE_ROUTES: readonly ActorProvenanceRouteEntry[] = Object.freeze([
  // --- Visitors / VisitorCredentials (BIO-VIS-001 / BIO-VIS-002) ---
  // Only `/credential` (GrantAsync) is actually ActorProvenanceGate-gated. `check-out`
  // (CheckOutAsync) has no provenance gate at all - correctly falls through to the plain
  // Visitors prefix entry below (harmless: nothing there checks the proof either).
  { feature: 'VisitorCredentials', configurationKey: 'VisitorCredentials:ActorProvenanceSecret', test: matches(/^\/api\/v1\/visitors\/[^/]+\/credential$/) },
  { feature: 'VisitorCredentials', configurationKey: 'VisitorCredentials:ActorProvenanceSecret', test: prefix('/api/v1/visitor-credentials') },
  { feature: 'Visitors', configurationKey: 'Visitors:ActorProvenanceSecret', test: prefix('/api/v1/visitors') },

  // --- Attendance / AttendanceSessions (BIO-ATT-001 / BIO-ATT-002) ---
  // `primary-action`/`flag-exception` (bio-att-002's own bridge takeover) are plain demo
  // bridges with no ActorProvenanceGate - no entry needed; they fall through to the
  // Attendance prefix below (harmless, nothing there checks the proof either).
  { feature: 'AttendanceSessions', configurationKey: 'AttendanceSessions:ActorProvenanceSecret', test: prefix('/api/v1/attendance-sessions') },
  { feature: 'Attendance', configurationKey: 'Attendance:ActorProvenanceSecret', test: prefix('/api/v1/attendance') },

  // --- AccessDecisions / AccessCache (two sibling features sharing one bridge prefix) ---
  // `/api/v1/access/primary-action` and `/api/v1/access/view-access-events` are BOTH plain
  // demo bridges with no ActorProvenanceGate - no entry needed; `/api/v1/access` has no
  // bare-prefix entry of its own, so both correctly resolve to no match (null).
  { feature: 'AccessDecisions', configurationKey: 'AccessDecisions:ActorProvenanceSecret', test: prefix('/api/v1/access-decisions') },
  { feature: 'AccessCache', configurationKey: 'AccessCache:ActorProvenanceSecret', test: prefix('/api/v1/access-cache') },
  { feature: 'AccessPolicy', configurationKey: 'AccessPolicy:ActorProvenanceSecret', test: prefix('/api/v1/access-policies') },

  // --- Rbac (BIO-IDN-006) - only its own sub-paths under the widely shared /api/v1/persons/*, /api/v1/roles* ---
  { feature: 'Rbac', configurationKey: 'Rbac:ActorProvenanceSecret', test: matches(/^\/api\/v1\/roles\/[^/]+\/permissions\//) },
  { feature: 'Rbac', configurationKey: 'Rbac:ActorProvenanceSecret', test: matches(/^\/api\/v1\/persons\/[^/]+\/roles(\/|$)/) },
  { feature: 'Rbac', configurationKey: 'Rbac:ActorProvenanceSecret', test: prefix('/api/v1/sensitive-actions') },

  // --- Everything else: one feature, one clean, non-overlapping prefix ---
  // `/whoami` is bearer-token authorized (BIO-INT-003's own OAuth2 AuthorizeAsync), never
  // ActorProvenanceGate-gated - excluded explicitly (Damilola's review, 2026-10-07) rather
  // than left to match-and-be-harmless, so this table doesn't imply it's gated when it isn't.
  { feature: 'ApiSecurity', configurationKey: 'ApiSecurity:ActorProvenanceSecret', test: prefixExcluding('/api/v1/integrations/service-accounts', ['/api/v1/integrations/service-accounts/whoami']) },
  { feature: 'Integrations', configurationKey: 'Integrations:ActorProvenanceSecret', test: prefix('/api/v1/integrations/mappings') },
  { feature: 'MobileVerification', configurationKey: 'MobileVerification:ActorProvenanceSecret', test: prefix('/api/v1/mobile-verification') },
  { feature: 'OfflineEnrolment', configurationKey: 'OfflineEnrolment:ActorProvenanceSecret', test: prefix('/api/v1/enrolment') },
  { feature: 'OperationalDashboard', configurationKey: 'OperationalDashboard:ActorProvenanceSecret', test: prefix('/api/v1/dashboard') },
  { feature: 'Transport', configurationKey: 'Transport:ActorProvenanceSecret', test: prefix('/api/v1/transport') },
  { feature: 'BiometricRetention', configurationKey: 'BiometricRetention:ActorProvenanceSecret', test: prefix('/api/v1/biometrics/retention') },
  // `/api/v1/tenants/{tenantId}/feature-configuration` (BIO-TEN-003) - distinct from, and never
  // matched by, BIO-TEN-001's `/api/v1/control-plane/tenants` or BIO-TEN-002's
  // `/api/v1/tenant-domains` prefixes (neither is ActorProvenanceGate-gated at all). Scoped to
  // exactly this shape (not a bare `prefix('/api/v1/tenants')`) so it never also matches
  // `/api/v1/tenants/primary-action` - BIO-UX-005-AC04's own pre-existing, ungated generic
  // simulated-action endpoint, confirmed harmless either way but imprecise (Seyi, advisory).
  { feature: 'TenantFeatureConfig', configurationKey: 'TenantFeatureConfig:ActorProvenanceSecret', test: matches(/^\/api\/v1\/tenants\/[^/]+\/feature-configuration$/) },
  // `/api/v1/workforce-attendance/*` (BIO-ATT-003) - a genuine gap found the same way
  // BIO-TEN-003's own TD-WEB-PROVENANCE-BRIDGE-REVERIFY round found its own missing entry:
  // this table was never updated for this story. Distinct from, and never matched by,
  // BIO-ATT-001's own `/api/v1/attendance` prefix above.
  { feature: 'WorkforceAttendance', configurationKey: 'WorkforceAttendance:ActorProvenanceSecret', test: prefix('/api/v1/workforce-attendance') },
  // `/api/v1/photo-quality/*` (BIO-PHO-003, ZB-PHO003-01 round 2) - round 1 shipped with no
  // provenance/RBAC gate at all and this table entry was correctly never added for it; added
  // now alongside the gate itself, the same "this table was never updated for this story" gap
  // shape BIO-ATT-003's own WorkforceAttendance entry above was found and fixed the same way.
  { feature: 'PhotoQuality', configurationKey: 'PhotoQuality:ActorProvenanceSecret', test: prefix('/api/v1/photo-quality') },

  // `/api/v1/devices/*` (BIO-DEV-004, SY-DEV004-01 round 1, Critical) - same "round 1 shipped
  // ungated, this table entry was correctly never added for it" shape as PhotoQuality above.
  // This prefix is SHARED with three sibling stories that remain deliberately ungated today
  // (BIO-DEV-001's own `POST /` register / `GET /{deviceId}`, BIO-DEV-003's own
  // `GET /{deviceId}/capabilities` / `GET /{deviceId}/health`) - matched precisely by exact
  // shape, NOT a bare `prefix('/api/v1/devices')`, so this table never implies those three are
  // gated when they are not (the same precision discipline this table's own header already
  // established for `/api/v1/tenants/*`). Only BIO-DEV-004's own four routes match:
  // `GET /` (list), `POST /{deviceId}/poll-health`, `GET /{deviceId}/remote-job-capabilities`,
  // `POST /{deviceId}/remote-jobs`.
  { feature: 'DeviceOperations', configurationKey: 'DeviceOperations:ActorProvenanceSecret', test: matches(/^\/api\/v1\/devices$/) },
  { feature: 'DeviceOperations', configurationKey: 'DeviceOperations:ActorProvenanceSecret', test: matches(/^\/api\/v1\/devices\/[^/]+\/(poll-health|remote-job-capabilities|remote-jobs)$/) },
]);

/** Returns the owning feature's `IConfiguration` key for a gated pathname, or `null` if none matches (not every `/api/v1/*` route is provenance-gated - e.g. OAuth2 bearer-token routes, read-only routes under ADR-0006 Decision 2). */
export function resolveActorProvenanceConfigurationKey(pathname: string): string | null {
  for (const entry of ACTOR_PROVENANCE_ROUTES) {
    if (entry.test(pathname)) {
      return entry.configurationKey;
    }
  }
  return null;
}

/**
 * ASP.NET Core's own environment-variable configuration provider convention: a hierarchical
 * key `"Section:Key"` is read from an env var literally named `Section__Key` (double
 * underscore in place of `:`, since most shells/platforms don't allow `:` in an env var
 * name) - not a convention invented here. A deployment that colocates the web server and the
 * API can therefore provision ONE set of fifteen env vars and have both processes read the
 * identical value; a deployment that does not MUST provision its own copy of each secret for
 * the web server's own environment (see this story's own README "Secret distribution
 * tradeoff" for the isolation consequence Kemi asked to be documented, not resolved here).
 */
export function actorProvenanceEnvVarName(configurationKey: string): string {
  return configurationKey.replace(/:/g, '__');
}

/** Mirrors `HmacActorProvenanceStore.MinimumSecretLength` (TD-PROVENANCE-DUMMYKEY-SWEEP) - a configured secret shorter than this is never used as a real HMAC key. */
export const MINIMUM_ACTOR_PROVENANCE_SECRET_LENGTH = 32;

function toHex(buffer: ArrayBuffer): string {
  return Array.from(new Uint8Array(buffer))
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
}

/**
 * `hex(HMACSHA256(secret, UTF8(message)))` via Web Crypto - identical on the Edge and Node.js
 * runtimes, no Node `crypto` module import needed. Must match `HmacActorProvenanceStore.Verify()`
 * exactly: UTF-8 secret bytes as the key, UTF-8 message bytes as the signed data, no tenant/
 * timestamp/path mixed in. Hex case is irrelevant - `Convert.FromHexString` on the .NET side is
 * case-insensitive - but lower-case is produced here regardless, for a stable, diffable value.
 *
 * Exported standalone (not inlined into `proxy.ts`) so it is unit-testable against a known
 * golden vector without importing `next/server` into the test.
 */
export async function computeHmacHex(secret: string, message: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(secret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const signature = await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(message));
  return toHex(signature);
}
