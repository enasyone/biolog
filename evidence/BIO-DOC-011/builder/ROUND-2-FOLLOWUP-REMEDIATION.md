# BIO-DOC-011 — Round-2 follow-up remediation (Seyi F01 / Zainab ZB-DOC011-03, both High)

**Reviewed verdicts, both against round-2 commit `2fbe2f5`:**
- Seyi, `REVIEW-Seyi-round2.md`: FAIL — F01.
- Zainab, `SECURITY-REVIEW-ZAINAB-ROUND-2.md`: FAIL — ZB-DOC011-03.

Both reviewers independently converged on the exact same remaining gap and confirmed every other
round-2 fix (live provenance, live permission gating on mutations, per-lineage locking, truthful
audit-before-commit ordering) held up.

## The finding

`ListVersionsAsync`, `GetVersionAsync`, and `GetPublishedAsync` called `ActorProvenanceGate.Require`
but performed no `IPermissionGateway` lookup at all. Provenance proves only who is asking, not that
they are authorised to use the tenant's administrator-only editor. Any provenance-valid actor in the
resolved tenant could enumerate version history and read draft/test/approval template content —
reference-layout hashes, region geometry, corpus results, author metadata — belonging to any other
caller in the same tenant, regardless of that caller's own permissions. This is an authorization
gap, not an authentication one: the round-1 critical fix (provenance) genuinely holds.

## The fix

- `DocumentTemplateDraftService.cs` — new `ReadPermission = "document_template_editor.read"`
  constant, distinct from `ManagePermission`/`PublishPermission`. New private
  `RequireReadPermissionAsync(tenantId, actorId, cancellationToken)` grants access on
  `ReadPermission` *or* either mutation permission — deliberate: a caller who can manage or
  publish a lineage can obviously also view it, so holding a manage/publish grant should not also
  require a separate read grant.
- `GetVersion`/`ListVersions`/`GetPublished` became `GetVersionAsync`/`ListVersionsAsync`/
  `GetPublishedAsync`, each now awaiting `RequireReadPermissionAsync` before touching the
  repository, taking an `actorId` parameter.
- `DocumentTemplateDraftEndpoints.cs` — the three read handlers now `await` the async service
  calls instead of calling a synchronous method, passing `actor.Id` and `context.RequestAborted`.
- `docs/features/bio-doc-011/DOCUMENT-TEMPLATE-EDITOR.md` — new "Round 2 follow-up" section
  documenting the finding and fix.

**New regression tests** (`DocumentTemplateDraftServiceTests.cs`):
- `ZB_DOC011_03_an_actor_without_read_manage_or_publish_permission_cannot_read_any_draft_content`
- `ZB_DOC011_03_an_actor_who_only_holds_manage_permission_can_still_read_what_they_manage`
- `Two_tenants_drafts_are_fully_isolated_from_each_other` (extended to assert `ListVersionsAsync`
  and `GetPublishedAsync` too, with a fully-privileged actor in the wrong tenant, isolating the
  "cross-tenant" negative case both reviewers asked for from the "unprivileged" one)

`tests/foundation/BIO-DOC-011.test.sh` gained a "F01/ZB-DOC011-03" section: the permission constant
exists; all three read methods call the new helper exactly once each; the helper's OR logic checks
all three permission keys; the endpoints `await` the async methods; the new test names exist.

## Verification

- `dotnet build api/Biolog.Api/Biolog.Api.csproj`: 0 errors (warnings are pre-existing NU1900
  network-advisory only).
- `dotnet test tests/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor.Tests/...`:
  48/48 passed (46 prior + 2 new).
- `tests/foundation/BIO-DOC-011.test.sh`: 55/55 passed (50 prior + 5 new).
- `scripts/check-module-project-references.sh`: PASS (no new `ProjectReference` anywhere — this
  fix touches only the service and endpoints, no new dependency on BIO-IDN-006's own project).
- `scripts/quality.sh --full` (outside the sandbox, full output in `quality-full-output.txt`):
  **discovered along the way** that this story was never wired into `scripts/quality.sh` at all —
  neither its own foundation harness nor its own `dotnet test` project appeared anywhere in that
  script, unlike every other delivered W12 story (BIO-IDN-006, BIO-RPT-002/003, BIO-CRD-008, etc.),
  so the "full gate" claim in the round-1 and round-2 evidence was never actually exercising this
  feature's own tests. Added the two missing `run` lines (mirroring the exact sibling convention)
  so this gap doesn't ship silently. With that fix in place, `BIO-DOC-011 document-template editor
  harness` (55/55) and `.NET document-template editor unit tests` (48/48) both now run inside the
  full gate and both report PASS. The remaining 10 failures are the same pre-existing,
  already-documented environment conditions from round 1/2 (missing `node_modules`/`vitest`/
  `playwright` in this worktree for frontend/Playwright/BIO-OPS-001's own harness; NU1900 nuget.org
  advisory-source network failures) — none attributable to this story's own code.
