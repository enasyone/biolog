# BIO-IDN-004 remediation re-review — round 4

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation commit `e2c8d9b` at builder evidence head `4b584f8`.

## Prior finding status

The round-three escape is closed: pending revocation work is now stored independently of current PERSON status, remains observable after reactivation, and can be reconciled through a dedicated idempotent operation.

## BIO-IDN-004-QA-01 remains High — audit failure occurs before pending work is recorded

The suspension path still has a post-commit gap. After the gateway commits `ACTIVE -> SUSPENDED`, `TransitionAsync` calls `WriteLifecycleEventAsync` before `pendingStore.MarkPendingAsync`. If the supplementary audit sink fails and cannot confirm the event, the method exits with `audit_unavailable`; it never records pending revocation work and never attempts the revocation publication.

The resulting PERSON is `SUSPENDED`, but the revocation stream is empty and `HasPendingRevocationsAsync` returns false. The new durable reconciliation mechanism therefore cannot see or repair this failure path.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-004/codex/RevocationAtomicityProbe/RevocationAtomicityProbe.csproj
```

Observed:

```text
PASS: rejected suspension emitted no credential/access revocation
PASS: pending revocation survived reactivation and reconciled exactly once
FAIL: suspension committed, but audit failure prevented both pending tracking and revocation emission
```

Required closure: once the suspension commit succeeds, guaranteed revocation work must be recorded before any fallible supplementary operation can return control. Audit failure must not bypass pending tracking. Cancellation after commit must likewise be unable to skip the pending record or its reconciliation path.

## Regression evidence

- BIO-IDN-004 structural suite: 55/55 PASS, but it does not cover failure of the supplementary audit between commit and `MarkPendingAsync`.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Builder evidence records feature tests 68/68, integration tests 70/70, unit tests 160/160, structural tests 55/55, and quality gate PASS.

Functional FAIL; Security FAIL; Tenant Isolation PASS; Offline/Recovery FAIL; Concurrency FAIL; Regression PASS.
