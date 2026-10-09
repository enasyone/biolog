# BIO-INT-002 security review — Zainab — round 3

**Verdict: FAIL**

Reviewed remediation `6bf5a89` and evidence `f7f99fc`. The structural harness passes 58/58.
Cancellation-safe orphan recording, content-aware adoption, and the locked fresh mapping read are
implemented, but the new ordering and provenance key introduce blocking integrity defects.

## ZAINAB-INT-002-F03 — post-claim rejection leaves the claim permanently InFlight

`ApplyAsync` acquires the idempotency claim, then acquires the mapping lock and performs the fresh
Active/SyncDirection checks. The encompassing release catch begins only after those steps. A disabled
mapping, a direction changed to `biolog_is_master`, a mapping disappearance, or cancellation while
waiting for/acquiring the lock therefore throws after claim ownership without releasing it. A later
retry with the same key waits and returns 503 forever, even after an administrator re-enables the
mapping.

Required fix: once this caller owns the claim, put lock acquisition, authoritative mapping read and
all gate checks inside the same release-on-failure scope. Add disabled/direction-changed/cancelled
post-claim tests that assert the claim is absent afterward and a legitimate retry can proceed.

## ZAINAB-INT-002-F04 — provenance key can replay the wrong row or mapping

`IImportRowProvenanceLedger` is keyed only by `(tenantId, idempotencyKey, externalKey)`.

- After an attempt fails and releases its idempotency record, the same key can legitimately be used
  against another mapping. A matching external key then replays the first mapping's outcome and
  PERSON id, skipping classification and link creation for the second mapping.
- Two rows in one request may carry the same external key. After row one is recorded, row two
  immediately replays row one's result, even if its incoming identity fields disagree. The second
  row's content is never evaluated, defeating conflict detection.

Required fix: bind recovery provenance to the full attempt identity, including mapping and an
unambiguous row identity/content fingerprint (or reject duplicate external keys at validation).
Use immutable create/compare semantics rather than dictionary overwrite. Add cross-mapping key-reuse
and duplicate-key/different-content adversarial tests.

## Residual durability note

The new provenance record is explicitly best-effort. If preserving Created provenance is a required
audit invariant, silently swallowing its failure does not guarantee the stated remediation. The
recovery record must be durable/verified or the operation must fail before later effects can be
reported inconsistently.
