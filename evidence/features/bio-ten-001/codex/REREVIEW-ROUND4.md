# BIO-TEN-001 remediation re-review — round 4

**Verdict: FAIL**

Reviewed remediation `f0c0eb7` at evidence head `49d433d`.

The singleton per-tenant lock closes the in-process competing-writer window, but it does not make the audit write and registry commit atomic. Two blocking defects remain:

1. An audit sink may durably append the `ALLOWED / tenant_updated` event and then throw (timeout, lost acknowledgement, or failure after persistence). `AuditAsync` converts that to `audit_unavailable` and the locked service never commits, leaving the same false-success event. A lock cannot resolve an ambiguous result from the independent audit store.
2. A process failure after the audit write returns but before the registry commit also leaves a permanent success event for a mutation that never landed.

The new concurrency test does not exercise its claimed interleaving: `DelayFirstCallAuditSink` is installed before `CreateAsync`, so its one delay is consumed by the seed create. Both later update calls use a non-delaying sink and the in-memory methods complete synchronously. The test therefore proves sequential stale-version behavior, not contention on the new lock.

This confirms the architecture-level result: with two independent stores, ordering plus an in-process lock cannot guarantee an exact final audit outcome. Closure requires a shared atomic unit (for example mutation plus transactional outbox) or an explicit attempt lifecycle with detectable pending state, terminal finalization, and reconciliation semantics.

Functional FAIL; Security FAIL; Tenant Isolation PASS; Offline/Recovery FAIL; Concurrency FAIL (claimed test invalid); Regression FAIL.
