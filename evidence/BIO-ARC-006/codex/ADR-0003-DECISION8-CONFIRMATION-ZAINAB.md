# ADR-0003 Decision 8 — Independent Confirmation — Zainab

**Verdict:** CONFIRMED  
**Content commit:** `7435abf`  
**Mechanism/snapshot commit:** `6d6ab93`

Compared Decision 8 directly with Seyi's `SY-OPS002-05` finding in `evidence/features/bio-ops-002/codex/REVIEW-Seyi-round3.md`.

The text accurately records the reopened Decision 7 boundary: the browser/Next.js session effect survives an API restart while the API's process-local in-memory audit record does not. It does not claim that the mismatch is technically closed. Instead, it makes a narrow architecture exception for the named synthetic demo-session context-change mechanism and states the three material constraints:

- no authorization decision depends on the record's presence;
- the audited token has a bounded expiry;
- the audited action is the already-approved demo self-selection rather than a new privilege boundary.

The non-precedent language is explicit. A future real credential, tenant grant, authorization-relevant record, or other non-synthetic effect must use a shared transaction/outbox or an attempt-then-finalize recovery protocol, or obtain its own separately justified exception. This preserves Decision 7's general rule.

The historical reference to `InMemoryDemoSessionContextAuditSink` identifies the concrete sink Seyi reviewed; the later audit-shape remediation replaced it with the shared `InMemoryAuditSink` without changing the relevant process-local volatility. The reasoning therefore remains current.

`scripts/check-contract-freeze.py` passes for snapshot `1.1.11` (39 artifacts, 83 future stories).

Confirmation of the ADR text is not evidence that unrelated BIO-OPS-002 acceptance criteria pass; those remain governed by their own implementation and review evidence.
