# BIO-INT-002 independent re-review — round 2

Verdict: **FAIL**

Reviewed consolidated remediation `a832c6d` directly.

## Passing remediation

- F01 claim ownership/wait/replay: PASS. Only the winning `IsNewClaim` caller executes; losers poll for completion/release with a bounded 503 timeout.
- F01 claim scope: PASS. The release guard now covers row processing, audit and batch persistence.
- F02: PASS. Disabled mappings reject preview and apply with a stable 409 response.
- Structural harness: **49 passed, 0 failed**.

## Blocking F03 — orphan adoption is cancellation-sensitive and content-blind

The new backstop does not fully prevent duplicate or mismatched PERSON linkage.

### Cancellation window

After `personGateway.CreateAsync` returns a durable PERSON, `orphanedPersonLedger.RecordAsync` is called with the original request cancellation token. If that token is cancelled after PERSON creation but before/during the ledger write, `RecordAsync` throws, the claim is released, and no link or orphan record identifies the created PERSON. A retry classifies the row as new and creates a duplicate PERSON—the exact failure the ledger is intended to prevent.

Once PERSON creation may have succeeded, recording the recovery identity must use an independent token (`CancellationToken.None`) and ambiguous completion must be verified where applicable. Add a cancellation-after-create test proving a retry never mints a second PERSON.

### Changed-content adoption

The ledger stores only `PersonId`, keyed by tenant/mapping/external key. A later retry may use a new idempotency key and changed identity fields. `ApplyAsync` adopts the orphaned `PersonId` without reading the PERSON or comparing incoming fields, creates the link, and leaves the outcome as `created`. Example: failed attempt creates “Ada Lovelace”; later request for the same external key says “Ada Byron”; adoption links the Lovelace PERSON without returning a `last_name` conflict.

The orphan record must bind enough original content to validate adoption, or adoption must fetch the PERSON and run the same field-conflict comparison before linking. A disagreement must remain unlinked and surface as `conflict`; it must never be reported as a clean creation.

## Evidence context

Builder evidence reports unit **28/28** and integration **192/192**. Current tests cover a same-content retry and ordinary injected failures, but not cancellation after PERSON creation or changed-content orphan adoption.
