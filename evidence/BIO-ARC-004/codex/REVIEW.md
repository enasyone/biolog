# BIO-ARC-004 independent adversarial review

- Reviewer: Seyi
- Implementation commit: `66fae3e`
- Reviewed head: `42c3951`
- Verdict: **FAIL**

## Findings

### BIO-ARC-004-QA-01 — High — concurrent retries execute the write twice

`withIdempotency` performs `store.get`, then `execute`, then `store.set` as separate
operations. Two requests arriving before either stores its result both observe no record
and both execute. This defeats idempotency on the exact failure mode the convention exists
for: concurrent retries of card activation, production completion, offline sync, or
webhook intake. A crash after `execute` but before `set` has the same duplicate-effect gap.

Failing probe: `AC03 concurrent identical retries execute exactly once`.

### BIO-ARC-004-QA-02 — High — delimiter-based scope keys are not injective

The storage key is raw string concatenation with `:` while both `operation` and the opaque
client key may contain `:`. For one tenant, `(operation="card", key="activate:key")` and
`(operation="card:activate", key="key")` produce the same storage key. A record from one
operation can therefore replay or conflict with another operation, contradicting the
documented tenant-plus-operation scope.

Failing probe: `AC03 operation and idempotency-key tuples cannot collide`.

### BIO-ARC-004-QA-03 — High — response builders can violate their published schemas

`toProblemResponse` accepts invalid machine codes and statuses including `NaN`, fractional
values, and values outside 100–599. It also copies `errors` onto any problem code although
ADR-0004 says validation errors occur only with `validation_failed`.

`toValidationProblem` accepts a non-string item message. `buildPageEnvelope` accepts a
non-string cursor, a missing limit, and negative or fractional totals. Each resulting
object is rejected by the corresponding published schema. The shared constructors and
schemas therefore do not define one interoperable response format.

Failing probes:

- `AC02 problem builder cannot emit a schema-invalid code or status`
- `AC02 non-validation problems cannot carry validation errors`
- `AC04 validation and page builders cannot emit schema-invalid values`

## Verified behavior

- `/api/v1` is the public HTTP prefix and non-path versioning is documented.
- Existing tenant, concurrency, and audit errors serialize into the common envelope.
- Valid correlation identifiers round-trip unchanged into a real audit event.
- Sequential identical retries replay without re-executing; changed payloads conflict.
- Validation paths use raw segments and ADR-0003 escaping.
- Builder acceptance and regression harness: 25/25. Contract suite: 230/230.

## Reproduction

```sh
bash evidence/BIO-ARC-004/codex/independent-review.sh
```

## Category verdicts

| Category | Verdict | Reason |
|---|---|---|
| Functional | FAIL | Shared response builders can produce schema-invalid documents. |
| Security | FAIL | Idempotency scope-key collisions cross operation boundaries. |
| Tenant isolation | PASS | Keys remain tenant-scoped and prior tenant regressions pass. |
| Offline | FAIL | Concurrent retry intake can apply the same write twice. |
| Concurrency | FAIL | Check-then-execute-then-store is not atomic. |
| Regression | PASS | Builder 25/25 and contract 230/230 pass. |
