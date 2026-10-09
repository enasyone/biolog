'use client';

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { endDemoSession, startDemoSession } from './session-actions';
import type { Permission } from '../lib/permissions';

/**
 * Tenant/role session context (BIO-FND-007, extended BIO-UX-002-AC01 for an
 * interactive tenant selector and role selector, not just a display).
 *
 * This is a placeholder session, not authentication or RBAC (BIO-FND-007-AC04,
 * still true). A later identity/tenant story replaces the body of
 * `SessionProvider` with a real resolved session; every consumer below only
 * reads the `Session` shape, so that swap does not touch the shell.
 *
 * TD-WEB-PROVENANCE-BRIDGE, round 1 (reverted 2026-10-07, ZB-WEBPROV-01, critical): this
 * provider briefly mirrored the selected role into a plain, client-written `biolog-demo-role`
 * cookie for `web/proxy.ts` to read. Zainab's required security review failed it — any caller
 * could set that cookie directly and have the proxy manufacture a valid, signed
 * `X-Actor-Provenance` proof for a privileged demo role, the exact caller-supplied-value
 * problem ADR-0006 Decision 3's trusted edge exists to prevent.
 *
 * TD-WEB-PROVENANCE-BRIDGE, round 2 (Kemi's scope ruling): real browser authentication stays
 * out of this TD's scope (its own future story) — what's in scope is making the EXISTING demo
 * mechanism tamper-evident and revocable instead of inventing real sign-in. `setTenantId`/
 * `setRoleId`/`signOut`/`signBackIn` below now call `./session-actions.ts`'s Server Actions,
 * which mint/revoke a signed, `HttpOnly` session token (`web/lib/session-token.ts`) — never a
 * client-writable cookie again. This is still exactly the placeholder session the paragraph
 * above describes (no real identity, synthetic tenants/roles only); the token proves "this
 * selection passed through our own server," not "this is a specific, authenticated person."
 */
export interface Tenant {
  id: string;
  name: string;
}

export interface Role {
  id: string;
  name: string;
  permissions: ReadonlySet<Permission>;
}

/** Feature-flag keys a nav item can be gated behind (BIO-UX-011-AC04) — see `nav-items.ts`. */
export type FeatureFlagKey = 'cardDesigner' | 'documentVerify' | 'mobileCredentials';

export interface Session {
  tenant: Tenant;
  role: Role;
  userName: string;
  permissions: ReadonlySet<Permission>;
  /** Every selectable tenant, for a tenant-selector control. */
  tenants: readonly Tenant[];
  /** Every selectable role, for a role-selector control. */
  roles: readonly Role[];
  setTenantId: (id: string) => void;
  setRoleId: (id: string) => void;
  /** True after the simulated sign-out action (BIO-UX-002-AC01 user menu). */
  signedOut: boolean;
  signOut: () => void;
  signBackIn: () => void;
  /**
   * Which optional modules are enabled for this tenant (BIO-UX-011-AC04:
   * "feature changes visibly alter relevant navigation and controls").
   * Read by `app-shell.tsx`'s nav filtering (`nav-items.ts`'s
   * `requiredFeatureFlag`) and written by Settings' Feature Flags section —
   * a toggle there applies immediately, the same way the tenant/role
   * selectors apply immediately, not gated behind a separate save step.
   */
  featureFlags: Readonly<Record<FeatureFlagKey, boolean>>;
  setFeatureFlag: (key: FeatureFlagKey, enabled: boolean) => void;
}

export const DEFAULT_FEATURE_FLAGS: Readonly<Record<FeatureFlagKey, boolean>> = Object.freeze({
  cardDesigner: true,
  documentVerify: true,
  mobileCredentials: false,
});

/** SYNTHETIC demo tenants, selectable via the shell's tenant selector or `?tenant=`. */
const TENANTS: readonly Tenant[] = Object.freeze([
  Object.freeze({ id: 'alpha', name: 'Alpha State University (SYNTHETIC)' }),
  Object.freeze({ id: 'beta', name: 'Beta Polytechnic (SYNTHETIC)' }),
]);

/** SYNTHETIC demo roles, selectable via the shell's role selector or `?as=`. */
const ROLES: readonly Role[] = Object.freeze([
  Object.freeze({
    id: 'admin',
    name: 'System Administrator',
    permissions: Object.freeze(
      new Set<Permission>([
        'nav:people',
        'nav:students',
        'nav:staff',
        'nav:visitors',
        'nav:alumni',
        'nav:contractors',
        'nav:enrolment',
        'nav:photographs',
        'nav:fingerprints',
        'nav:facial-recognition',
        'nav:duplicate-review',
        'nav:cards',
        'nav:credentials',
        'nav:card-designer',
        'nav:mobile-credentials',
        'nav:document-verify',
        'nav:access-control',
        'nav:attendance',
        'nav:transport',
        'nav:devices',
        'nav:reports',
        'nav:audit',
        'nav:administration',
        'nav:settings',
      ]),
    ),
  }),
  Object.freeze({
    id: 'staff',
    name: 'Registration Staff',
    permissions: Object.freeze(
      new Set<Permission>([
        'nav:people',
        'nav:students',
        'nav:staff',
        'nav:visitors',
        'nav:enrolment',
        'nav:photographs',
        'nav:fingerprints',
        'nav:facial-recognition',
        'nav:duplicate-review',
        'nav:cards',
        'nav:credentials',
        'nav:card-designer',
        'nav:mobile-credentials',
        'nav:document-verify',
        'nav:reports',
      ]),
    ),
  }),
  Object.freeze({
    id: 'viewer',
    name: 'Report Viewer',
    permissions: Object.freeze(new Set<Permission>(['nav:reports', 'nav:audit'])),
  }),
]);

