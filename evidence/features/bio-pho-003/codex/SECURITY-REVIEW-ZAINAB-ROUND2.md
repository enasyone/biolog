# BIO-PHO-003 security review — Zainab round 2

**Verdict: FAIL**  
**Reviewed head:** `958f2fd`  
**Remediation:** `e2f247d`

## Closed

### ZB-PHO003-01 — closed

All five routes now resolve an actor, verify the HMAC provenance proof, resolve the current
tenant, and require separate tenant-scoped submit/decide/view permissions. The web proxy route
table includes `/api/v1/photo-quality`, the secret remains fail-closed, and startup grant seeding
preserves every existing row, including revocations. Host tests cover real unprivileged denial
and the endpoint suite covers authorized paths through a substituted feature-local permission
gateway.

## Blocking finding

### ZB-PHO003-02 — High — post-promotion marker does not close the atomicity gap

`ApproveAsync` still calls `PromoteAsync` first and writes `PendingPromotedMasterVersion` only
after the external promotion returns. A process crash, cancellation, or repository failure
between those two operations leaves a committed `PhotoMaster` with no marker. The next retry
sees an unmarked pending capture and promotes again. The new fault-injection test starts its
failure at the later audit write, after the marker has already succeeded, so it does not exercise
the unresolved boundary.

The marker also stores only the master version and audit event ID. If operator A promotes and the
audit then fails, operator B can retry and write the preserved event ID using B's actor and new
correlation ID. The terminal audit would attribute A's external effect to B.

**Required remediation:** make promotion itself idempotent under a stable operation key derived
from tenant/person/capture, or persist a pre-promotion operation/outbox record and have an
idempotent consumer perform promotion. Persist the immutable initiating actor, correlation and
audit payload with the operation. Add fault injection for failure/crash before marker persistence,
after promotion, after audit, and before final capture commit; retries, including a different
authorized actor, must converge on one master and one audit attributed to the initiator.

## Verification

- Inspected the complete `03bf20e..958f2fd` production, bridge and test delta.
- Traced every endpoint through provenance and permission evaluation and checked synthetic grant
  seeding for revocation preservation.
- Traced approval across promotion, marker replacement, audit and final replacement, including
  retry under a different actor.
- Builder evidence reports 35/35 feature tests and 250/250 integration tests; the new recovery
  test covers audit failure after successful marker persistence, not the remaining gap.
