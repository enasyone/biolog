# BIO-OPS-003 round 2 — BLOCKED

Reviewed remediation `bbcd6e1` at merged worktree head `c5b2a27`.

## Finding SY-OPS003-02 — High

AC04 still has a time-of-check/time-of-use race. CRD-006 calls
`IProductionStatusGateway.HasPassedAsync` while holding `CardLifecycleLockRegistry`, then later
revokes earlier cards and calls `ICardIssueGateway.ActivateAsync`. CRD-007 creates, updates,
rejects, and cancels production jobs under a different `ProductionJobLockRegistry`.

Reproduction:

1. Card 002's latest production job is `Passed`.
2. Start `ActivateReplacementAsync`; let `HasPassedAsync` return true.
3. Before its later card activation call, concurrently create a reprint job for Card 002 (latest
   status becomes `InProgress`) and fail it (latest status becomes `Failed`).
4. CRD-006 continues from the stale snapshot and activates Card 002.

The real adapter is a read-only `ListJobsAsync` projection and neither the adapter nor CRD-006
shares CRD-007's per-card production lock. Thus a production failure can coexist with a newly
activated replacement, violating AC04.

## Checks

- Sequential no-job/failed gate: PASS.
- Sequential passed-production replacement and permanent revocation: PASS.
- Idempotency claim release after precondition failure: PASS by source/test trace.
- Atomic production predicate through activation: FAIL.
- Independent acceptance harness: 3 PASS, 1 FAIL.

Required fix: perform production validation and replacement activation within one shared per-card
serialization boundary, or expose a CRD-007-owned atomic coordinator that performs the lifecycle
replacement operation while its production lock is held. Add a barrier-controlled concurrency
regression where a reprint/failure races replacement activation.
