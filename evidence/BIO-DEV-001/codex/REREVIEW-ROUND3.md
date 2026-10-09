# BIO-DEV-001 remediation re-review — round 3

**Verdict: PASS**

Reviewed QA-03 remediation `09eda1a` at evidence head `e289a87`.

## Result

- QA-03 is closed. Once `IAuditSink.RecordAsync` succeeds, `ChangeStatusAsync` calls the repository with `CancellationToken.None`; cancellation arriving after the final `ALLOWED / status_updated` event can no longer abandon its matching commit.
- The independent cancellation probe now passes using the shipped `InMemoryAuditSink`: its logger cancels the caller token after the audit queue append, and the device still commits to version 2 with the status matching the sole audit event.
- The concurrency evidence defect is closed. The revised test blocks the first writer inside a gated audit sink, proves the second writer remains pending while the singleton device lock is held, then verifies one commit, one conflict, and one audit event.
- The process-crash residual is accepted under the same narrow boundary as BIO-TEN-001: the registered repository and audit sink are singleton, same-process, volatile stores, so neither survives a process death. This must be reopened before either becomes durable, remote, independently transactional, or shared across processes.

## Verification

- Independent cancellation probe: PASS.
- Compiled unit suite: 79 passed, 0 failed.
- Structural suite: 36 passed, 0 failed.

## Acceptance criteria and categories

- AC01 required device fields: PASS.
- AC02 credential references only: PASS.
- AC03 audited status changes: PASS within the explicit in-memory/single-process boundary.
- AC04 tenant isolation: PASS; production-host positive path remains the tracked BIO-TEN-002 follow-up under the accepted W03 standard.
- Functional: PASS.
- Security: PASS.
- Tenant isolation: PASS.
- Offline: NOT APPLICABLE.
- Concurrency: PASS for the accepted singleton, same-process tier.
- Regression: PASS.
