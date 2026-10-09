# BIO-INT-002 independent review — round 1

Verdict: **FAIL**

Reviewed implementation `01728c0`, regression fix `a9ea1ef`, and builder evidence `68ae7ab` against BIO-INT-002 AC01–AC06.

## Blocking finding F01 — concurrent identical idempotency claims both execute

AC05 fails for two concurrent `ApplyAsync` requests using the same tenant, operation, idempotency key, mapping and rows.

`InMemoryIdempotencyStore.Claim` correctly returns one shared `InFlight` record, but `ApplyAsync` handles only fingerprint mismatch and `Completed`. An existing matching `InFlight` record falls through as though the caller owned a new claim. The mapping lock is acquired only afterward and does not repair the claim state.

Reproducible interleaving:

1. Request A claims key K and receives `InFlight`.
2. Request B claims K before A completes and receives the same `InFlight` record.
3. A acquires the mapping lock; B waits.
4. A creates the PERSON/link and batch, completes K with batch A, and releases the lock.
5. B acquires the lock but never re-reads K. It reprocesses the request, classifies the now-linked row as `unchanged`, creates batch B, and calls `Complete`, replacing K's stored result.

Both callers report `Replayed=false`, receive different batch identifiers/outcomes, and the durable replay result changes from batch A to batch B. This violates AC05 idempotent reprocessing and makes audit/batch history depend on timing.

Required remediation: distinguish claim ownership from an already-in-flight claim. A duplicate matching request must wait for or replay the original completion, or return a stable in-progress response without executing. Re-checking the claim after acquiring the mapping lock is sufficient for this in-process design if completion/release transitions are ownership-safe. Add a deterministic two-caller test proving one execution, one stable batch identifier/result, and no second audit/batch write.

## AC04 assessment

PASS. Conflict detection without resolution is acceptable under the approved criterion. Existing PERSON fields are never mutated; disagreements are returned as field-level conflicts in preview/apply and rendered by the live integrations UI. Adding a BIO-IDN-001 profile mutator would be cross-story scope and is not required merely to satisfy “do not silently overwrite.” The current no-write guard is stronger than an implicit resolution path.

## Other category results

- Functional AC01–AC04: PASS apart from the AC05 blocker above.
- Security/tenant isolation: PASS by source inspection; mutation endpoints require permission and provenance, and repository/link/idempotency keys are tenant-scoped.
- UI/regression: PASS by inspection. `/administration/integrations` is live and conflict-visible; the additive adapter preserves BIO-INT-001's original primary action. `/settings/authentication` is explicitly documented HYBRID.
- Structural harness: `bash tests/features/bio-int-002/BIO-INT-002.test.sh` — **33 passed, 0 failed**.
- Builder evidence reports feature unit **20/20**, BIO-IDN-001 regression **70/70**, integration **192/192**, and clean web typecheck. No existing test covers an already-in-flight matching claim.
