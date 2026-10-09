// Developer tests for TD-WEB-PROVENANCE-BRIDGE round 2 (ZB-WEBPROV-01, Critical, Emeka's half).
// SYNTHETIC: no real tenant/user data; the secret below is a literal test fixture.
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DEMO_TENANT_IDS as SESSION_DEMO_TENANT_IDS } from '../../../web/context/session-context';
import { DEMO_TENANT_IDS, isDemoTenantId } from '../../../web/lib/demo-identity';
import {
  MINIMUM_SESSION_TOKEN_SECRET_LENGTH,
  __clearRevokedSessionsForTests,
  isDemoSessionIssuanceAllowed,
  issueSessionToken,
  revokeSession,
  verifySessionToken,
} from '../../../web/lib/session-token';

const TEST_SECRET = 'this-is-a-synthetic-session-token-secret-32chars-plus';

// Each test run gets its own revocation-store file - this story's own revocation store is
// deliberately file-backed (ZB-WEBPROV-01 follow-up: an in-memory Set doesn't actually share
// state across Turbopack's separate module graphs), so these tests must point it somewhere
// private, not the real dev-time default path a concurrently-running dev server might also be
// using in this shared worktree.
let revocationStoreDir: string;

describe('demo-identity tenant ids stay in sync with session-context.tsx', () => {
  it('lib/demo-identity.ts\'s DEMO_TENANT_IDS has exactly the same ids as session-context.tsx\'s own list', () => {
    expect([...DEMO_TENANT_IDS].sort()).toEqual([...SESSION_DEMO_TENANT_IDS].sort());
  });
});

describe('isDemoTenantId', () => {
  it('accepts every known tenant id and rejects unknown/empty/null/undefined', () => {
    for (const id of DEMO_TENANT_IDS) {
      expect(isDemoTenantId(id)).toBe(true);
    }
    expect(isDemoTenantId('gamma')).toBe(false);
    expect(isDemoTenantId('')).toBe(false);
    expect(isDemoTenantId(null)).toBe(false);
    expect(isDemoTenantId(undefined)).toBe(false);
  });
});

