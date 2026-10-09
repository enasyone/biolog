# BIO-MOB-002 independent review — round 1

## Verdict: FAIL

### Finding QA-01 — state transitions are not guarded

`SqlCipherAppDatabase.markUploading` updates by `mutation_id` only, without requiring the current state to be `pending`. A mutation already in terminal `failed` or human-owned `conflict` state can therefore be called by an uploader and resurrected to `uploading`, bypassing the bounded-retry and conflict-resolution guarantees. The same unguarded update pattern makes stale workers able to alter queue state after another worker has moved it on.

Reproduction: enqueue a mutation, call `recordConflict(id)` (or exhaust retries to `failed`), then call `markUploading(id)`. The row becomes `uploading`; `conflictedMutations` no longer returns it and the terminal state is lost. AC02 says `markUploading` transitions pending -> uploading, not arbitrary states -> uploading.

Additionally, `recordUploadFailure` performs an untransactional read-then-write. Two concurrent failures can both read the same retry count and write the same increment, undercounting attempts and weakening the retry bound.

Category verdicts: Functional FAIL; Security PASS; Tenant isolation PASS; Offline FAIL; Concurrency FAIL; Regression FAIL.

Foundation harness: 15 passed, 0 failed; it does not exercise invalid state transitions or concurrent failure updates.
