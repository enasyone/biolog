# BIO-OPS-004 Security Review — Zainab

**Frozen builder:** `b786bbbea02a732368c7d1d78cf032538aa0c9b9`  
**Verdict:** FAIL  
**Blocking finding:** `ZA-OPS004-01` — High

## Finding ZA-OPS004-01 — reconnect during an in-flight failure is lost

`SessionController._syncAccessCache()` treats every online signal received while
`_accessCacheSyncInFlight` is true as a permanent no-op. It does not remember that
another pass is required after the current pass completes.

This creates a security-relevant lost-wakeup race:

1. The device reports online and starts a revocation pull.
2. The network drops while the HTTP request is pending.
3. The network returns and emits the only new online signal before that request
   fails or reaches its 30-second timeout.
4. The second signal returns immediately because `_accessCacheSyncInFlight` is
   true.
5. The original pull and/or push fails. The coordinator clears the flag but does
   not rerun, even though the device is now online.

The device can then remain connected indefinitely while its revocation cursor is
stale and presentation events remain queued. A credential revoked during that
interval can continue through the cached-verification path until some unrelated
future connectivity transition occurs. This violates the remediation requirement
to retain failures for retry and blocks AC03/AC05 in the adversarial reconnect
case.

The new reconnect-storm test asserts only that two overlapping online signals
produce one pass. It scripts a successful first pass, so it codifies suppression
without testing the failure/recovery interleaving above.

### Required remediation

Coalesce overlapping triggers instead of dropping them. Record a pending rerun
while a pass is active and, after the active pass finishes, run one more pass if
connectivity is still online and a trusted tenant remains bound. Add a deterministic
test whose first pull is held open, whose offline/online transition occurs while it
is open, and whose first pull then fails; prove a second pull runs without requiring
a third connectivity transition. Preserve single-flight execution.

## Other threat-model results

- **Tenant isolation:** PASS for this delta. The client uses only the trusted
  restored tenant context, while the server independently binds the device secret
  to a tenant and rejects a body tenant mismatch.
- **Authentication / RBAC bypass:** PASS for the changed path. Device ID and the
  access-cache-specific secret are sent; no caller-controlled tenant is introduced.
- **Replay / idempotency:** PASS. Presentation event ID remains the idempotency key,
  and local rows are marked synced only after HTTP 200/201.
- **Secret and credential privacy:** PASS. No new logging or persistence of the
  device secret was added.
- **Concurrency / offline-cache abuse:** FAIL due to `ZA-OPS004-01`.
- **Regression:** Existing focused test execution was attempted but the installed
  Flutter SDK tried to write `/opt/homebrew/share/flutter/bin/cache`, which is
  outside this sandbox. God independently reported 136/136 tests and clean analysis
  on this exact builder commit; that success does not exercise the lost-wakeup race.

## Category verdicts

| Category | Verdict |
|---|---|
| Functional | FAIL |
| Security | FAIL |
| Tenant isolation | PASS |
| Offline / recovery | FAIL |
| Concurrency | FAIL |
| Replay / idempotency | PASS |
| Regression | PASS with execution caveat above |

