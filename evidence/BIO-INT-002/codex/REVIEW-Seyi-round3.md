# BIO-INT-002 round-three review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed `6bf5a89` / `f7f99fc`.

## Blocking finding F04 — the fresh mapping guard strands its idempotency claim

`ApplyAsync` wins and persists the idempotency claim before acquiring the per-mapping lock. The fresh mapping read and its `BiologIsMaster` / `Active` guards then run at lines 210–225, but the claim-release `try/catch` does not begin until line 235.

Consequently, the intended concurrent-disable outcome throws `import_mapping_disabled` while leaving the key permanently `InFlight`. If the administrator re-enables the mapping and the client retries the same logical request with the same idempotency key, it cannot proceed or replay; it waits and returns `idempotency_wait_timeout`. Cancellation or another failure while acquiring the lock/fresh mapping produces the same stranded-claim class.

The new TOCTOU test proves the disable wins but does not inspect or retry the claimed key. The separate re-enable test never performs a failed apply before re-enabling, so it misses the regression.

Required: once this caller owns the claim, cover lock acquisition and all authoritative mapping checks with release-on-failure, and add a deterministic disable-then-re-enable same-key retry regression.

## Other round-two findings

- Orphan recording now uses `CancellationToken.None` with ambiguous-write verification: fixed.
- Orphan adoption compares current PERSON fields and surfaces disagreement as conflict: fixed.
- Row provenance replay and the fresh post-lock mapping read are present.

Independent structural harness: 58 passed, 0 failed; it does not detect F04.
