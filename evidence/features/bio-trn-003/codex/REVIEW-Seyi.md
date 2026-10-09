# BIO-TRN-003 independent review — Seyi

Verdict: **FAIL**

Reviewed builder evidence commit `100ee4b` against AC01–AC05.

## SY-TRN003-01 — High — stale concurrent rule updates both commit

`TransportNotificationRuleService.SetEnabledAsync` reads a rule and checks the caller's expected version before calling `UpdateAsync`, but `InMemoryTransportNotificationRuleRepository.UpdateAsync` is a blind dictionary assignment. The read/check/write sequence has no per-rule lock and the repository does not perform compare-and-swap.

Two administrators can therefore both read version 1, both pass the version check, and both commit version 2. One acknowledged configuration change is silently overwritten, despite the endpoint exposing `version` specifically to reject stale writes. This affects notification delivery configuration and fails the required concurrency/data-integrity review category.

Reproduction: `tests/features/bio-trn-003/SeyiRuleConcurrencyProbe.cs` coordinates two reads of the same version before either update. Both writes commit; the probe expects one update and observes two.

```text
SeyiRuleConcurrencyProbe.Concurrent_stale_rule_updates_must_not_both_commit [FAIL]
Expected: 1
Actual:   2
```

Required correction: make rule replacement atomic against the expected stored version (repository compare-and-swap), or hold a shared per-rule lock across read/check/write. Preserve a deterministic concurrent regression test against the corrected path.

## Other categories

- Functional: PASS for event-type matching, minimized payload projection, bounded result-based retry/dead-letter, and read-only transport feed design.
- Security / tenant isolation: PASS by static trace through provenance, RBAC, tenant-scoped repositories, and gateway calls.
- UI / regression: no separate blocker identified in the reviewed diff.
- Concurrency / data integrity: **FAIL** due to SY-TRN003-01.

## Execution

- Baseline direct xUnit execution: **19/19 passed**.
- With the independent concurrency probe: **19 passed, 1 failed**.
- VSTest could not bind its local socket in the sandbox; direct xUnit v3 execution was used.
