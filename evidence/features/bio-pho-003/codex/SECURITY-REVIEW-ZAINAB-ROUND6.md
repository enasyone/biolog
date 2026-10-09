# BIO-PHO-003 Security Review — Zainab — Round 6

**Verdict:** PASS  
**Reviewed builder:** `c5a0791`  
**Remediation:** `03f4cec`

## Scope

Re-reviewed the round-five promotion-identity defect, with emphasis on tenant isolation, replay, concurrency, actor attribution, and the required failed-A / unrelated-B / retry-A interleaving.

## Findings

No blocking security finding remains.

- `PendingApprovalAttemptId` is claimed before promotion as a tenant-scoped local idempotency key.
- `ConcurrentDictionary.GetOrAdd` plus reference identity gives exactly one claim owner; claim losers cannot call `PromoteAsync`.
- A completed claim stores and returns only that attempt's master version, with no external-state inference or second promotion.
- A failed promotion releases A's claim. An unrelated promotion B cannot create or complete A's tenant/operation/attempt key. Retry A therefore wins a new claim and performs its own promotion; it cannot adopt B merely because B used the next version.
- The original initiating actor remains in the pending capture and is used for resumed audit attribution.
- Claim scope includes tenant ID, operation, and server-generated attempt ID using an injective JSON-array key, preventing cross-tenant or delimiter-collision reuse.
- The former audit/version/content reconstruction paths and expanded gateway reads are removed.
- The accepted volatile-store residual fails closed/loud: an in-flight claim with no completion times out and does not re-promote or infer another result.

## Verification

- Inspected `02c6c9c..c5a0791`, `PhotoQualityService`, `InMemoryIdempotencyStore`, gateway wiring, fakes, and regression coverage.
- Offline structural suite: **55/55 PASS**.
- Local `dotnet test tests/features/bio-pho-003/Biolog.Features.PhotoQuality.Tests.csproj` could not start because the managed sandbox denied the test platform's loopback socket (`SocketException (13): Permission denied`). Builder evidence records **39/39 PASS** for the same suite.

## Residual boundary

The store is process-local and volatile. Within the currently accepted in-memory tier, a process-level failure after promotion but before claim completion cannot be reconciled; an extant in-flight claim times out rather than silently duplicating or misattributing a promotion. Durable cross-process idempotency belongs with the planned persistent-storage boundary.
