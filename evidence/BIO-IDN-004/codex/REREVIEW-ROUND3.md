# BIO-IDN-004 remediation re-review — round 3

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation commit `fbc7737` at builder evidence head `f49ce40`.

## Prior finding status

Rejected suspensions no longer emit false revocations. A failed post-commit publication is now exposed as `revocation_pending_reconciliation`, and repeating `SUSPENDED -> SUSPENDED` can publish the version-keyed event idempotently.

## BIO-IDN-004-QA-01 remains High — pending reconciliation can be bypassed and forgotten

The remediation describes reconciliation as mandatory, but `TransitionAsync` permits every ordinary outgoing transition from `SUSPENDED` without first checking whether the current suspension's revocation event exists. In particular, an authorized `SUSPENDED -> ACTIVE` reactivation succeeds while revocation is pending. Once the person becomes `ACTIVE`, `IsRevocationPendingAsync` returns false because it only examines the current status/version, so the missing suspension event is no longer observable or recoverable through the reconciliation path.

The result is still an emergency suspension for which no credential/access revocation event was ever emitted. The gap is modeled only while the caller voluntarily leaves the person suspended and retries; it is not mandatory reconciliation.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-004/codex/RevocationAtomicityProbe/RevocationAtomicityProbe.csproj
```

Observed:

```text
FAIL: reactivation escaped pending reconciliation; the suspension revocation event is permanently missing
```

The probe commits a suspension while publication is unavailable, then performs a fully permissioned, reason-bearing reactivation through a recovered service. Reactivation succeeds, no revocation event exists, and the pending indicator becomes false.

Required closure: persist pending work independently of the person's current status and make reconciliation mandatory. Before any later lifecycle transition can erase the current-version signal, the service must either reconcile the missing event or reject the transition while retaining an observable, retryable pending record. A client-optional self-loop is insufficient.

## Regression evidence

- BIO-IDN-004 structural suite: 53/53 PASS, but it checks that the retry path exists, not that pending work cannot be bypassed.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Builder evidence records feature tests 67/67, integration tests 70/70, unit tests 160/160, structural tests 53/53, and quality gate PASS.

Functional FAIL; Security FAIL; Tenant Isolation PASS; Offline/Recovery FAIL; Concurrency FAIL; Regression PASS.
