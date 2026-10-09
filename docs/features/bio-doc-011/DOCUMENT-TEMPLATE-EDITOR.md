# BIO-DOC-011 — Tenant visual document-template editor

**Owner:** Ifeanyi — Document Intelligence Engineer
**Dependencies:** BIO-DOC-004 (OCR engine, DONE), BIO-IDN-006 (RBAC/maker-checker, DONE)

## User story

As authorised BTS/tenant administrators, we need a controlled visual editor to define OCR field
regions for supported legacy/custom documents.

## AC → evidence map

| AC | Statement | Where it lives |
| --- | --- | --- |
| AC01 | Upload reference layout and draw named field/graphic regions. | `Domain/DocumentTemplateReferenceLayout.cs`, `DocumentTemplateDraft.DefineRegions` |
| AC02 | Draft/test/approval/publish lifecycle. | `Domain/DocumentTemplateDraftStatus.cs`, `DocumentTemplateDraft`'s transition methods |
| AC03 | Test corpus must pass before publish. | `Domain/SyntheticCorpusRenderer.cs`, `DocumentTemplateDraftService.RunCorpusAsync` |
| AC04 | Tenant templates cannot masquerade as official global templates. | `DocumentTemplateDraft.TenantId`/`PublicReference` |
| AC05 | Published versions are immutable. | `DocumentTemplateDraftStatus.IsImmutable`, `CreateNextDraftVersion` |
| AC06 | Approved web surface connected or HYBRID documented. | See "Why this story ships no new web UI" below. |

## What was built

A new feature project (`api/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor`) governing
one tenant-scoped aggregate, `DocumentTemplateDraft`: a lineage of versioned rows moving through
`Draft → Test → Approval → Published`, mirroring BIO-CRD-008's own `CardTemplate` lifecycle shape
(the closest existing precedent for a draft/test/approval/publish template lifecycle in this
codebase) while being adapted to this story's own field/graphic-region and corpus-testing
domain.

- **AC01** — `AttachReferenceLayout` records the canvas (width/height + a SHA-256 content hash
  computed server-side — no image-decoding library exists anywhere in this repo, the same boundary
  BIO-DOC-004's own `DocumentRaster` and BIO-CRD-008's signature-asset-by-reference pattern both
  already draw). `DefineRegions` then accepts named field regions and named graphic regions,
  validating non-empty/non-duplicate names and that every region's geometry actually fits within
  the uploaded layout. Field regions are BIO-DOC-004's own public `OcrTemplateField` type, reused
  directly rather than re-invented — the same "zero translation layer" choice this epic already
  made for `DocumentTemplateVerificationState`/`EvidenceCheckState`.
- **AC02** — `DocumentTemplateDraftStatus` is the exact named four-stage lifecycle the AC text
  uses. `SubmitForTest` (Draft→Test) requires a layout and at least one field region;
  `RecordCorpusRun` (Test→Approval or back to Draft) is driven by a corpus result; `Publish`
  (Approval→Published) freezes the row.
- **AC03** — Unlike BIO-CRD-008's own `RecordValidationAsync(bool passed, ...)`, which trusts its
  caller's own attestation for a human validation step, this story's "test corpus must pass" is a
  REAL, computed gate: `SyntheticCorpusRenderer` stamps each corpus sample's declared field values
  into a synthetic `DocumentRaster` using BIO-DOC-004's own public `OcrGlyphFont` glyph templates,
  then runs the actual `TemplateOcrEngine.Read` against it and compares every field's real,
  independently-computed read back to the sample's own declared expectation. The HTTP surface for
  "run the corpus" takes no `passed` field at all from the caller — there is nothing for a caller
  to forge. A deliberate `corrupt_field_names` option on a sample lets a tenant (and this story's
  own tests) prove the negative path genuinely fails, not just that the happy path always passes.
