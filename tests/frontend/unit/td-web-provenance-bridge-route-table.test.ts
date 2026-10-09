// Developer tests for TD-WEB-PROVENANCE-BRIDGE (Emeka's half).
// SYNTHETIC: no real tenant/user data; the secret below is a literal test fixture, never a
// real deployment value.
import { describe, expect, it } from 'vitest';
import {
  actorProvenanceEnvVarName,
  computeHmacHex,
  MINIMUM_ACTOR_PROVENANCE_SECRET_LENGTH,
  resolveActorProvenanceConfigurationKey,
} from '../../../web/lib/actor-provenance-routes';

describe('computeHmacHex', () => {
  it('matches a known-good HMAC-SHA256 golden vector (cross-checked via openssl dgst -sha256 -hmac)', async () => {
    const secret = 'this-is-a-synthetic-test-secret-32chars-plus';
    const actorId = 'demo-actor-admin';
    const expected = 'c38008775c8618646048334a649e2bf5c70065013cb75983d0006ce2a41aaa60';
    expect(secret.length).toBeGreaterThanOrEqual(MINIMUM_ACTOR_PROVENANCE_SECRET_LENGTH);
    await expect(computeHmacHex(secret, actorId)).resolves.toBe(expected);
  });

  it('produces a different proof for a different actor id under the same secret', async () => {
    const secret = 'this-is-a-synthetic-test-secret-32chars-plus';
    const admin = await computeHmacHex(secret, 'demo-actor-admin');
    const staff = await computeHmacHex(secret, 'demo-actor-staff');
    expect(admin).not.toBe(staff);
  });

  it('produces a different proof for the same actor id under a different secret', async () => {
    const actorId = 'demo-actor-admin';
    const a = await computeHmacHex('this-is-a-synthetic-test-secret-32chars-plus', actorId);
    const b = await computeHmacHex('a-totally-different-synthetic-secret-value-here', actorId);
    expect(a).not.toBe(b);
  });
});

describe('actorProvenanceEnvVarName', () => {
  it('replaces every colon with a double underscore (ASP.NET Core env-var configuration provider convention)', () => {
    expect(actorProvenanceEnvVarName('AccessPolicy:ActorProvenanceSecret')).toBe('AccessPolicy__ActorProvenanceSecret');
    expect(actorProvenanceEnvVarName('VisitorCredentials:ActorProvenanceSecret')).toBe('VisitorCredentials__ActorProvenanceSecret');
  });
});

describe('resolveActorProvenanceConfigurationKey - clean, non-overlapping prefixes', () => {
  const cases: ReadonlyArray<[string, string]> = [
    ['/api/v1/access-policies', 'AccessPolicy:ActorProvenanceSecret'],
    ['/api/v1/access-policies/pol_1/publish', 'AccessPolicy:ActorProvenanceSecret'],
    ['/api/v1/access-decisions/decisions', 'AccessDecisions:ActorProvenanceSecret'],
    ['/api/v1/access-cache/presentation-events', 'AccessCache:ActorProvenanceSecret'],
    ['/api/v1/attendance-sessions/sessions', 'AttendanceSessions:ActorProvenanceSecret'],
    ['/api/v1/attendance/events', 'Attendance:ActorProvenanceSecret'],
    ['/api/v1/attendance/policy', 'Attendance:ActorProvenanceSecret'],
    ['/api/v1/roles/Gate/permissions/integrations.service_account_whoami', 'Rbac:ActorProvenanceSecret'],
    ['/api/v1/persons/per_1/roles/TenantAdmin', 'Rbac:ActorProvenanceSecret'],
    ['/api/v1/persons/per_1/roles/TenantAdmin/revoke', 'Rbac:ActorProvenanceSecret'],
    ['/api/v1/sensitive-actions/some-action/rule', 'Rbac:ActorProvenanceSecret'],
    ['/api/v1/integrations/service-accounts', 'ApiSecurity:ActorProvenanceSecret'],
    ['/api/v1/integrations/service-accounts/svc_1/rotate-secret', 'ApiSecurity:ActorProvenanceSecret'],
    ['/api/v1/integrations/mappings', 'Integrations:ActorProvenanceSecret'],
    ['/api/v1/mobile-verification/decisions', 'MobileVerification:ActorProvenanceSecret'],
    ['/api/v1/enrolment/packages', 'OfflineEnrolment:ActorProvenanceSecret'],
    ['/api/v1/dashboard', 'OperationalDashboard:ActorProvenanceSecret'],
    ['/api/v1/transport/routes', 'Transport:ActorProvenanceSecret'],
    ['/api/v1/biometrics/retention/policy', 'BiometricRetention:ActorProvenanceSecret'],
    ['/api/v1/tenants/ten_1/feature-configuration', 'TenantFeatureConfig:ActorProvenanceSecret'],
    ['/api/v1/workforce-attendance/shifts', 'WorkforceAttendance:ActorProvenanceSecret'],
    ['/api/v1/photo-quality/captures', 'PhotoQuality:ActorProvenanceSecret'],
    ['/api/v1/photo-quality/captures/per_1/1/approve', 'PhotoQuality:ActorProvenanceSecret'],
  ];

  it.each(cases)('%s -> %s', (pathname, expectedKey) => {
    expect(resolveActorProvenanceConfigurationKey(pathname)).toBe(expectedKey);
  });
});

