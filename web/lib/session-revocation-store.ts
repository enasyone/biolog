/**
 * File-backed revoked-session-id store (TD-WEB-PROVENANCE-BRIDGE round 2, ZB-WEBPROV-01/03).
 *
 * `session-token.ts` originally kept revoked session ids in a module-scope `Set<string>`,
 * documented as safe because both `proxy.ts` and the issuance/sign-out Server Action pin to
 * the Node.js runtime and therefore "share one process's memory." **That turned out to be
 * necessary but not sufficient** — Damilola found, by hand-driving the real sign-out/replay
 * flow against a real dev server (not just unit tests), that Turbopack compiles the
 * middleware/proxy bundle and the Server Action bundle as SEPARATE module graphs even within
 * one Node.js process, so each gets its OWN copy of any module-scope state. A revoked session
 * id written by the sign-out action was never visible to the proxy's own copy, so sign-out
 * silently did nothing and a replayed pre-sign-out cookie still produced a valid proof.
 * "Same process" does not mean "same module instance" once a bundler is involved.
 *
 * **First fix (fail): one shared JSON document.** The first version of this file kept every
 * revoked `sid` in one JSON file, read-modify-written with no cross-instance lock (write-to-
 * temp-then-rename, the same shape `FileBackedPendingGalleryCleanupStore.cs` - BIO-BIO-009
 * round 4 - already established for an identical cross-instance need). Zainab's round-2
 * review (ZB-WEBPROV-03, High) correctly rejected the "no lock, documented race, accepted as
 * proportionate" framing that fix shipped with: two concurrent revocations could race, and the
 * LATER rename could silently erase an EARLIER one's revocation - "a narrow residual race
 * accepted as proportionate" is a reasonable bar for a lost AUDIT record (BIO-INT-002's own
 * precedent for that framing), but not for a REVOCATION, where losing the write means a
 * credential that is supposed to be dead silently stays alive. Applying a lower-stakes
 * precedent's accepted-residual bar to a strictly higher-stakes security primitive was the
 * actual mistake here, not the file-backed mechanism itself.
 *
 * **Second fix (this one): one marker file PER session id, not one shared document.** Revoking
 * `sid` now means creating a file at `${revocationDir()}/${encodeURIComponent(sid)}.revoked`;
 * checking revocation means checking that ONE file's existence. This is atomic by
 * CONSTRUCTION, not by a lock that might help: two concurrent revocations of two DIFFERENT
 * session ids touch two entirely different files and cannot race each other at all - there is
 * no shared document for a later write to clobber. Two concurrent revocations of the SAME
 * session id both write the same (empty, content-free) marker and are idempotent by
 * definition. No read-modify-write cycle exists anymore for revoking, so there is nothing left
 * to lose.
 *
 * **Fails closed on anything it cannot positively rule out as "not revoked"**: a marker file
 * that does not exist (`ENOENT`) means genuinely not revoked. Any OTHER filesystem error
 * (permission denied, a directory where a file was expected, etc.) means "cannot prove this
 * sid is NOT revoked" - `isRevoked` returns `true` rather than silently treating an
 * unreadable/unexpected filesystem state as "nothing is revoked."
 */

import { promises as fs } from 'node:fs';
import path from 'node:path';

function revocationDir(): string {
  return process.env.WEB_SESSION_REVOCATION_STORE_DIR || path.join(process.cwd(), '.web-session-revocations');
}

function markerPath(sid: string): string {
  // `sid` is always a `crypto.randomUUID()` in production, but encode defensively anyway -
  // this function must never let a crafted `sid` escape the revocation directory via `..`/`/`.
  return path.join(revocationDir(), `${encodeURIComponent(sid)}.revoked`);
}

export async function recordRevocation(sid: string): Promise<void> {
  await fs.mkdir(revocationDir(), { recursive: true });
  await fs.writeFile(markerPath(sid), '', 'utf8');
}

/** Fails closed: anything other than "the marker file does not exist" is treated as "cannot prove this sid is not revoked." */
export async function isRevoked(sid: string): Promise<boolean> {
  try {
    await fs.access(markerPath(sid));
    return true;
  } catch (err) {
    return (err as NodeJS.ErrnoException)?.code !== 'ENOENT';
  }
}

/** Test-only escape hatch - production code never needs to inspect or reset the store directly. */
export async function __clearRevocationStoreForTests(): Promise<void> {
  const dir = revocationDir();
  await fs.rm(dir, { recursive: true, force: true });
}
