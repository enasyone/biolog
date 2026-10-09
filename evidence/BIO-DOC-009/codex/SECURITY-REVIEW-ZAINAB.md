# BIO-DOC-009 security review — Zainab

## Verdict: FAIL (High)

### BIO-DOC-009-F01 — HTTP read/override surface has no authorization or tenant boundary

`GET /api/v1/documents/authenticity/{documentSessionId}` executes without any actor, permission, or tenant check and returns the full assessment (including individual evidence/check results) to any caller who knows or guesses a session ID. The in-memory store is keyed only by session ID. More critically, `POST .../{id}/override` accepts arbitrary caller-supplied `X-Actor-Id` and only requires it to be nonblank; no permission/RBAC or trusted actor provenance gate is applied. Any network caller can rewrite the effective authenticity disposition and have the forged identity durably audited. The source comments disclose that authentication is deferred to BIO-INT-001, but this is an exposed HTTP surface and the manual override is consequential.

**Required fix:** enforce trusted actor provenance and an explicit override permission before applying overrides; bind reads and writes to resolved tenant context (or keep endpoints inaccessible until that boundary exists). Do not treat a caller-controlled header as authentication.

### Positive checks

Raw evidence is immutable across overrides, expected-version concurrency is checked, blank reasons are rejected, and override audit writes are durable. These do not mitigate the missing authorization boundary.

Evidence reviewed: `Api/AuthenticityEndpoints.cs`, `Api/ActorResolver.cs`, `Api/AuthenticityAssessmentService.cs`, `Model/InMemoryAuthenticityAssessmentStore.cs`, and AC05 tests.
