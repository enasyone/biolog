# BIO-DEV-004 — Builder evidence

**Story:** Implement device health and configuration jobs
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-DEV-004`, branch `feature/BIO-DEV-004` off `develop`
**Dependency:** BIO-DEV-003 (vendor adapter framework), itself depending on BIO-DEV-001 (device
registry) and BIO-DEV-002 (certification matrix)
**Commit:** see `metadata.json`

## Summary

- **AC01** — `Features.DeviceRegistry.Device` extended with `Health`/`HealthDetail`/
  `HealthCheckedAt`, and a new `DeviceService.RecordHeartbeatAsync` populates them alongside the
  previously-reserved, never-populated `LastSeenAt` field, mirroring `ChangeStatusAsync`'s own
  audit-before-commit/locked/`CancellationToken.None` discipline exactly.
- **AC02** — remote jobs gated by certification status (BIO-DEV-002) → adapter resolution
  (BIO-DEV-003) → an explicit per-category job-type support table (`BioDev004Module`'s own
  `SupportedJobsByCategory`) — "where vendor APIs permit."
- **AC03** — every remote job audited through the real shared `IAuditSink` (the same sink
  `DeviceService`'s own status-change audit uses) BEFORE execution, not after.
- **AC04** — three distinct, explicit failure shapes: `device_combination_unsupported` (409),
  `device_adapter_unavailable` (503), `remote_job_not_supported` (409) — never a generic error.
- **AC05** — a new "Device operations" panel mounted additively onto `/devices`, proven via
  SIMULATION-mode Playwright (6 tests) and a real `.NET` integration test
  (`BioDev004EndpointsTests.cs`) against the real composed host (real registration, real
  certification, real poll, real remote job, real audit event, real rejection paths, real
  cross-tenant isolation).

**Round 2 (SY-DEV004-01, Seyi, Critical) — fixed**: round 1 shipped with zero provenance/RBAC
gating on all four routes, and the two mutating routes derived the audit actor directly from
caller-controlled request JSON — any caller could run a real remote job and attribute it to an
arbitrary actor. Fixed by porting the canonical `ActorProvenanceGate` + `IPermissionGateway`
pattern (ADR-0006 Decision 3, same shape as BIO-TRN-003/BIO-INT-004/BIO-ATT-003) onto every
route, with two distinct least-privilege permissions
(`DeviceHealthPollingService.ReadPermission`/`DeviceRemoteJobService.ExecutePermission`) and the
audit actor now derived only from verified `X-Actor-Id`/`X-Actor-Provenance` headers, never the
request body. Added the missing `web/lib/actor-provenance-routes.ts` entry, scoped precisely to
this story's own four routes.

Also disclosed explicitly (not silently assumed): no real background scheduler exists (polling is
triggered via HTTP, not scheduled); no vendor SDK exposes a real remote-admin call (job execution
is a deterministic simulation). Full detail in `docs/features/bio-dev-004/README.md`.

Verified: `dotnet build` clean. 279/279 integration (265 original + 9 round-1 + 5 new
authorization-boundary tests, no regression). 210/210 unit, no regression. 16/16 feature-unit
tests (13 round-1 + 3 new permission-gate tests). Module-reference/secret-scan/traceability all
PASS. `npx tsc --noEmit` clean. `npx vitest run` 550/550 (unaffected by the round-2 backend
change). `npx playwright test bio-dev-004-device-operations`: 6/6 passed, re-run twice more to
confirm stability (SIMULATION mode, unaffected by the round-2 backend change for the same
reason).
