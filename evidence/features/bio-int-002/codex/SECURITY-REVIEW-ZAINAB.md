# BIO-INT-002 security review — Zainab — round 1

**Verdict: FAIL**

Reviewed implementation `01728c0`, frontend regression fix `a9ea1ef`, and evidence `68ae7ab`.
The structural harness passes 33/33. The AC04 decision to detect and expose conflicts without
resolving them is acceptable for this story: existing PERSON profile data is never silently
overwritten, and adding a profile mutator belongs to the PERSON aggregate owner.

## Blocking findings

### ZAINAB-INT-002-F01 — Idempotency claim does not provide exclusive execution

`InMemoryIdempotencyStore.Claim` returns an existing `InFlight` record, but
`ImportBatchService.ApplyAsync` only branches on `Completed`. A second request with the same
tenant, idempotency key, and fingerprint therefore proceeds into the import instead of waiting,
rejecting, or replaying. The per-mapping lock serializes the two requests but does not repair the
logic: after acquiring it, the second request does not re-check the idempotency record and can
execute again.

This also combines with non-atomic PERSON/link creation. `CreateAsync` persists a PERSON before
the external identity link. If link creation is cancelled or fails, the catch block releases the
idempotency claim even though a durable PERSON has already been created. A retry sees no link and
creates another PERSON. Audit or batch persistence failures also release a claim after row writes
have occurred, contradicting the code comment that “nothing durable happened.”

Impact: concurrent delivery or a transient failure can violate AC05, create orphaned or duplicate
identity records, and produce an import result that does not faithfully represent earlier writes.

Required fix: make the idempotency claim an exclusive owner token and define deterministic handling
of an existing in-flight claim; re-check after lock acquisition if the lock is part of the design.
Make PERSON creation, link creation, batch persistence, audit, and idempotency completion atomic, or
introduce a durable recovery state that resumes from the created PERSON without creating another.
Add concurrency and injected-failure tests for every boundary after PERSON creation.

### ZAINAB-INT-002-F02 — Disabled mappings still accept preview and apply

`ImportSourceMapping.Active` can be set false, but neither `PreviewAsync` nor `ApplyAsync` checks it.
An actor retaining the apply permission can therefore continue importing through a mapping that an
administrator disabled.

Impact: the administrative kill switch is ineffective and stale or compromised integrations remain
able to create PERSON records.

Required fix: reject preview and apply for inactive mappings, using a stable error code, and cover
both paths with tests.

## Non-blocking design note

Conflict detection is limited to an already-linked external key. A new external key always creates
a PERSON even if its identity fields match an existing PERSON. That behavior should be explicitly
accepted as the intended identity-matching policy or addressed by a later, governed reconciliation
workflow; it must not be described as proof that the new identity is “uncontested.”
