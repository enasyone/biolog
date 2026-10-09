# BIO-BIO-009 security review — round 3

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** remediation `366ee60`, evidence `302c735`

## Closed from round 2

- Raw fingerprint image retention is now checked against the authoritative tenant policy at the BIO-BIO-002 write boundary and fails closed on policy errors. Face enrolment continues to discard the source image.
- The public deterministic provenance fallback is removed; an unconfigured store rejects proofs.
- Deletion now records owner identity before source deletion, has a typed pending-cleanup outcome, and exposes a permission-gated reconciler.
- Two independently switchable frontend ports and a visible retention section now exist.

## Blocking findings

### ZB-BIO009-07 — High — weak provenance secrets remain forgeable

`HmacActorProvenanceStore.Configure` accepts every non-empty string, including a one-character key. This gate protects retention-policy administration and biometric deletion. An attacker who observes one `(actorId, proof)` pair can brute-force such a tiny configured key and forge proofs for a privileged actor.

Kemi's floor-wide normalization now requires the same `MinimumSecretLength = 32` no-fallback pattern in every provenance gate. This new/remediated copy must not retain the weak-key path.

Required: reject/null configured secrets shorter than 32 characters and test absent, empty, 31-character, and valid 32-character values, including former-proof rejection.

### ZB-BIO009-08 — High — deletion exceptions can erase the recovery obligation

The service correctly records `PendingGalleryCleanup` before source deletion, but its catch around `Delete*Async` removes that entry on every exception, with the comment that the record was never actually deleted. That inference is not valid for an asynchronous persistence boundary: a repository or gateway can delete successfully and then throw or lose the response. Removing the only saved `PersonId` then recreates ZB-06—source gone, gallery entry still searchable, no reconciliation record.

The test fake throws before doing anything, so it proves only the non-ambiguous case and encodes the unsafe assumption.

Required: preserve the obligation until the source record's state is independently re-read. Remove it only when the record is confirmed present/not deleted; retain and reconcile it when deletion is confirmed or ambiguous. Add a gateway mutant that deletes then throws.

### ZB-BIO009-09 — High — the claimed live control surface cannot pass backend authorization

`web/features/biometrics/live.ts` sends only `Content-Type` for retention policy and deletion. Both backend endpoints require `X-Actor-Id` and a valid `X-Actor-Provenance` proof before live permission lookup. No trusted edge/proxy in this change injects those values. Consequently, switching either new port to LIVE deterministically returns an authorization error; the visible control is not connected end to end.

The 4/4 Playwright run exercises the default simulated adapter, not an authenticated LIVE browser-to-backend request.

Required: connect the UI through the approved authenticated trusted-edge session so verified actor provenance is injected server-side; never expose the shared HMAC secret to browser JavaScript. Add an end-to-end LIVE test proving an authorised user succeeds and an unprivileged user is denied.

## Non-blocking observation

On successful reconciliation, the pending entry is removed before `gallery_cleanup_reconciled` audit. Audit failure then loses the retry marker and the completion event. Gallery privacy has converged, so this is lower severity than the blockers, but audit/remove ordering should be made recoverable.

## Verification

- Reviewed the provenance store, BIO-BIO-002 write-boundary authority, face-image behavior, cleanup ledger/reconciler, gateway implementations, frontend port selection and live request headers.
- Builder evidence reports: BIO-002 35/35, BIO-003 36/36, BIO-006 29/29, BIO-007 25/25, BIO-009 32/32, integration 202/202, unit 200/200, frontend 473/473, Playwright 4/4.
