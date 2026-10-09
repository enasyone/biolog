# BIO-OPS-002 independent acceptance — round 2

Verdict: **FAIL**

Reviewed remediation `56927c7` as merged into `feature/BIO-OPS-002` at `db17392`.

## Required-category verdicts

| Category | Verdict |
|---|---|
| Functional | FAIL |
| Security | PASS for token verification and the closed two-name allowlist |
| Tenant isolation | FAIL |
| Offline/recovery | FAIL |
| Concurrency | Not reached while required AC01/AC04 invariants fail |
| Regression | PASS by source trace for the existing primary synthetic tenant path |

## SY-OPS002-03 — High — beta silently collapses to alpha

AC01 requires switching tenant to change the real database/data context. The UI always offers `alpha` and `beta`, but `DemoSessionTenantResolver.ResolveSyntheticTenantId` resolves beta to `_syntheticTenantIdBeta ?? _syntheticTenantId`. When the optional beta key is absent, both choices resolve to the same backend tenant. The remediation's own integration test explicitly asserts this collapse.

This is the original cosmetic-switch failure under a supported/default configuration. Either beta must be required when the selector exposes it, the backend must fail closed for beta, or the UI must not offer beta without a distinct backend mapping.

## SY-OPS002-04 — High — successful context changes can have no audit record

AC04 says audit records context changes. `startDemoSession` writes the replacement cookie before calling `void recordContextChangeAudit(...)`; that function catches all delivery errors and does not inspect non-success HTTP responses. Therefore an unreachable backend, HTTP 401/500, cancellation, or process exit can complete the context change with no audit record.

The backend registers `InMemoryDemoSessionContextAuditSink`, whose queue is erased on process restart. Its own header admits it has no cross-restart durability. That does not satisfy the required durable record described by the original accepted remediation request or ADR-0003's hard-dependency rule, which the implementation explicitly deviates from without an approval recorded in the story.

## Reproduction

`bash tests/features/bio-ops-002/BIO-OPS-002.acceptance.sh` reports three failed required invariants and exits 1.

The focused .NET integration test command could not execute in this sandbox because MSBuild named-pipe socket binding is denied. This environment limitation does not affect the source-level reproductions above; the builder tests also encode the beta-to-alpha collapse as expected behavior rather than catching it.
