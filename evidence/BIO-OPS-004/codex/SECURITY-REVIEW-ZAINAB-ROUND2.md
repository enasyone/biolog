# BIO-OPS-004 Security Review — Zainab, Round 2

**Frozen builder:** `e5dda38fd33fdeac517acc7474664d86e81053bd`  
**Verdict:** PASS

## Closure of ZA-OPS004-01

The lost-wakeup race is closed.

`SessionController._syncAccessCache()` now records an overlapping online signal
in `_accessCacheSyncRerunRequested` instead of discarding it. The active caller
retains the single-flight lock and performs one coalesced rerun after the current
pull/push round finishes, provided connectivity is still online. It reloads the
trusted tenant context for each round and stops if the tenant was cleared.

The new deterministic regression holds the first revocation pull open, emits an
offline/online recovery while that pull remains active, fails the first pull, and
proves a second pull runs without a third connectivity event. The former storm
test now correctly expects one active pass plus one coalesced rerun.

There is no asynchronous yield between the loop-condition check and releasing
the in-flight flag, so another signal cannot be lost in that boundary on Dart's
single-threaded event loop.

## Threat-model results

- **Authentication / authorization bypass:** PASS. The remediation changes only
  coordinator scheduling; the access-cache-specific device credential remains
  mandatory.
- **Tenant isolation:** PASS. Every rerun reads the current trusted tenant; the
  backend independently binds the authenticated device to its tenant and rejects
  a body tenant mismatch.
- **Replay / idempotency:** PASS. Single-flight execution is preserved and event
  ID remains the server idempotency key. A repeated pass cannot double-count a
  presentation event.
- **Offline-cache abuse / failure recovery:** PASS. A recovery signal received
  during a failing request now guarantees a follow-up pass while still online.
- **Secrets / credential privacy:** PASS. No secret storage, logging, or transport
  behavior changed.
- **Race / concurrency:** PASS. Overlapping triggers are coalesced without
  concurrent pulls or pushes; sign-out stops a pending rerun.
- **Regression:** PASS based on independent orchestration replay of 137/137
  Flutter tests and clean Flutter analysis at the frozen builder. Local execution
  remains unavailable because the installed Flutter SDK attempts to write its
  sandbox-external cache.

## Category verdicts

| Category | Verdict |
|---|---|
| Functional | PASS |
| Security | PASS |
| Tenant isolation | PASS |
| Offline / recovery | PASS |
| Concurrency | PASS |
| Replay / idempotency | PASS |
| Regression | PASS |

No unresolved Critical or High security finding remains.
