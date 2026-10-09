# BIO-PHO-003 security review — Zainab

**Verdict: FAIL**  
**Reviewed head:** `03bf20e`  
**Implementation:** `37531ff`

## Blocking findings

### ZB-PHO003-01 — Critical — caller-controlled identity can replace another person's authoritative photo

All five routes lack trusted actor provenance and tenant-scoped permission enforcement. Submit,
approve and reject accept any non-empty caller-written `X-Actor-Id`; list and get require no actor
at all. The comment that no authentication story has landed is stale: this build already contains
the canonical provenance and role-permission infrastructure used by current tenant-plane modules.

This is not audit attribution only. `approve` calls `PhotoMasterService.AppendAsync`, changing the
person's authoritative photo. A caller who can reach a tenant host and knows a valid person ID can
upload arbitrary bytes, approve them, and attribute the decision to any chosen actor. The same
caller can list biometric-suitability verdicts, operator decision reasons and photo metadata
without authorization.

**Required remediation:** apply the canonical actor-provenance gate, resolve the live tenant actor,
and require explicit tenant-scoped permissions before every read or mutation. Use separate least-
privilege read/manage/decision permissions as appropriate. Add real-host negatives for missing,
malformed and forged provenance, unprivileged actors, and cross-tenant person IDs, plus an
authorized-positive path. Never treat `X-Actor-Id` alone as authority.

### ZB-PHO003-02 — High — approval promotes before its audit/state transaction is secured

`ApproveAsync` calls `promotionGateway.PromoteAsync` before writing
`photo_quality.capture.approve` and before replacing the pending capture. If this story's audit
write is genuinely unavailable, the call fails while the external `PhotoMaster` has already been
appended and independently committed. The capture remains `Pending`; retrying approval can append
another master version and repeat the side effect.

The service header discloses the lack of a distributed transaction, but disclosure does not make
the claimed audited operator decision or retry behavior correct. Existing tests cover only the
all-success path and do not inject a post-promotion audit failure.

**Required remediation:** make approval a recoverable, idempotent operation across promotion,
audit and local state — for example with a durable operation/claim keyed by tenant, person and
capture sequence plus a resumable outbox/state machine. A retry after any failure must converge on
one promoted master and one truthful terminal decision audit. Add fault-injection tests at each
boundary, including audit failure after successful promotion and local commit failure after audit.

## Confirmed controls

- Capture storage and lookups are tenant-keyed.
- The biometric-suitability adapter calls only the quality and liveness interfaces; it does not
  invoke `FaceEnrolmentService.CaptureAsync` or create a biometric reference.
- Raw photo bytes are not copied into the feature audit events.
- Capture content uses defensive copies, and approve/reject use version checks under a per-person
  lock.

## Verification

- Inspected endpoint mapping, actor resolution, tenant propagation, repository keys, service
  transitions, audit construction/write recovery, photo-master promotion, biometric adapter and
  the focused endpoint/service/host tests.
- Builder evidence reports 23/23 feature tests and 247/247 full integration tests; those suites do
  not cover either blocking adversarial path above.
