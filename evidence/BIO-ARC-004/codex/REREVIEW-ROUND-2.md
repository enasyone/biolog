# BIO-ARC-004 independent re-review — round 2

- Reviewer: Seyi
- Fix commit: `14a5346`
- Reviewed head: `2cc2ae0`
- Verdict: **FAIL**

## Closure status

- QA-01 closed: atomic claims prevent concurrent identical requests from both executing;
  differing content conflicts, waits are bounded, and failed execution releases the claim.
- QA-02 reported delimiter collision closed: JSON tuple encoding distinguishes operation
  and idempotency-key strings containing colons, quotes, and backslashes.
- QA-03 partially closed: code/status, item messages, page cursor/limit/count, and
  non-validation `errors[]` placement are now checked. Other schema constraints remain
  unenforced.

Builder acceptance and regression checks pass 26/26. Contract suite passes 252/252.

## Findings

### BIO-ARC-004-QA-03 — High — problem builders still emit schema-invalid documents

`toProblemResponse` does not require `message` to be a non-empty string. It also accepts
arbitrary `errors[]` items when `code` is `validation_failed`; primitive items become `{}`
and malformed `path`, `code`, and `message` fields pass unchanged. `toValidationProblem`
validates each item message but not its own top-level `message` option.

Examples currently returned rather than refused include:

- `{ code: "bad", status: 400, message: 42 }`
- `validation_failed` with `errors: [42]`, serialized as `errors: [{}]`
- a validation item with numeric path, non-snake-case code, and null message
- a validation problem whose top-level message is numeric

All are rejected by the published problem or validation-item schema. The fix's stated
invariant — builders enforce their exact schemas — therefore remains incomplete.

Failing probes:

- `AC02 problem builder validates message and validation-item schema`
- `AC04 validation and page builders cannot emit schema-invalid values`

### BIO-ARC-004-QA-04 — High — tenant scope accepts untrusted, non-canonical values

`idempotencyStorageKey` validates operation and key but not `tenantId`. The public
idempotency API accepts a raw tenant identifier rather than a resolved tenant context.
Non-canonical values therefore become storage scopes; notably `NaN` JSON-encodes as
`null`, producing the same tuple component as the documented control-plane scope.

This weakens the tenant-scoped guarantee and reintroduces a caller-named tenant boundary
at a shared API primitive. The tenant component must be null only for a deliberate
control-plane operation, or a canonical/provenanced tenant value otherwise.

Failing probe: `AC03 malformed tenant ids cannot alias the control-plane scope`.

## Reproduction

```sh
bash evidence/BIO-ARC-004/codex/independent-review.sh
```

## Category verdicts

| Category | Verdict | Reason |
|---|---|---|
| Functional | FAIL | Problem builders still disagree with published schemas. |
| Security | FAIL | Raw, malformed tenant values can select or alias idempotency scopes. |
| Tenant isolation | FAIL | Tenant scope lacks canonical/provenance enforcement. |
| Offline | PASS | Atomic retry claim, replay, conflict, release, and timeout behavior pass. |
| Concurrency | PASS | Concurrent identical requests execute once. |
| Regression | PASS | Foundation 26/26 and contract 252/252 pass. |
