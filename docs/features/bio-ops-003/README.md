# BIO-OPS-003 — card issue-replace-produce local acceptance

Status: **PASS — production success and replacement activation are composed and serialized.**

The accepted dependency features each enforce only one half of the required journey:

- BIO-CRD-007 `ActivateAfterProductionAsync` requires the latest production job to pass, then delegates directly to BIO-CRD-005 activation. It cannot revoke/supersede Card 001, so Card 002 activation fails while Card 001 remains active.
- BIO-CRD-006 `ActivateReplacementAsync` permanently revokes earlier cards and activates Card 002, but never reads BIO-CRD-007 production state. It can therefore activate Card 002 after failed production or with no production job.

No approved endpoint enforces both preconditions in one transaction. AC03–AC05 cannot all be true in one complete issue-report-replace-produce journey.

See `evidence/features/bio-ops-003/builder/BLOCKED.md`.

## Round 2 re-review

The CRD-006 gateway added in `bbcd6e1` closes the sequential gap, but not the concurrent one.
`ActivateReplacementAsync` reads the latest CRD-007 job through `HasPassedAsync` while holding
only `CardLifecycleLockRegistry`. CRD-007 production mutations use their separate
`ProductionJobLockRegistry`. A concurrent reprint can therefore become the latest
`InProgress`/`Failed` job after the gateway returns `Passed` and before CRD-006 calls the card
activation gateway. The replacement then activates against a stale production result.

At that review point AC04 remained blocked pending shared per-card serialization.

## Round 3 closure

Commits `7b79866` and `c6152ea` close the concurrent gap with one host-level
`CardActivationBarrierLockRegistry`. CRD-006 holds the barrier from the production-status read
through replacement activation; every CRD-007 production mutation holds the same per-card
barrier. The lock ordering is consistent and introduces no cross-feature lock cycle.

Independent re-review passed AC03–AC05. See
`evidence/features/bio-ops-003/builder/ROUND3-PASS.md`.
