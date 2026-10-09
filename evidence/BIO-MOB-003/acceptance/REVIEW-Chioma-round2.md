# BIO-MOB-003 acceptance audit — round 2

## Verdict: FAIL

The central intake remediation closes the original system-boundary gap and Seyi's later
audit-before-commit finding. Backend feature tests, host-channel tests, structural checks, builder
evidence, and Seyi round-five review establish central addition/update/revocation, replay,
changed-fingerprint rejection, conflict queuing, tenant-claim rejection, race handling, and audit
failure recovery.

Two linked production-boundary defects still block acceptance.

### BIO-MOB-003-QA-01 — production client cannot authenticate to its central endpoint

Severity: High. Categories: Functional, Security, Offline, Regression. AC01/AC05.

`HttpSyncTransport.upload()` sends only `Content-Type` and `Idempotency-Key`. The new central
endpoint requires `X-Biolog-Device-Id` and `X-Biolog-Device-Secret`; missing credentials return
generic 401. Production `SessionController` constructs this uncredentialed transport. The real host
tests manually add credentials and therefore do not exercise the shipped client-to-server path.
Every production reconnect consequently fails authentication and no queued delta can synchronize.

### BIO-MOB-003-QA-02 — authenticated device identity is not bound to the envelope

Severity: High. Categories: Security, Data integrity, Tenant isolation. AC02/AC03.

The service cross-validates `tenant_id` but never checks `request.DeviceId` against the authenticated
`device.DeviceId`. It also omits both device ID and tenant ID from the idempotency fingerprint. An
authenticated device can therefore submit another device's canonical ID as the mutation's claimed
origin, and changing that claim under the same key is treated as the same request/replay rather than
`idempotency_key_reuse`. Audit attribution uses the real caller, but the accepted envelope is not
validated against it, leaving contradictory provenance at the central boundary.

Reproduction: `bash evidence/BIO-MOB-003/acceptance/round2-boundary-probe.sh` — three failures.

## Required remediation

1. Provision the production client with device credentials and send both required authentication
   headers through the real `SessionController` path without persisting/logging the raw secret.
2. Cross-validate body `device_id` against the authenticated device before idempotency claim.
3. Bind all identity-bearing request claims, including tenant and device, into the request
   fingerprint and add changed-claim reuse regressions.
4. Add a genuine mobile-transport-to-central-host integration test, not a host test that manually
   constructs headers independently of `HttpSyncTransport`.

