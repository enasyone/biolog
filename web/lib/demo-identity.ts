/**
 * Shared, framework-agnostic demo-identity constants (TD-WEB-PROVENANCE-BRIDGE).
 *
 * Used by both `context/session-context.tsx` ('use client', the shell's role picker) and
 * `middleware.ts` (server-only, runs on the Edge runtime) — kept in its own file with no
 * React/Next import so middleware's bundle never pulls in client-component code across
 * that boundary.
 *
 * `DEMO_ROLE_IDS` duplicates `session-context.tsx`'s own `ROLES`/`DEMO_ROLE_IDS` ids
 * (not re-exported from there, for the cross-boundary reason above) — kept in sync by
 * `td-web-provenance-bridge-role-sync.test.ts`, which fails loudly if the two ever drift,
 * rather than relying on manual upkeep across two files.
 */

/** No longer read by `proxy.ts` (ZB-WEBPROV-01) - kept only as the historical cookie name a forged-cookie negative test must still prove rejected. The real session lives in `SESSION_TOKEN_COOKIE` (`./session-token.ts`). */
export const DEMO_ROLE_COOKIE = 'biolog-demo-role';

export const DEMO_ROLE_IDS = ['admin', 'staff', 'viewer'] as const;
export type DemoRoleId = (typeof DEMO_ROLE_IDS)[number];

/** Matches `session-context.tsx`'s own `DEFAULT_ROLE_ID`. */
export const DEFAULT_DEMO_ROLE_ID: DemoRoleId = 'staff';

export function isDemoRoleId(value: string | null | undefined): value is DemoRoleId {
  return !!value && (DEMO_ROLE_IDS as readonly string[]).includes(value);
}

/** Duplicates `session-context.tsx`'s own private `TENANTS` ids, same cross-boundary reason and the same drift guard as `DEMO_ROLE_IDS` above. */
export const DEMO_TENANT_IDS = ['alpha', 'beta'] as const;
export type DemoTenantId = (typeof DEMO_TENANT_IDS)[number];

/** Matches `session-context.tsx`'s own `DEFAULT_TENANT_ID`. */
export const DEFAULT_DEMO_TENANT_ID: DemoTenantId = 'alpha';

export function isDemoTenantId(value: string | null | undefined): value is DemoTenantId {
  return !!value && (DEMO_TENANT_IDS as readonly string[]).includes(value);
}

/**
 * Stable, opaque, role-only actor id (TD-WEB-PROVENANCE-BRIDGE joint sign-off with Emeka,
 * 2026-10-06: role-only, not tenant+role — tenant scoping already happens inside
 * `PermissionService.ActorHasPermissionAsync`'s own `tenantId` parameter, not the actor id
 * string, so doubling the id space per-tenant would buy nothing provenance-wise). This is
 * deliberately not the free-text display name (`session-context.tsx`'s `DEMO_USER_BY_ROLE`,
 * e.g. "Amara Adeleke (SYNTHETIC)") — it needs to stay a clean, stable, machine-usable key,
 * since it is the literal UTF-8 input to the HMAC proof (`ActorProvenanceGate`'s
 * `HMACSHA256(secret, UTF8(actorId))`, see each feature's own `Api/ActorProvenanceGate.cs`
 * under `api/features/`).
 *
 * Round 6 (Kemi's ruling, 2026-10-07, folded into the ADR-0002 amendment this TD already
 * owes): the earlier `demo-actor-{roleId}` value is not a well-formed canonical BIOLOG
 * identifier (ADR-0001 Decision 1) — `CanonicalId.IsPersonId` rejects it on format before any
 * RBAC grant lookup ever runs, so no story could ever get a real "granted" result through this
 * bridge. Fixed, hardcoded `per_<UUIDv7>` literals instead — one per demo role, matching the
 * three synthetic Person rows + PersonRoleAssignment rows seeded server-side (see
 * `api/Biolog.Api/Tenancy/DemoSessionIdentitySeed.cs`), confined to the one hardcoded
 * `WebSessionBridge:SyntheticTenantId` round 5 already established. `computeActorProvenance`
 * is unchanged — it signs whatever this function returns, same as before.
 */
const DEMO_PERSON_ID_BY_ROLE: Readonly<Record<DemoRoleId, string>> = Object.freeze({
  admin: 'per_01a113f6-6268-730c-a5ce-44d97f0aa7f5',
  staff: 'per_01a113f6-6268-7c44-a9e5-3aee6ff59416',
  viewer: 'per_01a113f6-6268-757e-926c-4a1b5f98b45b',
});

export function deriveDemoActorId(roleId: DemoRoleId): string {
  return DEMO_PERSON_ID_BY_ROLE[roleId];
}
