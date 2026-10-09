# BIO-OPS-002 independent acceptance — round 3

Verdict: **FAIL**

Reviewed remediation `8c01d45` as merged into `feature/BIO-OPS-002` at `3be38f5`.

## Required-category verdicts

| Category | Verdict |
|---|---|
| Functional | FAIL |
| Security | FAIL — audit attribution/envelope is not the accepted contract |
| Tenant isolation | PASS for the repaired two-entry mapping and unconfigured-beta rejection |
| Offline/recovery | FAIL — audit is lost independently of the committed browser session |
| Concurrency | Not reached while AC04 fails |
| Regression | PASS by source trace for primary-tenant resolution and simulation mode |

## Closed from round 2

- SY-OPS002-03 is closed: beta now resolves to its separately configured tenant or is rejected; it never substitutes alpha.
- The committed-but-unaudited delivery failure in SY-OPS002-04 is closed: the web action now awaits an HTTP-successful audit response before setting the cookie.

## SY-OPS002-05 — High — the accepted session can outlive its audit record

`TenantModule` registers `InMemoryDemoSessionContextAuditSink`, a process-local queue explicitly erased on API restart. The resulting session cookie is committed by the independently deployed Next.js process and remains in the browser across that restart. Therefore this reachable sequence violates ADR-0003 Decision 1:

1. API appends the event in memory and returns 200.
2. Web commits the replacement cookie.
3. API restarts; the audit queue is empty while the browser session remains valid.

ADR-0003 Decision 7 permits same-lifetime volatile audit and domain state only because both disappear together. It expressly reopens the boundary when one side is durable, remote, independently transactional, or shared across processes. Here the audit and acknowledged effect are already across independent processes and lifetimes.

## SY-OPS002-06 — High — context-change records violate the frozen audit contract

ADR-0003 Decision 1 requires the one shared append-only envelope and rejects separate subsystem shapes. `DemoSessionContextAuditEvent` is a bespoke shape missing mandatory `tenant_id`, structured `actor`, `device`, `action`, `target`, `reason_code`, `causation_id`, and `withheld` semantics. The endpoint also emits `classification = "synthetic"`, while the frozen schema permits only `public`, `internal`, `personal`, or `sensitive`. It accepts an arbitrary correlation header without validating the canonical `evt_...` format.

Consequently AC04 does not produce a valid BIOLOG audit record even when delivery succeeds. This shared-contract deviation has no superseding Architecture Decision Record or Kemi approval.

## Reproduction

`bash tests/features/bio-ops-002/BIO-OPS-002.acceptance.sh` reports the remaining AC04 violations and exits 1.

The focused .NET suite remains unexecutable in this sandbox because MSBuild named-pipe binding is denied. The builder suite does not test API restart survival or validate the emitted event against `contracts/schemas/audit-event.schema.json`.
