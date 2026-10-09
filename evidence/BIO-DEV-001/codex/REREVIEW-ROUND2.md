# BIO-DEV-001 remediation re-review — round 2

**Verdict: FAIL**

Reviewed remediation `de57aaa` at evidence head `7bc756f`.

## Closed findings

- QA-01 is accepted under the W03 tenant-resolution standard: the real host fails closed, feature behavior is proven with an explicitly injected context, BIO-TEN-002 is named as the blocking dependency, and the real verified-host positive-path follow-up is tracked.
- The earlier commit-then-rollback race is removed. The singleton `DeviceLockRegistry` correctly serializes same-device service writers for the current single-process tier.

The builder concurrency test does not itself prove this: both operations use synchronously completing in-memory dependencies, so the first call can finish before the second is invoked. This is an evidence defect, but independent source inspection confirms the lock is held across read, audit, and commit.

## QA-03 — cancellation after audit leaves a false-success event (High)

`ChangeStatusAsync` records `Outcome = Allowed / status_updated`, then calls `InMemoryDeviceRepository.UpdateStatusAsync` with the request cancellation token. The repository calls `ThrowIfCancellationRequested()` before its compare-and-set. If cancellation arrives after the audit write but before that check, the repository never changes, the caller receives `OperationCanceledException`, but the audit queue permanently contains a success event for the rejected change.

This is not a future durable-store hypothesis. It uses the shipped `InMemoryAuditSink` and `InMemoryDeviceRepository`. The deterministic probe cancels at the audit sink's existing logging point, after enqueue and before control returns to the repository call.

Reproduce:

```sh
dotnet run --project evidence/BIO-DEV-001/codex/CancellationAtomicityProbe/CancellationAtomicityProbe.csproj
```

Expected current output and exit: `FAIL cancellation after audit leaves ALLOWED status_updated for an uncommitted change`, exit 1.

## Categories

- Functional: FAIL
- Security: FAIL
- Tenant isolation: PASS
- Offline/recovery: FAIL
- Concurrency: FAIL
- Regression: FAIL

AC03 remains blocked. The audit and commit sequence must not allow caller cancellation to reject the repository write after a final success event has been recorded.
