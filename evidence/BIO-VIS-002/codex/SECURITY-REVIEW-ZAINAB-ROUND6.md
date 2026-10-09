# BIO-VIS-002 security re-review — Zainab round 6

## Verdict: FAIL (Medium, blocking)

### F08 — stable-ID audit deduplication is check-then-overwrite

Commit `ac00f58` fixes sequential audit retry, but not concurrent reconciliation. `AuditAsync` performs `ExistsAsync(eventId)` followed by `WriteAsync(event)`. The production sink writes with `_byEventId[eventId] = auditEvent`, which overwrites an existing payload rather than atomically inserting once.

Reconciliation triggers do not all share the tenant/PERSON lock: the operator/grant paths do, while checkout/revoke acquire visitor-record locks. Two callers can therefore obtain the same stable event ID, both observe it absent, build events with different actor/correlation attribution, and both write. The second write silently replaces the first append-only audit event. The record count stays one, but original security attribution is lost.

**Required fix:** make audit creation atomic and immutable, such as `TryAdd`/create-if-absent returning the stored event and rejecting conflicting payloads, or serialize all reconciliation triggers under the same tenant/PERSON lock. Add a barrier-based concurrency test proving two callers cannot overwrite the first durable payload and that resolution occurs only after the immutable audit exists.

Sequential F07 convergence passes. Structural checks pass 79/79 but do not test this race. This agrees with Seyi’s concurrent round-8 finding.
