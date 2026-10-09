# BIO-DOC-011 — Builder evidence

**Story:** Implement tenant visual document-template editor
**Dependencies:** BIO-DOC-004 (OCR engine, DONE), BIO-IDN-006 (RBAC/maker-checker, DONE)

## What was built

A new feature project (`api/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor`)
governing one tenant-scoped aggregate, `DocumentTemplateDraft`: a lineage of versioned rows
moving through `Draft → Test → Approval → Published`, mirroring BIO-CRD-008's own `CardTemplate`
lifecycle shape (the closest existing precedent for a draft/test/approval/publish template
lifecycle in this codebase), adapted to this story's own field/graphic-region and corpus-testing
domain. Full design rationale lives in `docs/features/bio-doc-011/DOCUMENT-TEMPLATE-EDITOR.md`.

- **AC01** — `AttachReferenceLayout` records the uploaded canvas (width/height + a server-computed
  SHA-256 content hash — no image-decoding library exists anywhere in this repo, the same
  by-reference boundary BIO-CRD-008 draws for signature assets). `DefineRegions` accepts named
  field regions (reusing BIO-DOC-004's own public `OcrTemplateField` directly — zero translation
  layer) and named graphic regions, validating non-empty/non-duplicate names and that every
  region's geometry fits within the uploaded layout's bounds.
- **AC02** — `DocumentTemplateDraftStatus` is the exact named four-stage lifecycle the AC text
  uses (`Draft`/`Test`/`Approval`/`Published`). `SubmitForTest` requires a layout and at least one
  field region; `RecordCorpusRun` moves Test→Approval on a genuine pass or back to Draft on a
  genuine fail; `Publish` freezes the row.
- **AC03** — A REAL, computed gate, not a caller-attested boolean: `SyntheticCorpusRenderer`
  stamps each corpus sample's own declared field values into a synthetic raster using BIO-DOC-004's
  own public `OcrGlyphFont` glyph bitmaps, then runs the actual `TemplateOcrEngine.Read` against it
  and compares every field's independently-computed read back to the sample's declared
  expectation. The HTTP surface for "run the corpus" takes no `passed` field from the caller — a
  deliberate `corrupt_field_names` option on a sample proves the negative path genuinely fails,
  not just that the happy path always passes.
- **AC04** — `TenantId` is required, validated against ADR-0001's canonical `ten_` shape, and
  resolved server-side only (never caller-supplied — ADR-0002's "an identifier arriving in a
  request is a claim, not an authorisation"). `PublicReference` carries a `tenant:`-prefixed wire
  identifier that structurally cannot collide with or be mistaken for a BIO-DOC-010 global pack
  entry id, because `DocumentTemplatePackEntry` has no `TenantId` field at all — a different
  aggregate, not merely a different value in a shared one.
- **AC05** — `IsImmutable` gates every content-bearing method once `Published`; the only way to
  change a published template's content is `CreateNextDraftVersion`, a brand-new version row under
  the same lineage (ADR-0001 §3.3).
- **AC06** — See "Why this story ships no new web UI" below: a disclosed scope boundary, not a
  silently-worked-around gap.

## Why this project's own declared dependencies are BIO-DOC-004 only (no ProjectReference to BIO-IDN-006)

BIO-DOC-004's `OcrDocumentTemplate`/`OcrTemplateField`/`TemplateOcrEngine`/`DocumentRaster`/
`OcrGlyphFont` are all public (no `InternalsVisibleTo` wall) — this story's own AC03 corpus runner
constructs and reads them directly. BIO-IDN-006's `RoleAssignmentService`/`PermissionService`/
`MakerCheckerService` are `internal`, scoped to `Biolog.Api` and its own tests only, and no
authentication story has landed yet (BIO-INT-001 is W12) to produce a verified actor identity
ADR-0006 Decision 3 would allow gating a real authorization decision on. Attempting a
"TenantAdmin role" check against today's unverified `X-Actor-Id` would be exactly the anti-pattern
ADR-0006 exists to close. AC04's actual substance does not need it — it is satisfied structurally
(required, server-resolved `TenantId`; a wholly separate aggregate/store from BIO-DOC-010's own
global pack). Full reasoning in the design doc and in `BioDoc011Module.cs`'s own header.

## Why this story ships no new web UI

No admin editor route exists anywhere in `web/lib/route-manifest.ts` or
`governance/ui-integration-map.json` for document-template authoring — confirmed by direct search
before design, not assumed. `governance/ui-integration-map.json`'s own `BIO-DOC-011` entry maps
this story to exactly one surface: `/document-verify` via `DocumentVerifyService` — the same route
and port BIO-DOC-010 already wired live. No `web/` file is touched by this story (verified by
`tests/foundation/BIO-DOC-011.test.sh`'s own `git diff` check). AC06 is satisfied by confirming
BIO-DOC-010's own `/document-verify` wiring remains fully intact (a regression check, not a new
capability from this story) plus this explicit disclosure — the same "seam kept, new UI surface
deferred" shape this epic already established for BIO-DOC-010's own
`correlatedAuthenticityDisposition`. A future UX-focused story owning the actual drawing canvas can
build directly on this validated backend.

A further, smaller disclosed boundary: a tenant-published draft does not (yet) register into
BIO-DOC-006's shared `DocumentIntelligenceRegistry` the way BIO-DOC-010's own global pack does.
Kept as a tested, documented gap rather than worked around — would require a shared registry
wiring change beyond this story's own write scope.

## A genuine defect found and fixed during review before submission

While reviewing this story's own `DocumentTemplateDraftService.MutateAsync`, found that the
"Allowed" audit event was being written *before* `repository.TryUpdate(updated, expectedRevision)`
was checked. `TryUpdate` performs real optimistic-concurrency compare-and-set and can genuinely
return `false` on a lost race (two callers with the same stale `expectedRevision`) — when it does,
a `ConcurrencyConflict` was thrown, but the audit sink already held an "Allowed" record for a
mutation that never actually persisted. The same claim-vs-actual-outcome race class BIO-INT-002
hit. Fixed by reordering: `TryUpdate` is attempted first; on failure, a `Denied`/
`document_template_draft_concurrency_conflict` audit event is written before the exception is
thrown; the `Allowed` audit only follows a confirmed successful persist. A regression test
(`A_lost_concurrency_race_records_a_Denied_audit_event_never_an_Allowed_one`) proves the audit
sink never records a false-positive `Allowed` outcome on a lost race. The foundation test's own
check label (previously "audits before TryUpdate") was corrected to describe this actual,
now-fixed invariant.

**A related, pre-existing issue noted but out of this story's scope:** BIO-CRD-008's own
`CardTemplateService.MutateAsync` (already-accepted code) has the identical audit-before-persist
ordering. Not fixed here — outside this story's write scope (`api/features/bio-doc-011/**` only) —
flagged to god for separate follow-up.

## Test/quality evidence

- `dotnet-build-output.txt` — `dotnet build api/Biolog.Api/Biolog.Api.csproj -maxCpuCount:1`: 0
  errors, 2 NU1900 warnings (nuget.org vulnerability-data fetch unavailable in this environment —
  a network condition, not a defect; see environment note below).
- `dotnet-test-output.txt` — this feature's own xUnit suite: 41/41 passed (40 original + the
  concurrency-audit-integrity regression test added during review).