describe('resolveActorProvenanceConfigurationKey - documented route handoffs (real, gated ones only)', () => {
  it('BIO-VIS-002 owns /api/v1/visitors/{id}/credential (GrantAsync, confirmed .Require()\'d), a new route under BIO-VIS-001\'s own prefix', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/visitors/vis_1/credential')).toBe('VisitorCredentials:ActorProvenanceSecret');
  });

  it('every other /api/v1/visitors/* route stays BIO-VIS-001\'s own Visitors secret', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/visitors/vis_1/approve')).toBe('Visitors:ActorProvenanceSecret');
    expect(resolveActorProvenanceConfigurationKey('/api/v1/visitors/vis_1/check-in')).toBe('Visitors:ActorProvenanceSecret');
    expect(resolveActorProvenanceConfigurationKey('/api/v1/visitors/config')).toBe('Visitors:ActorProvenanceSecret');
  });

  it('/api/v1/visitors/{id}/check-out (CheckOutAsync) has no ActorProvenanceGate at all - falls through to the surrounding Visitors prefix, not VisitorCredentials, harmlessly', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/visitors/vis_1/check-out')).toBe('Visitors:ActorProvenanceSecret');
  });

  it('/api/v1/attendance/primary-action and /flag-exception (bio-att-002\'s own demo bridges) have no ActorProvenanceGate at all - fall through to the surrounding Attendance prefix, harmlessly', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/attendance/primary-action')).toBe('Attendance:ActorProvenanceSecret');
    expect(resolveActorProvenanceConfigurationKey('/api/v1/attendance/flag-exception')).toBe('Attendance:ActorProvenanceSecret');
  });

  it('every other /api/v1/attendance/* route stays BIO-ATT-001\'s own Attendance secret', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/attendance/events')).toBe('Attendance:ActorProvenanceSecret');
  });

  it('/api/v1/access/primary-action and /view-access-events (two sibling demo bridges sharing one bridge prefix) have no ActorProvenanceGate at all - resolve to no match, not a fabricated secret', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/access/primary-action')).toBeNull();
    expect(resolveActorProvenanceConfigurationKey('/api/v1/access/view-access-events')).toBeNull();
  });

  it('/api/v1/tenants/primary-action (BIO-UX-005-AC04\'s own pre-existing, ungated generic simulated-action endpoint) resolves to no match - the TenantFeatureConfig entry is scoped exactly to /{tenantId}/feature-configuration, not a bare /api/v1/tenants prefix (Seyi advisory, fixed)', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/tenants/primary-action')).toBeNull();
  });
});

describe('resolveActorProvenanceConfigurationKey - no match', () => {
  it('returns null for an OAuth2 bearer-token route (different mechanism, not actor-provenance-gated)', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/oauth/token')).toBeNull();
  });

  it('returns null for /whoami specifically, even though it sits under the gated service-accounts prefix (it is bearer-token authorized, not ActorProvenanceGate-gated)', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/integrations/service-accounts/whoami')).toBeNull();
  });

  it('returns null for the bare /api/v1/persons/{id} prefix (shared by several non-provenance-gated features)', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/persons/per_1/profiles')).toBeNull();
    expect(resolveActorProvenanceConfigurationKey('/api/v1/persons/per_1/card-issues/staff')).toBeNull();
  });

  it('returns null for an unrelated path', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/health')).toBeNull();
    expect(resolveActorProvenanceConfigurationKey('/')).toBeNull();
  });

  it('BIO-TEN-001\'s /api/v1/control-plane/tenants and BIO-TEN-002\'s /api/v1/tenant-domains are different prefixes entirely, never matched by BIO-TEN-003\'s /api/v1/tenants entry, and neither is ActorProvenanceGate-gated', () => {
    expect(resolveActorProvenanceConfigurationKey('/api/v1/control-plane/tenants')).toBeNull();
    expect(resolveActorProvenanceConfigurationKey('/api/v1/tenant-domains')).toBeNull();
  });
});
