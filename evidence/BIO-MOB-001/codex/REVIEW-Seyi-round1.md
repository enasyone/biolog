# BIO-MOB-001 independent review — Seyi

Date: 2026-09-23
Verdict: FAIL

## Finding BIO-MOB-001-F01 — offline queue silently replaces an existing idempotency key (High)

`SqlCipherAppDatabase.enqueueMutation` inserts the mutation with
`ConflictAlgorithm.replace`, while the schema enforces a unique `idempotency_key`. A retry or a
malicious second mutation using the same key therefore deletes/replaces the original queued
envelope instead of preserving the first request or rejecting a content mismatch. This breaks
offline idempotency and can cause the device to sync a different payload than the operator first
queued.

### Reproduction

1. Enqueue mutation A with idempotency key `k` and payload A.
2. Enqueue mutation B with the same key `k` but a different payload.
3. Query the queue: `ConflictAlgorithm.replace` leaves B, silently discarding A.

The queue must retain the original envelope for an identical replay and reject (or surface a
conflict for) reuse with different content; it must not replace by key.

## Checks

- Foundation suite: 29 passed, 0 failed.
- Source review confirms SQLCipher/Keystore passphrase handling, explicit tenant binding, queue
  schema, offline visibility, and Android shell scaffolding otherwise meet the stated criteria.

Category verdicts: Functional FAIL (offline idempotency); Security FAIL (payload substitution); Tenant Isolation PASS; Offline FAIL; Concurrency FAIL (racing queue writes can replace); Regression FAIL pending mitigation.
