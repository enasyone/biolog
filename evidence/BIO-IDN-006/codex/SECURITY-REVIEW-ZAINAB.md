# BIO-IDN-006 specialist security review — Zainab

**Reviewed head:** `4c39477` (implementation `1814616`)  
**Verdict:** FAIL

## Category verdicts

| Category | Verdict | Reason |
| --- | --- | --- |
| Functional | PASS | The role catalogue, tenant-scoped grants, maker-checker lifecycle, audit history, and single-use approval behavior are implemented. |
| Security | FAIL | Caller-controlled `X-Actor-Id` is treated as authenticated identity. A caller can impersonate any known privileged PERSON identifier. |
| Tenant isolation | PASS | Repository and permission lookups use the resolved tenant; foreign-tenant records do not satisfy the gates. |
| Offline | APPLICABLE/PASS | No offline surface is delivered by this story. |
| Concurrency | PASS for current tier | Same-process per-key locks and compare-and-set repositories cover the reviewed races. Reopen for independently durable or multi-process stores. |
| Regression | FAIL | The mandatory privilege-escalation negative suite does not test impersonation of an existing privileged actor and therefore reports green over a live bypass. |

## ZA-IDN006-01 — privileged actor identifiers are bearer credentials

**Severity: Critical**

The shipped HTTP mutation endpoints call `ActorResolver.Resolve`, which copies the request's `X-Actor-Id` header directly into `AuditActor.Id`. No authentication middleware, signed claim, session, service credential, or other proof binds the request to that PERSON.

Every authorization gate then trusts this value:

- `ActorHoldsActiveSuperAdminAsync` looks up an active `SUPER_ADMIN` assignment using `actor.Id`.
- `PermissionService.ActorHasPermissionAsync` resolves active roles and grants using the same `actor.Id`.
- maker and checker authorization therefore also accepts an asserted ID belonging to a privileged user.

An attacker does not need their own privileged assignment. They only need to send the known canonical PERSON identifier of a Super Admin or delegated maker/checker. The server follows the identical code path used by the positive HTTP tests and records the victim identifier in the audit event, falsely attributing the attack to that user.

Canonical identifier validation does not prove possession or identity. The existing negative tests use `unprivileged_attacker`, a malformed/free-form actor, or a real unprivileged PERSON. They never send an existing privileged PERSON identifier from an unauthenticated request, which is the bypass.

This conflicts with the higher-authority architecture rule in `02-ARCHITECTURE/SYSTEM-ARCHITECTURE.md`: client/API boundaries require authentication, validation, and audit appropriate to risk. The local feature documentation's decision to defer authentication to BIO-INT-001 cannot override that master requirement for security-critical mutation endpoints.

## Required closure

Bind authorization to a trusted authenticated principal or service identity before any RBAC or maker-checker endpoint executes. `X-Actor-Id` may remain only if a trusted edge strips and injects it and the application verifies that provenance; ordinary callers must not be able to choose it. Add end-to-end negatives proving that an unauthenticated caller cannot impersonate a real seeded Super Admin or checker merely by copying their PERSON identifier. Preserve the existing tenant, permission, self-approval, replay, and audit tests.

## Verification

- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS 113/113, demonstrating the gap is absent from the structural suite.
- `dotnet tests/features/bio-idn-006/bin/Debug/net10.0/Biolog.Features.Rbac.Tests.dll` — PASS 109/109.
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using fallback scanner.
- Attempting `--filter` against the in-process xUnit executable returned unsupported-option exit 3; the unfiltered executable passed.

No implementation code was changed during review.
