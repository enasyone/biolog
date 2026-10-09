# BIO-INT-005 security review — Zainab

**Verdict: FAIL**  
**Reviewed head:** `f36f66b`  
**Implementation:** `e710f8a`

## Blocking findings

### ZB-INT005-01 — High — scan is an authorization decision but accepts forged attribution

`POST .../conflicts/scan` requires only a caller-written `X-Actor-Id`. It does not verify actor
provenance or require a tenant permission. Scan is not read-only: it creates, augments, withdraws
and reopens review records and writes append-only audit events attributed to that actor.

Any caller who can reach a tenant host can therefore mutate the tenant's human-review queue and
forge another actor's audit attribution. Depending on current import history, repeated scans can
change which conflicts operators see as pending or withdrawn. Calling this “audit-attribution-
only” does not match the implemented state transitions.

**Required remediation:** require canonical actor provenance and a tenant-scoped scan/manage
permission before reading import outcomes or changing review state. Add real-host negatives for
missing/malformed/forged provenance, missing grant and cross-tenant mapping IDs, plus an
authorized-positive scan. The audit actor must be the verified actor.

### ZB-INT005-02 — High — review data is disclosed without provenance or read permission

`GET .../conflicts` also accepts any non-empty caller-written actor ID and performs no permission
check. Its response includes `person_id`, external key, source batch, decision notes, and each
conflict's exact existing BIOLOG value and incoming source value. These fields can contain names,
dates of birth, identifiers or other personal identity data.

A resolved tenant context is isolation, not authorization. Direct callers can forge the actor
header and enumerate mapping IDs to retrieve the conflict queue.

**Required remediation:** verify canonical provenance and require an explicit tenant-scoped read
permission before returning the queue. Add missing/forged/unprivileged/cross-tenant negatives and
an authorized-positive test that proves the permission is evaluated against the resolved tenant.

## Confirmed controls

- `Decide` verifies HMAC actor provenance before service execution and requires the live
  tenant-scoped `integrations.conflict_review_decide` permission.
- Repository identity includes tenant, mapping and external key; decisions use version checks and
  a per-row lock.
- Decision and scan transitions audit before local commit and resolve ambiguous audit writes.
- Audit detail excludes compared field values, and this feature has no person-record mutation
  dependency, so `USE_SOURCE` cannot silently overwrite identity evidence.

## Verification

- Inspected endpoint gates, provenance provisioning, permission adapter, tenant propagation,
  import gateway, service transitions, repository compare-and-swap behavior, response data,
  audit projection and focused host/service tests.
- Builder evidence reports the suites passing, but the host tests explicitly assert that scan and
  list succeed without permission and do not exercise adversarial provenance on those routes.
