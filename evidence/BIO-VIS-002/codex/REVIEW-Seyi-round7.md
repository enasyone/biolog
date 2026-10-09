# BIO-VIS-002 independent re-review — round 8

Verdict: **FAIL**

Reviewed implementation `ac00f58` and builder evidence `ec0276c` against the F07 audit-convergence remediation.

## Passing checks

- The sequential F07 case is fixed: every retry obtains the same ledger-owned event identifier, attempts or verifies the audit before resolving, and leaves the ledger unresolved after a genuine audit failure.
- F06 remains fail-closed: unresolved stranded cards prevent another card issue.
- The dedicated operator endpoint remains permission/provenance-gated and tenant-scoped.
- `bash tests/features/bio-vis-002/BIO-VIS-002.test.sh`: **79 passed, 0 failed**.

## Blocking finding F08 — stable identifier deduplication is not atomic

`AuditAsync` implements check-then-write: `ExistsAsync(eventId)` followed later by `WriteAsync(event)`. The production in-memory sink implements `WriteAsync` as dictionary assignment (`_byEventId[eventId] = event`), not insert-once or compare-and-confirm.

Reconciliation is not protected by one common lock. Grant and the operator endpoint use the tenant/person lock, while checkout and revoke enter with visitor-record identifiers. Therefore two legitimate calls for the same person and stranded card can run this sequence:

1. Operator reconciliation and checkout reconciliation obtain the same stable event identifier.
2. Both observe `ExistsAsync == false`.
3. Both build audit events with different actor/correlation attribution.
4. Both call `WriteAsync`; the later dictionary assignment silently replaces the earlier event.
5. Both may mark the ledger resolved.

The count remains one, but the supposedly append-only audit record is mutable and its original actor/correlation can be lost. The new failure-injection test is sequential and cannot expose this race. This blocks AC05 audit integrity and the requested retry/concurrency category.

## Required remediation

Make stable-event persistence atomic and immutable (for example, an audit-sink create-if-absent operation that returns the stored event, treating an identical existing event as success and conflicting content as an error), or serialize every reconciliation trigger with the same tenant/person lock without nested-lock deadlock. Add a barrier-based test in which two callers both pass the pre-check before either write, then prove exactly one immutable payload is retained and the ledger resolves only after that durable result.

## Execution note

The structural harness passed locally. Builder evidence reports unit **43/43**, BIO-VIS-001 regression **25/25**, and integration **192/192**. The workspace previously allowed compilation but blocks Visual Studio test execution sockets; this finding follows directly from the production lock and sink implementations.