describe('issueSessionToken / verifySessionToken', () => {
  const originalSecret = process.env.WEB_SESSION_TOKEN_SECRET;
  const originalIssuanceFlag = process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE;

  beforeEach(async () => {
    process.env.WEB_SESSION_TOKEN_SECRET = TEST_SECRET;
    // ZB-WEBPROV-02: issuance defaults to refused - every test in this describe block except
    // the ones that specifically test the gate itself needs this explicitly enabled, the same
    // way a real sanctioned demo deployment's own environment must set it.
    process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE = 'true';
    revocationStoreDir = mkdtempSync(path.join(tmpdir(), 'td-web-provenance-bridge-'));
    process.env.WEB_SESSION_REVOCATION_STORE_DIR = path.join(revocationStoreDir, `revocations-${randomUUID()}`);
    await __clearRevokedSessionsForTests();
  });

  afterEach(() => {
    process.env.WEB_SESSION_TOKEN_SECRET = originalSecret;
    if (originalIssuanceFlag === undefined) {
      delete process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE;
    } else {
      process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE = originalIssuanceFlag;
    }
    delete process.env.WEB_SESSION_REVOCATION_STORE_DIR;
    rmSync(revocationStoreDir, { recursive: true, force: true });
  });

  it('a genuinely issued token for a synthetic role+tenant verifies successfully and round-trips the claims', async () => {
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'beta' });
    expect(token).not.toBeNull();

    const result = await verifySessionToken(token);
    expect(result.valid).toBe(true);
    if (result.valid) {
      expect(result.claims.roleId).toBe('admin');
      expect(result.claims.tenantId).toBe('beta');
      expect(result.claims.sid).toEqual(expect.any(String));
      expect(result.claims.sid.length).toBeGreaterThan(0);
    }
  });

  it('two tokens issued for the same role+tenant have different session ids (sid is unique per issuance)', async () => {
    const first = await issueSessionToken({ roleId: 'staff', tenantId: 'alpha' });
    const second = await issueSessionToken({ roleId: 'staff', tenantId: 'alpha' });
    const firstResult = await verifySessionToken(first);
    const secondResult = await verifySessionToken(second);
    expect(firstResult.valid && secondResult.valid && firstResult.claims.sid !== secondResult.claims.sid).toBe(true);
  });

  it('returns unconfigured and never issues a token when the secret is missing', async () => {
    delete process.env.WEB_SESSION_TOKEN_SECRET;
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    expect(token).toBeNull();

    const result = await verifySessionToken('anything.at-all');
    expect(result).toEqual({ valid: false, reason: 'unconfigured' });
  });

  it('returns unconfigured and never issues a token when the secret is shorter than the minimum length (TD-PROVENANCE-DUMMYKEY-SWEEP discipline)', async () => {
    process.env.WEB_SESSION_TOKEN_SECRET = 'short-secret';
    expect('short-secret'.length).toBeLessThan(MINIMUM_SESSION_TOKEN_SECRET_LENGTH);
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    expect(token).toBeNull();
  });

  it('rejects a missing token as malformed (no session at all - the ordinary no-cookie case)', async () => {
    const result = await verifySessionToken(undefined);
    expect(result).toEqual({ valid: false, reason: 'malformed' });
  });

  it('rejects a garbage/forged token string with no valid signature', async () => {
    const result = await verifySessionToken('not-a-real-token-at-all');
    expect(result.valid).toBe(false);
    if (!result.valid) {
      expect(result.reason).toBe('malformed');
    }
  });

  it('rejects a token with a tampered payload - flipping any claim byte invalidates the whole signature (role and tenant are bound together)', async () => {
    const token = await issueSessionToken({ roleId: 'staff', tenantId: 'alpha' });
    expect(token).not.toBeNull();
    const [payloadB64, signature] = token!.split('.');
    // Forge an "admin" claim by hand-crafting a different payload, keeping the ORIGINAL signature.
    const forgedPayload = Buffer.from(JSON.stringify({ sid: 'forged-sid', roleId: 'admin', tenantId: 'alpha', issuedAtMs: Date.now(), expiresAtMs: Date.now() + 10_000 }), 'utf8')
      .toString('base64')
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
    const forgedToken = `${forgedPayload}.${signature}`;
    const result = await verifySessionToken(forgedToken);
    expect(result).toEqual({ valid: false, reason: 'bad_signature' });

    // Sanity: the ORIGINAL payload with its OWN original signature still verifies fine.
    const originalResult = await verifySessionToken(`${payloadB64}.${signature}`);
    expect(originalResult.valid).toBe(true);
  });

  it('rejects a token signed with the wrong secret (a different signing key never produces a token this verifier accepts)', async () => {
    process.env.WEB_SESSION_TOKEN_SECRET = 'a-completely-different-synthetic-secret-value-here';
    const tokenSignedWithOtherSecret = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });

    process.env.WEB_SESSION_TOKEN_SECRET = TEST_SECRET;
    const result = await verifySessionToken(tokenSignedWithOtherSecret);
    expect(result).toEqual({ valid: false, reason: 'bad_signature' });
  });

  it('rejects an expired token', async () => {
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' }, -1);
    const result = await verifySessionToken(token);
    expect(result).toEqual({ valid: false, reason: 'expired' });
  });

  it('rejects a revoked token even though its signature and expiry are both still otherwise valid (sign-out)', async () => {
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    const beforeRevocation = await verifySessionToken(token);
    expect(beforeRevocation.valid).toBe(true);

    if (beforeRevocation.valid) {
      await revokeSession(beforeRevocation.claims.sid);
    }

    const afterRevocation = await verifySessionToken(token);
    expect(afterRevocation).toEqual({ valid: false, reason: 'revoked' });
  });

  it('revoking one session does not invalidate a different, still-live session', async () => {
    const tokenA = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    const tokenB = await issueSessionToken({ roleId: 'staff', tenantId: 'beta' });
    const resultA = await verifySessionToken(tokenA);
    if (resultA.valid) {
      await revokeSession(resultA.claims.sid);
    }

    expect((await verifySessionToken(tokenA)).valid).toBe(false);
    expect((await verifySessionToken(tokenB)).valid).toBe(true);
  });

  it('ZB-WEBPROV-02: refuses to issue ANY token at all when BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE is unset (this repo\'s real default) - including for admin, the exact privilege a caller could otherwise just ask for', async () => {
    delete process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE;
    expect(isDemoSessionIssuanceAllowed()).toBe(false);
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    expect(token).toBeNull();
  });

  it('ZB-WEBPROV-02: refuses to issue when the flag is set to anything other than the exact literal "true" (e.g. a truthy-looking but wrong value)', async () => {
    process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE = '1';
    expect(isDemoSessionIssuanceAllowed()).toBe(false);
    expect(await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' })).toBeNull();
  });

  it('ZB-WEBPROV-02: issues normally once BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE is explicitly "true" (sanity check that the gate is an opt-in, not a permanent lockout)', async () => {
    expect(isDemoSessionIssuanceAllowed()).toBe(true);
    expect(await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' })).not.toBeNull();
  });

  it('round 4 fail-closed gap: disabling BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE instantly invalidates every already-issued token, not just future issuance - the deployment off switch revokes the active issuance epoch, not a separate counter', async () => {
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    expect((await verifySessionToken(token)).valid).toBe(true);

    delete process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE;

    const result = await verifySessionToken(token);
    expect(result).toEqual({ valid: false, reason: 'issuance_disabled' });
  });

  it('round 4 fail-closed gap: re-enabling the flag makes the SAME still-unexpired, non-revoked token valid again (the gate re-checks live state every call, it does not burn a one-time epoch)', async () => {
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    delete process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE;
    expect((await verifySessionToken(token)).valid).toBe(false);

    process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE = 'true';
    expect((await verifySessionToken(token)).valid).toBe(true);
  });
});
