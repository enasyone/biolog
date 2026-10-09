// Developer tests for TD-WEB-PROVENANCE-BRIDGE round 2 (ZB-WEBPROV-01/03, Emeka's half).
// SYNTHETIC: no real tenant/user data.
//
// Regression coverage for two distinct bugs found across two review rounds, not one:
//
// 1. The exact bug class Damilola found by hand-driving a real sign-out/replay flow: the
//    original in-memory Set design was NOT actually shared between proxy.ts and the
//    issuance/sign-out Server Action, because Turbopack compiles them as separate module
//    graphs even within one Node.js process. `vi.resetModules()` + a fresh dynamic import
//    forces two genuinely separate module instances within this single test file - the
//    closest a vitest test can get to reproducing that same-process-different-module-graph
//    shape - and proves a revocation recorded through one instance is visible through the
//    other, which the original Set-based design would have failed.
//
// 2. ZB-WEBPROV-03 (High, round 2): the FIRST file-backed fix (one shared JSON document,
//    unlocked read-modify-rename) could lose a concurrent revocation - two sign-outs racing
//    could each read the same state, and the later rename could erase the earlier one's
//    write. Fixed by switching to one marker file PER session id (this file's own current
//    design) - atomic by construction, not by a lock that might help. The concurrency test
//    below proves many concurrent revocations of DIFFERENT session ids all survive, which the
//    OLD shared-document design could have failed under exactly this load.
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

let storeParentDir: string;

beforeEach(() => {
  storeParentDir = mkdtempSync(path.join(tmpdir(), 'td-web-provenance-bridge-revstore-'));
  process.env.WEB_SESSION_REVOCATION_STORE_DIR = path.join(storeParentDir, `revocations-${randomUUID()}`);
});

afterEach(() => {
  delete process.env.WEB_SESSION_REVOCATION_STORE_DIR;
  rmSync(storeParentDir, { recursive: true, force: true });
});

describe('session-revocation-store - file-backed, not module-scope state', () => {
  it('a missing marker file means "no revocations yet" - isRevoked is false, not an error', async () => {
    const { isRevoked } = await import('../../../web/lib/session-revocation-store');
    expect(await isRevoked('some-sid-that-was-never-revoked')).toBe(false);
  });

  it('a revocation recorded through ONE module instance is visible through a SEPARATE module instance reading the same directory - the exact cross-bundle-graph scenario that broke the original in-memory Set', async () => {
    const instanceA = await import('../../../web/lib/session-revocation-store');
    vi.resetModules();
    const instanceB = await import('../../../web/lib/session-revocation-store');
    expect(instanceA).not.toBe(instanceB);

    await instanceA.recordRevocation('sid-revoked-via-instance-a');

    expect(await instanceB.isRevoked('sid-revoked-via-instance-a')).toBe(true);
    expect(await instanceB.isRevoked('sid-never-revoked')).toBe(false);
  });

  it('revoking many sids one after another does not lose an earlier revocation', async () => {
    const { recordRevocation, isRevoked } = await import('../../../web/lib/session-revocation-store');
    await recordRevocation('sid-1');
    await recordRevocation('sid-2');
    await recordRevocation('sid-3');

    expect(await isRevoked('sid-1')).toBe(true);
    expect(await isRevoked('sid-2')).toBe(true);
    expect(await isRevoked('sid-3')).toBe(true);
  });

  it('ZB-WEBPROV-03: many CONCURRENT revocations of different session ids all survive - one marker file per sid means there is no shared document for a later write to clobber an earlier one', async () => {
    const { recordRevocation, isRevoked } = await import('../../../web/lib/session-revocation-store');
    const sids = Array.from({ length: 50 }, (_, i) => `concurrent-sid-${i}`);

    await Promise.all(sids.map((sid) => recordRevocation(sid)));

    for (const sid of sids) {
      expect(await isRevoked(sid)).toBe(true);
    }
  });

  it('ZB-WEBPROV-03: concurrently revoking the SAME session id many times is idempotent, not a race', async () => {
    const { recordRevocation, isRevoked } = await import('../../../web/lib/session-revocation-store');
    await Promise.all(Array.from({ length: 20 }, () => recordRevocation('same-sid-revoked-many-times')));
    expect(await isRevoked('same-sid-revoked-many-times')).toBe(true);
  });

});
