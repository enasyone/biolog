# BIO-DOC-011 — Round-2 remediation (Zainab FAIL at `313a546`)

**Reviewed verdict:** FAIL at commit `313a5468faa360de1191b198a810861fb4f97b24`
**Evidence reviewed:** `evidence/BIO-DOC-011/codex/SECURITY-REVIEW-ZAINAB.md`

## ZB-DOC011-01 (Critical) — unauthenticated template administration

**Finding:** every create/edit/test/publish/new-version route trusted an arbitrary `X-Actor-Id`
with no ADR-0006 provenance verification, authentication, or live BIO-IDN-006 permission lookup.
Read routes did not even require the asserted actor.

**Fix:**
- `Api/ActorProvenanceGate.cs` — a new file, byte-for-byte copying BIO-INT-003's own canonical,
  Kemi-ruled fixed shape (`ZB-INT003-05`): `HmacActorProvenanceStore.Verify` always returns `false`
  when unconfigured or configured below `MinimumSecretLength` (32), full stop — no dummy/fallback
  key of any kind, unlike the older random-key shape several earlier-built stories had to be swept
  and normalized away from. Reads its own feature-local `DocumentTemplateEditor:ActorProvenanceSecret`
  configuration key, matching every sibling story's own per-feature secret convention.
- Every route (10 of 10, reads included) now calls `ActorProvenanceGate.Require(request, actor.Id)`
  immediately after resolving the actor, before anything else runs. A missing/invalid proof throws
  `ActorProvenanceRequiredException`, mapped to HTTP 401 in `DocumentTemplateDraftEndpoints`'s own
  `Handle` wrapper.
- `Domain/IPermissionGateway.cs` — a new, narrow per-feature seam (no `ProjectReference` to
  BIO-IDN-006 — see the design doc's updated "Why this project's own declared dependencies" section).
  `Biolog.Api.Modules.BioDoc011Module.PermissionGatewayAdapter` wraps BIO-IDN-006's own
  already-registered `PermissionService.ActorHasPermissionAsync` directly, reachable only because
  that project's own csproj already grants `Biolog.Api` internals visibility — the identical
  cross-story gateway pattern BIO-INT-002's own module already established.
- Every mutating service method now calls `RequirePermissionAsync` before anything else.
  `PublishAsync` uses a distinct `PublishPermission` (`document_template_editor.publish`), separate
  from every other mutation's `ManagePermission` (`document_template_editor.manage`) — "Publishing
  should use a dedicated high-impact permission" from the review, satisfied directly.
- **Maker-checker separation-of-duties for publish was deliberately NOT built.** ADR-0006's own
  Decision 3 text discloses that the interim provenance-only model cannot yet prove two *distinct*
  people approved an action — a single caller can still self-approve by presenting two different
  asserted actor ids in two requests. An apparent dual-control gate on that foundation would be
  security theater, not a real control, so it is deferred to whichever story lands genuine
  per-operator authentication — the same disclosed-boundary discipline ADR-0006 itself uses.

## ZB-DOC011-02 (High) — successful mutations could persist without their audit record

**Finding:** `MutateAsync` committed via `TryUpdate` and only then audited `Allowed` — a sink
failure or cancellation could leave a persisted, unaudited change, and the error text would falsely
claim the change was not committed. `CreateNextVersionAsync` audited `Allowed` before `Add`, and
concurrent callers could choose the same next version. `ExistsAsync` existed but was never called.

**Fix:** the same architecture BIO-INT-002 already established for this exact class of problem.
- `DocumentTemplateDraftLockRegistry` (new) — per-`(TenantId, TemplateId)` async locking, mirroring
  `ImportLockRegistry` exactly. Every mutation (including `CreateDraftAsync` and
  `CreateNextVersionAsync`) now acquires this lock before touching the lineage.
- Under that lock there is only ever one writer for a given lineage, which removes the race
  entirely — this is what makes it safe to audit `Allowed` immediately before an unconditional
  `repository.Update` again, matching ADR-0003 Decision 7's audit-before-commit discipline without
  round 1's false-positive risk. `IDocumentTemplateDraftRepository.TryUpdate(expectedRevision)` is
  removed; `Update` is now a plain, unconditional replace — the caller's own asserted
  `expectedRevision` is still checked in-service (while holding the lock) to surface staleness to a
  caller whose own view was out of date, now a client-communication concern, not a
  concurrency-safety one.
- `AuditAsync`'s helper now calls `auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None)`
  when `WriteAsync` throws, resolving the ambiguity instead of assuming failure — mirroring
  BIO-INT-002's own `AuditAsync` helper exactly.
- `CreateNextVersionAsync`'s entire read-`Max()+1`-compute-audit-add sequence now runs under the
  same lock, closing the version-number race structurally rather than by retrying.

**New regression tests** (all in `DocumentTemplateDraftServiceTests.cs`):
- `ZB_DOC011_01_a_mutation_without_the_required_permission_is_refused`
- `ZB_DOC011_01_publishing_requires_its_own_distinct_permission_from_every_other_mutation`
- `ZB_DOC011_02_an_ambiguous_write_throw_is_resolved_via_ExistsAsync_and_the_mutation_still_succeeds`
  (a new `AmbiguousWriteAuditSink` fixture: the inner sink genuinely persists the event, then the
  wrapper throws anyway, simulating a transient ack failure on an otherwise-successful write)
- `ZB_DOC011_02_a_genuine_total_audit_failure_refuses_the_mutation_and_leaves_no_persisted_row`
  (a new `AlwaysFailingAuditSink` fixture proves the mutation is refused AND nothing is persisted)
- `ZB_DOC011_02_two_concurrent_CreateNextVersionAsync_calls_never_collide_on_the_same_version_number`
  (a real `Task.WhenAll` race against the same lineage, proving the lock — not luck — serializes it)

## Verification

- `dotnet-build-output.txt` — `dotnet build` (host, with the new module wiring): 0 errors.
- `dotnet-test-output.txt` — this feature's own xUnit suite: 46/46 passed (41 round-1 + 5 new
  round-2 regression tests).
- `foundation-test-output.txt` — `tests/foundation/BIO-DOC-011.test.sh`: 50/50 (36 round-1 + 14 new
  structural checks naming ZB-DOC011-01/02 directly).
- `module-reference-check-output.txt` — PASS, unchanged (no new `ProjectReference` from the
  feature project; `Biolog.Api.csproj` already referenced BIO-IDN-006's feature project from an
  earlier story, so no host csproj change was needed either).
- A full `scripts/quality.sh --full` run was executed (outside the sandbox). The only failures
  observed are the same pre-existing environment conditions already documented in the round-1
  evidence (missing `node_modules`/vitest in this worktree for frontend/Playwright/BIO-OPS-001's
  own harness; NU1900 nuget.org network-advisory failures) — no new failure, and nothing traces to
  `api/features/bio-doc-011/**` or its host wiring.
