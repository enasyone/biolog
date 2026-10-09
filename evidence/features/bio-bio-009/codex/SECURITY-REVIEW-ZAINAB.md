# BIO-BIO-009 security review — round 1

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** implementation `840f476`, builder evidence `9190d8b`

## Blocking findings

### ZB-BIO009-01 — Critical — caller-controlled headers authorize privacy administration

All three mutations accept `X-Actor-Id` and `X-Actor-Permission` directly from the HTTP caller. `PermissionResolver.Resolve` merely returns the header, and the services authorize when that string equals the required constant. There is no ADR-0006 actor-provenance gate and no BIO-IDN-006 role-based access control lookup. Any tenant-plane caller can therefore name any actor, assert `biometric.retention.policy.manage` or `biometric.retention.delete`, change raw-image retention, change retention periods, and submit deletion requests with audit attribution forged as the chosen actor.

The implementation's statement that BIO-IDN-006 has not landed is stale: this base contains BIO-IDN-006 and many current mutation surfaces enforce the ADR-0006 provenance gate. A permission claim is not a permission decision.

Required: verify actor provenance before every protected operation and resolve the permission from the tenant-scoped role/permission source. Add negatives for a privileged permission header with missing/invalid provenance, impersonation of a real privileged actor, and a proven actor who lacks the relevant grant.

### ZB-BIO009-02 — High — AC04's deletion and propagation do not exist in production

The registered production adapter returns `NotYetAvailable` for every existing fingerprint record and every face request. Both gallery-removal methods always return `false`. Thus no template is deleted and no permitted cache/provider is invalidated. A test-double proving an orchestration interface cannot satisfy an acceptance criterion whose required side effect never occurs in the composed host. The typed outcome is honest, but it is evidence of incompleteness rather than an interim implementation of AC04.

Moreover, the service deliberately reports `Deleted` even when gallery invalidation returns `false`. That leaves a biometric gallery entry usable after the system reports primary deletion. For biometric erasure, propagation failure must remain a visible, retryable incomplete state (or be reconciled durably), not a successful terminal result.

Required: add real tenant-scoped delete/remove capabilities to the owning stores, wire the production adapter, and prove composed-host deletion plus cache/provider invalidation. Preserve immutable audit metadata. Model partial propagation durably and retry it, or return a non-success outcome until convergence.

### ZB-BIO009-03 — High — raw-image authorization is disconnected from capture

BIO-BIO-009 stores a separate tenant authorization map, but no enrolment/capture path reads it. BIO-BIO-002 still decides whether to persist the raw fingerprint payload from its session's `retentionAuthorised` boolean. Changing or revoking BIO-BIO-009's tenant policy therefore has no effect on actual raw-image retention. The new map defaulting to `false` does not enforce AC03 at the data-write boundary.

Required: make every raw-image persistence path query the authoritative tenant policy server-side, fail closed when it is unavailable, and test that caller input cannot enable retention, default-off capture discards the payload, authorization permits it, and revocation affects subsequent capture.

### ZB-BIO009-04 — High — caller supplies an unrelated person for cache eviction

The deletion request contains both `RecordId` and caller-controlled `PersonId`. Record deletion is performed using only `RecordId`, while gallery removal uses only the supplied `PersonId`; the gateway does not establish that they belong together. Once real deletion is wired, a caller can delete record A while evicting person B's gallery entry, leaving A cached and disrupting B.

Required: derive the owner/person from the tenant-scoped stored record inside the deletion transaction; do not trust a second caller-supplied ownership identifier. Add mismatch and cross-tenant adversarial tests.

## Other scope results

- AC01 policy values are tenant/category keyed, but are configuration only; no expiry/purge worker consumes them. The user story's legal-hold behavior is also absent. These are functional completeness gaps for the acceptance review.
- AC02 audit-before-attempt and immutable audit-sink shape are present, but forged actor attribution blocks security acceptance.
- AC05 explicitly permits an accurately documented HYBRID state. The zero-web-change disclosure can be assessed by product/QA, but it does not waive AC04 or the security gates above.
- Tenant keys are used consistently in the new in-memory policy stores and adapter lookup; no direct cross-tenant read was found in this feature.

## Verification

- Source review of the endpoints, policy/deletion services, production gateway adapter, BIO-BIO-002 raw-image write path, and feature tests.
- Builder evidence records 30/30 feature tests and broader suites passing.
- Local replay could not start `vstest`: the managed sandbox denied its loopback socket bind (`SocketException (13): Permission denied`). This environment limitation does not affect the source-proven findings.
