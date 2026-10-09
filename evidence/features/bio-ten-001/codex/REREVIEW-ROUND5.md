# BIO-TEN-001 Codex re-review — round 5

## Verdict

PASS.

The round-4 implementation closes the reproducible same-process concurrency defect. The remaining crash/acknowledgement atomicity gap is inert for the concrete accepted tier: `InMemoryTenantAuditSink.WriteAsync` cannot append and then throw, and both singleton in-memory stores disappear on process death. This is a scope boundary, not a general guarantee of `ITenantAuditSink`; it must be reopened before either side becomes durable, remote, independently transactional, or shared across processes.

## Independent checks

- `dotnet tests/features/bio-ten-001/bin/Debug/net10.0/Biolog.Features.TenantRegistry.Tests.dll`: 50 passed, 0 failed.
- `bash evidence/features/bio-ten-001/codex/independent-review.sh`: PASS. The probe forces writer A to yield inside audit, starts writer B with the same observed version against the production-shaped singleton `TenantService`, verifies B cannot cross the tenant lock, then verifies exactly one commit and one matching `ALLOWED` audit.
- The corrected builder concurrency test seeds through a separate plain sink, so its delay is no longer consumed during setup.

## Acceptance criteria and categories

- AC01 control-plane CRUD: PASS.
- AC02 no operational person/credential fields: PASS.
- AC03 active/suspended/disabled lifecycle: PASS.
- AC04 administrative audit events: PASS within the explicit in-memory/single-process boundary above.
- Functional: PASS.
- Security: PASS (RBAC remains a documented later-story dependency).
- Tenant isolation: PASS / control-plane scoped.
- Offline: NOT APPLICABLE.
- Concurrency: PASS for the accepted singleton, same-process tier.
- Regression: PASS.

## Non-blocking forward obligation

The `ITenantAuditSink` documentation calls the abstraction durable while the current implementation is volatile. `ExistsAsync` also cannot make two independently durable stores atomic if verification itself fails. A persistence/distribution story must replace the current boundary with a shared transaction/outbox or another explicitly modeled recovery protocol before claiming durable atomicity.
