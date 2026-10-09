# BIO-DEV-004 independent review — Seyi

Verdict: **FAIL**

Reviewed implementation `a3ccb70` and builder evidence `3c74d22` against AC01–AC05.

## SY-DEV004-01 — Critical — unauthenticated device control and forgeable audit actor

All four BIO-DEV-004 routes under `/api/v1/devices` omit both tenant-scoped permission checks and verified actor provenance:

- `GET /api/v1/devices` exposes device identifiers, vendor/model, status and health;
- `POST /{deviceId}/poll-health` contacts the device adapter and mutates heartbeat/health state;
- `GET /{deviceId}/remote-job-capabilities` exposes supported control operations;
- `POST /{deviceId}/remote-jobs` executes reboot, configuration-push or firmware-update jobs.

The two mutating routes construct `AuditActor` directly from caller-controlled JSON. Any caller who reaches a tenant-bound host can therefore run a supported remote control operation and attribute it to an arbitrary user. The integration test's successful remote-job path demonstrates this exact unauthenticated contract: it sends only tenant test context plus `{ ActorType, ActorId }` and receives 200.

Following BIO-DEV-001/002/003 is not sufficient justification. Their documented missing authorization is inherited debt, not an authorization grant, and BIO-DEV-004 materially expands the surface from registry/diagnostic operations to remote reboot/configuration/firmware control. This violates the “controlled configuration jobs” user story and makes AC03's audit attribution untrustworthy.

Required correction:

- resolve and verify actor provenance at the HTTP boundary;
- require separate least-privilege tenant permissions for device read/health polling and remote-job execution;
- derive the audit actor only from the verified identity, never request JSON;
- add real-host negative tests for missing/forged provenance and unprivileged actors, plus an authorised positive path;
- wire the live browser path through the signed-session provenance bridge.

## Other checks

- Functional/capability gating: PASS by static trace and focused tests.
- Tenant repository scoping: PASS.
- Focused build: clean, 0 warnings/errors.
- Direct xUnit v3 execution: **13/13 passed**; these tests do not cover authorization and therefore pass over SY-DEV004-01.

Security verdict: **FAIL**. Acceptance is blocked.
