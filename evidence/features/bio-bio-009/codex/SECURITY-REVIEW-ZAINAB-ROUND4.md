# BIO-BIO-009 security review — round 4

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** remediation `afd48be`, evidence `2547c41`

## Closed from round 3

- ZB-BIO009-07: provenance secrets shorter than 32 characters are treated as unconfigured; absent/short proofs fail closed.
- ZB-BIO009-08: the registered cleanup store is file-backed with atomic replacement and survives reconstruction over the same path. A deletion exception now re-reads source ownership; only confirmed continued existence removes the speculative obligation, while confirmed deletion or verification failure preserves it.
- Retention controls now clear loading state and show an accessible error on live-adapter failure.

## Blocking finding

### ZB-BIO009-09 — High — AC05's LIVE human workflow still cannot perform either action

The new opt-in Playwright test confirms the original finding rather than closing it: both real LIVE actions reach the backend and receive `400 actor_required` because the browser sends no trusted identity. Policy update and deletion therefore cannot succeed from the mapped `/fingerprints` control surface.

AC05 explicitly requires the approved web surface to be connected to the live implementation and the mapped end-to-end human workflow to pass. A correctly rejected unauthenticated request is useful negative-path evidence, but it is not a passing authorised workflow. Labelling the limitation HYBRID or cross-cutting does not satisfy that criterion and does not make the visible LIVE controls operational.

Required: complete TD-WEB-PROVENANCE-BRIDGE (or another approved trusted-edge session mechanism) so server-side infrastructure injects verified actor identity/provenance without exposing the shared HMAC secret to browser JavaScript. Then prove, against real web and API servers, an authorised policy update and deletion request succeed, while an unprivileged session is denied.

## Verification

- Reviewed the provenance guard, file-backed store, ambiguous-delete verification, production registration, UI error recovery, and opt-in LIVE Playwright test.
- Builder evidence reports BIO-009 42/42, integration 202/202, shared unit 200/200, frontend unit 473/473, simulation Playwright 4/4, LIVE Playwright 2/2.
- The LIVE 2/2 assertions are negative-path assertions for `actor_required`; they do not prove AC05's required successful workflow.

## Non-blocking operational note

The file store's `SemaphoreSlim` protects one store instance only. Multiple processes sharing the configured path can race read-modify-write and lose an obligation. This is acceptable only for the documented single-process tier; a multi-process deployment needs shared storage or an inter-process lock.