- `foundation-test-output.txt` — `tests/foundation/BIO-DOC-011.test.sh`: 36/36 structural checks
  passed.
- `module-reference-check-output.txt` — `scripts/check-module-project-references.sh`: PASS.
- A full `scripts/quality.sh --full` run was also executed (outside the sandbox, to avoid
  sandbox-caused false failures — see below). All failures observed were either the expected
  pending-evidence traceability gap (resolved by this very evidence) or pre-existing environment
  conditions unrelated to this story's own files: missing `node_modules`/vitest in this worktree
  (frontend unit tests, Playwright, BIO-OPS-001's own harness all fail with
  `vitest: command not found` / `node_modules missing`) and NU1900 nuget.org network-advisory
  failures across several unrelated projects. No `web/` file is touched by this story and no
  failure traces to `api/features/bio-doc-011/**` or its host wiring.

### Environment notes

- **Sandbox local-port/temp-file restrictions**: running the quality gate inside the default
  sandbox produces spurious failures unrelated to any code — `dotnet test`'s vstest host needs a
  local TCP socket (blocked: `SocketException: Permission denied`), and `tests/foundation/
  BIO-FND-008.test.sh` / the BIO-ARC-006 contract-freeze harness both call `mktemp` against a path
  outside the sandbox's writable set. Reproduced and confirmed by rerunning outside the sandbox,
  which eliminates all of these. Not a defect in this story or in BIO-FND-008/BIO-ARC-006.
- **`-maxCpuCount:1`**: a floor-wide process-spawn issue made `dotnet build`/`dotnet test`'s
  default multi-node MSBuild hang and then fail with no diagnostic text. Forcing single-node
  MSBuild reliably works around it — the same workaround already flagged to god as floor-wide
  relevant.

## Scope notes (deliberately not built)

- No new web UI (a drawing-canvas admin page) — see "Why this story ships no new web UI" above.
- No live cross-reference wiring into BIO-DOC-006's shared `DocumentIntelligenceRegistry` for a
  tenant's published template — a tested, documented seam, not a silently worked-around gap.
- No `ProjectReference`/adapter to BIO-IDN-006 — no authentication story has landed yet to make a
  real authorization-decision seam meaningful; AC04's actual substance is satisfied structurally
  instead. A future story landing real authentication can add a narrow authority-check seam
  without this feature project's own domain code changing shape.