- **AC04** — `TenantId` is required, validated against ADR-0001's canonical `ten_` shape, and
  resolved server-side only (never a caller-supplied value — ADR-0002's "an identifier arriving in
  a request is a claim, not an authorisation"). `PublicReference` exposes a `tenant:`-prefixed wire
  identifier that structurally cannot collide with or be mistaken for a BIO-DOC-010 global pack
  entry id, because `DocumentTemplatePackEntry` (BIO-DOC-010's own type) has no `TenantId` field at
  all — a different aggregate, not merely a different value in a shared one.
- **AC05** — `IsImmutable` gates every content-bearing method once `Published`; the only way to
  change a published template's content is `CreateNextDraftVersion`, a brand-new version row under
  the same lineage (ADR-0001 §3.3: "correcting them means appending a new record that supersedes
  the old one").

## Why this project's own declared dependencies are BIO-DOC-004 and BIO-IDN-006

**BIO-DOC-004** is a real, direct `ProjectReference`. `OcrDocumentTemplate`/`OcrTemplateField`/
`TemplateOcrEngine`/`DocumentRaster`/`OcrGlyphFont` are all public (no `InternalsVisibleTo` wall,
unlike BIO-DOC-006's `DocumentClassificationOutcome` or BIO-DOC-009's `AuthenticityAssessmentResult`
this epic has hit twice before) — this story's own AC03 corpus runner constructs and reads them
directly.

**BIO-IDN-006 still gets NO `ProjectReference` from this feature project**, even after round 2 adds
a real permission check (see "Round 2 security remediation" below). Its `PermissionService` is
`internal`, `InternalsVisibleTo` scoped to `Biolog.Api` and its own test assembly only — reachable
only from `Biolog.Api.Modules.BioDoc011Module`, which wraps it behind this feature's own
`Domain.IPermissionGateway` seam, the identical cross-story gateway pattern BIO-INT-002's own
module already established. The feature project's own code only ever sees `IPermissionGateway`.

**Round 1's reasoning here was wrong by the time of review, not merely incomplete.** Round 1
claimed no authentication story had landed yet to make a real permission check meaningful — true
when BIO-DOC-011 was first dispatched (W12.2), but stale by review time: BIO-INT-001 was already
integrated and multiple accepted W12 features (BIO-INT-002, BIO-MOB-004/005, BIO-TRN-001,
BIO-VIS-001/002, BIO-ATT-001/002, BIO-ACC-001/002/003) already wired the same provenance-plus-
permission seam. Zainab's round-1 review (ZB-DOC011-01, Critical) caught this directly: every
lifecycle route trusted a raw `X-Actor-Id` with no provenance verification and no permission check
at all, so any caller reaching the tenant channel could create, edit, test, publish or forge the
audit identity of a tenant's templates. "No authentication story has landed" was never, on its own,
a safe reason to ship an unauthenticated mutation surface — ADR-0006 itself makes this explicit:
absence of authentication means the endpoint must stay unexposed or fail closed, not that an
unverifiable signal becomes safe to gate on.

## Round 2 security remediation (Zainab, ZB-DOC011-01 Critical, ZB-DOC011-02 High)

**ZB-DOC011-01 (Critical) — live provenance + permission gating, added from scratch.** Every route
(reads included, per Zainab's own note that "read routes do not even require the asserted actor")
now resolves the actor and calls `ActorProvenanceGate.Require(request, actor.Id)` before anything
else. `Api/ActorProvenanceGate.cs` is a byte-for-byte copy of BIO-INT-003's own fixed shape
(`ZB-INT003-05`), now the Kemi-ruled canonical for every *new* provenance gate — `HmacActorProvenanceStore.Verify`
always returns `false` when unconfigured or configured below `MinimumSecretLength` (32 chars), full
stop, no dummy/fallback key of any kind, unlike the older random-key shape several earlier-built
stories had to be swept and normalized away from. Every mutation additionally requires a live
`IPermissionGateway.ActorHasPermissionAsync` grant before anything else runs; `PublishAsync` uses
its own distinct `PublishPermission`, separate from every other mutation's `ManagePermission`, so a
grant for ordinary editing does not imply a grant to publish.

**Maker-checker separation-of-duties for publish was deliberately NOT built**, despite Zainab's
"if the approved policy requires it" framing. ADR-0006's own Decision 3 text discloses the exact
reason: the interim provenance-only model proves a request passed through the trusted edge, not
that two *distinct* people approved it — a single caller can still self-approve by presenting two
different asserted actor ids in two requests. Building an apparent dual-control gate on that
foundation would be security theater, not a real control. Deferred to whichever story lands genuine
per-operator authentication — the same disclosed-boundary discipline ADR-0006 itself uses, not a
gap carried forward silently.

**ZB-DOC011-02 (High) — truthful audit/commit ordering.** Round 1's own self-caught fix (committing
before auditing to avoid a false `Allowed` on a lost race) traded one bug for its mirror image: an
audit-sink failure could now leave a persisted, unaudited change, and `CreateNextVersionAsync`'s raw
`Max()+1` could still race two concurrent callers onto the same version number. Both are fixed the
way BIO-INT-002 already established for this exact class of problem: every mutation to a
`(tenantId, templateId)` lineage is now serialized through `DocumentTemplateDraftLockRegistry`
*first*. Under that lock there is only ever one writer for a given lineage, which removes the race
entirely and makes it safe to audit immediately before an unconditional `repository.Update` again
(ADR-0003 Decision 7) — no CAS is needed at the repository layer any more; the caller's own
asserted `expectedRevision` is still checked in-service (while holding the lock) purely to surface
staleness to a caller whose own view was out of date, which is a client-communication concern now,
not a concurrency-safety one. `IDocumentTemplateDraftAuditSink.ExistsAsync` — defined since round 1
but never actually called — now resolves a thrown `WriteAsync` by checking whether the event
genuinely landed before deciding whether to proceed or throw `audit_unavailable`, mirroring
BIO-INT-002's own `AuditAsync` helper exactly. `CreateNextVersionAsync`'s read-compute-audit-add
sequence runs entirely under the same lock, closing the version-number race structurally rather
than by retrying.

## Round 2 follow-up (Seyi F01, Zainab ZB-DOC011-03 — both High, independently raised)

Both reviewers converged on the same gap in the round-2 remediation above: `ListVersionsAsync`,
`GetVersionAsync`, and `GetPublishedAsync` were fixed to require a verified `ActorProvenanceGate`
proof, but still performed no `IPermissionGateway` lookup at all — a deliberate round-2 choice
(see this file's git history for the comment that read it as "the same verified identity as a
mutation, just no `ManagePermission` grant"), which both reviewers correctly called out as wrong.
Provenance proves only who is asking; it does not establish that the asker is authorised to use
the tenant's administrator-only editor. Any provenance-valid actor in the resolved tenant could
enumerate version history and read draft/test/approval template content — reference-layout
hashes, region geometry, corpus results, author metadata — belonging to a different, unauthorised
caller in the same tenant.

**Fix.** A new `ReadPermission` (`document_template_editor.read`), distinct from `ManagePermission`
and `PublishPermission`, now gates all three reads via a `RequireReadPermissionAsync` helper that
grants access on `ReadPermission` *or* either mutation permission — a caller who can manage or
publish a lineage can obviously also view it, so a manage/publish grant is not also required to
hold a separate read grant. All three methods became `Async` (`GetVersionAsync`, `ListVersionsAsync`,
`GetPublishedAsync`) to await the permission check; the three endpoint handlers now `await` them
instead of calling a synchronous method.

New regression coverage in `DocumentTemplateDraftServiceTests.cs`:
- `ZB_DOC011_03_an_actor_without_read_manage_or_publish_permission_cannot_read_any_draft_content` —
  an actor who created a draft (and so held `ManagePermission` at creation time) is denied all
  three read permissions afterward and is refused on all three read routes.
- `ZB_DOC011_03_an_actor_who_only_holds_manage_permission_can_still_read_what_they_manage` — proves
  the deliberate "manage/publish implies read" design choice, not an oversight.
- `Two_tenants_drafts_are_fully_isolated_from_each_other` (extended) — a fully-privileged actor in
  tenant B's own service instance still gets `NotFound`/empty/`null` from all three reads against
  tenant A's draft, proving tenant scoping — not a permission denial — is what isolates tenants.

`tests/foundation/BIO-DOC-011.test.sh` gained a matching "F01/ZB-DOC011-03" section asserting the
permission constant, that all three read methods call the new helper, that the helper's OR logic
checks all three permission keys, and that the endpoints await the async methods.

## Why this story ships no new web UI

The user story names a "visual editor" but no admin editor route exists anywhere in
`web/lib/route-manifest.ts` or `governance/ui-integration-map.json` for document-template
authoring, confirmed by direct search before design, not assumed. `governance/ui-integration-map.json`'s
own `BIO-DOC-011` entry (read directly from the orchestration control pack —
see `tests/foundation/BIO-DOC-011.test.sh`'s own note on why that file isn't re-checked from
inside this repo) maps this story to exactly one surface: `/document-verify` via
`DocumentVerifyService` — the SAME route and port BIO-DOC-010 already wired live. There is no
mapped surface for a drawing canvas.

This is a genuinely different shape from BIO-CRD-008's own precedent (the closest sibling story):
BIO-CRD-008 connected a `TemplateLifecycleBar` to `/card-designer`, an ALREADY-APPROVED canvas page
BIO-CRD-002/003/004 had built years earlier specifically for card templates. No equivalent
"draw regions on a document image" canvas has ever been built for documents by any BIO-UX or
BIO-DOC story. Building one here would be inventing new, unapproved UI surface outside this
story's own mapped contract and outside `web/lib/route-manifest.ts`'s own governed route list —
exactly what the UI-first constraints ("do not redesign the approved route... shared shell/
design-system edits require Damilola's recorded approval") caution against, not a gap this story
is permitted to unilaterally fill.

**Scope boundary, disclosed, not silently worked around:** AC01-AC05 are delivered as a complete,
tested backend capability (domain model + `/api/v1/document-template-drafts/*` REST surface) a
tenant administrator can drive today via direct API calls; no `web/` file is touched by this story
(verified by `tests/foundation/BIO-DOC-011.test.sh`'s own `git diff` check). AC06 is satisfied by
confirming BIO-DOC-010's own `/document-verify` `DocumentVerifyService` wiring remains fully intact
(a regression check, not a new capability from this story) and by this explicit disclosure, the
same "seam kept, new UI surface deferred" shape this epic already established for BIO-DOC-010's own
`correlatedAuthenticityDisposition`. A future UX-focused story owning the actual drawing canvas can
build directly on this validated backend — the same relationship BIO-CRD-002 has to BIO-CRD-008,
just in the opposite build order.

A further, smaller disclosed boundary: a tenant-published draft does not (yet) register into
BIO-DOC-006's shared `DocumentIntelligenceRegistry` the way BIO-DOC-010's own global pack does —
each of BIO-DOC-010's own service instances owns a private, unshared registry (see that story's own
module header), so making a tenant's published template genuinely resolvable by the live
`/api/v1/documents/identify` call would require either a shared registry singleton (a
Kemi-approval-requiring wiring change beyond this story's own write scope) or reaching into
BIO-DOC-010's own endpoint. Kept as a tested, documented gap rather than worked around.

## Why AC03 is a real computed gate, not an attestation

See `Domain/SyntheticCorpusRenderer.cs`'s own header. In short: because the renderer and the real
`TemplateOcrEngine` reader share the exact same glyph font (`OcrGlyphFont`), stamping a sample's own
declared expected value and reading it back is a genuine round-trip through production OCR logic,
not a mock. `DocumentTemplateDraftServiceTests.AC03_RunCorpusAsync_actually_runs_the_real_OCR_engine_and_advances_to_Approval_on_a_genuine_pass`
and its sibling `..._genuinely_fails_a_corrupted_sample_and_returns_to_Draft` prove both directions.

## Test/quality evidence

- `dotnet-build-output.txt` — `dotnet build api/Biolog.Api/Biolog.Api.csproj`: 0 errors, 0 warnings
  (`-maxCpuCount:1` — see the environment note below).
- `dotnet-test-output.txt` — this feature's own xUnit suite: 40/40 passed.
- `foundation-test-output.txt` — `tests/foundation/BIO-DOC-011.test.sh`: all structural checks
  passed.
- `module-reference-check-output.txt` — `scripts/check-module-project-references.sh`: PASS.

### Environment note: `-maxCpuCount:1`

A floor-wide process-spawn issue (the same class of issue behind this week's repeated
"posix_spawnp floor outage" incidents) makes `dotnet build`/`dotnet test`'s default multi-node
MSBuild hang for ~5 minutes and then fail with `Build FAILED`, `0 Error(s)`, `0 Warning(s)` — no
diagnostic text at all. Reproduced identically against BIO-DOC-010's own already-accepted,
previously-passing project in this same worktree, confirming it is an environment condition, not a
defect in this story's code. `-maxCpuCount:1` (forcing single-node MSBuild, no out-of-process
worker spawn) reliably works around it. Flagged to god as a floor-wide-relevant discovery.
