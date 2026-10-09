# BIO-DOC-011 security review — round 2

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** implementation `a5989f2`, builder evidence `2fbe2f5`

The original forged-actor mutation bypass and audit ordering/race findings are substantially closed. One authorization blocker remains.

## ZB-DOC011-03 — High — tenant template reads omit role-based access control

`ListVersionsAsync`, `GetVersionAsync`, and `GetPublishedAsync` require a valid actor provenance proof but deliberately perform no `IPermissionGateway` lookup. Provenance establishes an asserted identity; it does not authorize that actor to use the tenant's administrator-only editor.

Any provenance-valid but unprivileged actor in the resolved tenant can enumerate versions and read draft, test, approval, and published template definitions, including reference-layout hashes, region geometry, corpus results, and author metadata. These routes are part of the tenant editor, not BIO-DOC-010's separate public global-template surface.

Required: enforce a tenant-scoped read or manage permission on all three read routes and add unprivileged, withdrawn-permission, and cross-tenant negative tests.

## Verified closures

- All mutations require canonical fail-closed provenance plus live tenant manage/publish permissions; publish uses a distinct permission.
- Tenant identity remains server-resolved and repository keys are tenant-scoped.
- Per-lineage locking closes the next-version allocation race and protects expected-revision checks.
- Stable audit event identifiers plus `ExistsAsync` resolve ambiguous audit writes before persistence proceeds.

The in-process lock is sufficient for the current in-memory repository. A future multi-instance/durable repository must replace it with a cross-process transaction or concurrency primitive.