/** SYNTHETIC demo user per role, kept from BIO-FND-007's original three profiles. */
const DEMO_USER_BY_ROLE: Readonly<Record<string, string>> = Object.freeze({
  admin: 'Amara Adeleke (SYNTHETIC)',
  staff: 'Kelechi Okafor (SYNTHETIC)',
  viewer: 'Ovie Eferakeya (SYNTHETIC)',
});

export const DEFAULT_TENANT_ID = 'alpha';
export const DEFAULT_ROLE_ID = 'staff';

/** Exported for tests/back-compat with the pre-BIO-UX-002 `?as=` demo-profile switcher. */
export const DEMO_ROLE_IDS: readonly string[] = ROLES.map((role) => role.id);

/** Exported for `td-web-provenance-bridge-session-token.test.ts`'s own drift guard against `web/lib/demo-identity.ts`'s `DEMO_TENANT_IDS`. */
export const DEMO_TENANT_IDS: readonly string[] = TENANTS.map((tenant) => tenant.id);

const SessionContext = createContext<Session | null>(null);

function findTenant(id: string): Tenant {
  return TENANTS.find((tenant) => tenant.id === id) ?? TENANTS.find((t) => t.id === DEFAULT_TENANT_ID)!;
}

function findRole(id: string): Role {
  return ROLES.find((role) => role.id === id) ?? ROLES.find((r) => r.id === DEFAULT_ROLE_ID)!;
}

/**
 * Renders with the default tenant/role on both server and first client paint
 * (so hydration matches and the shell is present in the server-rendered
 * HTML), then applies `?tenant=`/`?as=` overrides, if present, after mount.
 * This is a deliberately client-only read: using `next/navigation`'s
 * `useSearchParams` here would opt this whole subtree out of server rendering.
 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const [tenantId, setTenantIdState] = useState(DEFAULT_TENANT_ID);
  const [roleId, setRoleIdState] = useState(DEFAULT_ROLE_ID);
  const [signedOut, setSignedOut] = useState(false);
  const [featureFlags, setFeatureFlags] = useState(DEFAULT_FEATURE_FLAGS);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const requestedTenant = params.get('tenant');
    const requestedRole = params.get('as');
    const resolvedTenantId =
      requestedTenant && TENANTS.some((t) => t.id === requestedTenant) ? requestedTenant : DEFAULT_TENANT_ID;
    const resolvedRoleId = requestedRole && ROLES.some((r) => r.id === requestedRole) ? requestedRole : DEFAULT_ROLE_ID;
    setTenantIdState(resolvedTenantId);
    setRoleIdState(resolvedRoleId);
    // Mint the initial session token once, after resolving BOTH overrides, not twice (once per
    // field) - the token binds tenant+role together, so an intermediate single-field token
    // would just be immediately superseded.
    void startDemoSession(resolvedTenantId, resolvedRoleId);
  }, []);

  // Wrapped setters: update local state for immediate UI feedback AND re-issue the session
  // token server-side, since it binds both fields together - changing just one still needs a
  // fresh token carrying the other's current value too (TD-WEB-PROVENANCE-BRIDGE round 2).
  function setTenantId(id: string) {
    setTenantIdState(id);
    void startDemoSession(id, roleId);
  }

  function setRoleId(id: string) {
    setRoleIdState(id);
    void startDemoSession(tenantId, id);
  }

  const session = useMemo<Session>(() => {
    const tenant = findTenant(tenantId);
    const role = findRole(roleId);
    return {
      tenant,
      role,
      userName: DEMO_USER_BY_ROLE[role.id] ?? DEMO_USER_BY_ROLE[DEFAULT_ROLE_ID]!,
      permissions: role.permissions,
      tenants: TENANTS,
      roles: ROLES,
      setTenantId,
      setRoleId,
      signedOut,
      // Genuinely revokes the session server-side (session-actions.ts's endDemoSession) before
      // flipping the UI state - not just the old setSignedOut(true), which left the cookie (and
      // whatever it authorized) fully valid. Fire-and-forget from the UI's perspective (the
      // exposed Session.signOut stays synchronous, no caller needs to await it), but the
      // revocation itself is a real, immediate server round-trip.
      signOut: () => {
        setSignedOut(true);
        void endDemoSession();
      },
      // Re-establishes a real session on the current (tenant, role) pair - signing back in
      // without this would leave the user "signed in" in the UI with no valid cookie underneath.
      signBackIn: () => {
        setSignedOut(false);
        void startDemoSession(tenantId, roleId);
      },
      featureFlags,
      setFeatureFlag: (key, enabled) =>
        setFeatureFlags((current) => ({ ...current, [key]: enabled })),
    };
  }, [tenantId, roleId, signedOut, featureFlags, setTenantId, setRoleId]);

  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>;
}

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) {
    throw new Error('useSession must be used within a SessionProvider');
  }
  return session;
}
