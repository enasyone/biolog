# BIO-MOB-001 independent review — round 5

## Verdict: FAIL

### Finding QA-04 — provenance marker is self-issued by any caller

`TenantContext.fromTrustedJson` is public and accepts any schema-valid map. `AppDatabase.saveTenantContext` is also public and unconditionally wraps its argument with the literal `trusted_resolution_provenance` marker. Therefore a production caller can construct a forged, locally supplied tenant claim from raw fields, pass it to `saveTenantContext`, and on the next bootstrap `loadTrustedTenantContext` will accept it as trusted. The round-3 fix only rejects records missing the marker; it does not establish that the marker came from a real resolution boundary.

Reproduction (source-level, no platform required):

```dart
final forged = TenantContext.fromTrustedJson({
  'kind': 'tenant',
  'tenant_id': 'ten_0194a7d2-7f10-7b3a-8d44-1f4c1a8e9b22',
  'binding_key': 'attacker-input',
  'channels': ['principal'],
  'correlation_id': 'evt_0194a7d2-7f10-7b3a-8d44-1f4c1a8e9b22',
  'resolved_at': '2026-09-23T00:00:00.000Z',
});
await database.saveTenantContext(forged);
// loadTrustedTenantContext() returns forged instead of invalidating it.
```

This violates ADR-0002 Decision 4.1 / AC03: field shape and a locally generated marker are not resolution provenance. The public API needs a capability or trusted-boundary token that an ordinary caller cannot manufacture; merely documenting `fromTrustedJson` as trusted is insufficient.

## Category verdicts

- Functional: PASS for the exercised foundation behavior.
- Security: FAIL (QA-04).
- Tenant isolation: FAIL (forged tenant binding is accepted).
- Offline: PASS for tested queue behavior.
- Concurrency: PASS for tested transactional idempotency path.
- Regression: FAIL because the round-3 provenance check is bypassable through its public write/construction APIs.

Foundation harness result: 35 passed, 0 failed. This does not cover the public forged-context path above.
