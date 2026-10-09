# BIO-IDN-004 remediation re-review — round 2

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation commit `b4b2223` at builder evidence head `56116aa`.

## Prior finding status

The original missing-revocation direction is closed: if revocation publication fails, the gateway is not called and the person remains `ACTIVE`.

## BIO-IDN-004-QA-01 remains High — rejected suspension emits a false revocation

The remediation only reverses two independent writes. It publishes the credential/access revocation event before asking the authoritative gateway to commit `ACTIVE -> SUSPENDED`. If the gateway rejects the request—for example because the supplied version is stale—the person remains `ACTIVE`, but a permanent revocation event already instructs consumers to disable access.

This is the inverse split state explicitly warned against in the round-one required closure. Calling it an “accepted trade-off” does not satisfy AC02 or the user story: a revocation event must correspond to an authorized, successful emergency suspension, not a rejected request.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-004/codex/RevocationAtomicityProbe/RevocationAtomicityProbe.csproj
```

Observed:

```text
PASS: failed revocation publication left the person ACTIVE
FAIL: credential/access revocation was emitted for a suspension the gateway rejected
```

The probe uses an `ACTIVE` version-2 snapshot and submits version 1. The gateway returns `concurrency_conflict`; the revocation sink nevertheless contains the event.

Required closure remains unchanged: make authoritative suspension and guaranteed revocation work atomic from the consumer perspective, such as status mutation plus transactional outbox, or an explicitly modeled non-final state with mandatory idempotent reconciliation. Neither ordering of two independent final writes is sufficient.

## Regression evidence

- BIO-IDN-004 structural suite: 49/49 PASS, but its new checks enforce publication order rather than the two-sided atomicity invariant.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Builder evidence records feature tests 66/66, integration tests 70/70, unit tests 160/160, structural tests 49/49, and quality gate PASS.

Functional FAIL; Security FAIL; Tenant Isolation PASS; Offline/Recovery FAIL; Concurrency FAIL; Regression PASS.
