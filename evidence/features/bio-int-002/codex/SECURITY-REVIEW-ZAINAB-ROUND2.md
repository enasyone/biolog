# BIO-INT-002 security review — Zainab — round 2

**Verdict: FAIL**

Reviewed remediation `a832c6d` directly. The structural harness passes 49/49. The exclusive
idempotency owner/wait loop is correct, and inactive mappings are rejected in ordinary sequential
calls. F01 and F02 are not fully closed under failure and concurrency.

## ZAINAB-INT-002-F01 remains open — recovery is neither cancellation-safe nor outcome-preserving

After `personGateway.CreateAsync` returns a durable PERSON, `orphanedPersonLedger.RecordAsync` uses
the caller's cancellation token. Cancellation in that boundary makes the ledger write throw before
the link is created. The catch releases the idempotency claim; a retry finds neither ledger entry
nor link and creates a second PERSON. This is the original duplicate-identity failure under a common
failure mode.

The ledger also does not resume the full operation after later failure. If PERSON and link creation
succeed but the batch audit fails, the claim is released. On retry, `ClassifyRowAsync` sees the link
and reports `Unchanged`; the new batch/audit no longer records that this import created the PERSON.
Thus the durable side effect and its batch/audit outcome diverge. The existing audit-failure test
asserts only claim release and person count, so it accepts this loss of provenance.

Required fix: once PERSON creation succeeds, persist recovery state with an independent token and
carry enough durable state to resume the original row/batch outcome through audit, batch persistence,
and idempotency completion. Add cancellation immediately after PERSON creation and audit-failure
recovery tests that assert the final batch/audit still records one `Created` row and the same PERSON.

## ZAINAB-INT-002-F02 remains open — disable can race with apply

`ApplyAsync` reads and checks `mapping.Active` before acquiring the per-mapping lock. Mapping update
uses that same lock. An apply can read Active=true, pause, an administrator can disable the mapping,
then apply acquires the lock and creates PERSON records using its stale mapping snapshot.

Required fix: acquire the mapping lock before the authoritative mapping read/check, or re-read and
revalidate Active and SyncDirection after acquiring it. Add a barrier-controlled disable-versus-
apply test proving no write begins after disable commits.
