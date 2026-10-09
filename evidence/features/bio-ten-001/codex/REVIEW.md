# BIO-TEN-001 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `65507b6` at evidence head `02b6fae`.

AC01–AC03 structure and the supplied foundation suite pass 19/19. AC04 is not fail-closed.

## Finding

### BIO-TEN-001-QA-01 — High — Audit failure leaves the administrative mutation committed and unaudited

Every mutating `TenantService` method commits to `ITenantRegistry` first, then calls `AuditAsync`. If `ITenantAuditSink.WriteAsync` fails, the service returns `audit_unavailable`, but the created or updated tenant remains in the registry with no audit event.

The existing audit-failure test checks only the returned error; it never checks registry state. The feature documentation explicitly confirms that the mutation is not rolled back and labels this an accepted in-memory limitation. That conflicts with AC04's requirement that audit events record administrative changes and the stated hard-dependency rule: an audit outage currently produces the exact unaudited administrative change the control is meant to prevent.

Required closure: make mutation and audit atomic, or stage/roll back the registry mutation on audit failure with concurrency-safe semantics. Add negative tests for create, update, status change, and delete proving an audit failure leaves no committed state/version change.

## Reproduction

The reviewer probe source is under `evidence/features/bio-ten-001/codex/AuditAtomicityProbe/`: it uses a throwing audit sink, catches `audit_unavailable`, then lists the registry and fails if the tenant remains. Its separate project restore could not complete in the network-restricted reviewer sandbox; the defect is source-deterministic from `CreateAsync`'s `registry.CreateAsync` call preceding `AuditAsync`, and is expressly documented in the feature README.

```sh
bash tests/features/bio-ten-001/BIO-TEN-001.test.sh
bash evidence/features/bio-ten-001/codex/independent-review.sh
```

Supplied foundation suite: 19/19 PASS. The suite does not test post-failure registry state.

## Category verdicts

- Functional: FAIL — AC04 is not guaranteed.
- Security: FAIL — administrative state can change without an audit record.
- Tenant isolation: PASS for the fixed control-plane data shape and absence of tenant context.
- Offline/recovery: FAIL — audit outage leaves inconsistent state.
- Concurrency: FAIL — rollback/atomicity under concurrent writers is unimplemented.
- Regression: FAIL — failure tests assert only the response, not data integrity.
