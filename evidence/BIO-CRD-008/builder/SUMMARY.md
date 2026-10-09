# BIO-CRD-008 — Builder evidence

**Story:** Implement published card-template lifecycle and provenance
**Result:** PASS

## Restated scope (per the story-stage prompt)

**User story.** As card administrators, I need Draft → Validation → Approval → Published →
Retired lifecycle with immutable published versions.

**Acceptance criteria:** AC01 (published template is immutable), AC02 (edit creates new version),
AC03 (template retains card size/orientation, objects/fonts/watermarks/assets/photo spec/barcode/
signature rules), AC04 (production references exact published version), AC05 (the approved web
surface is connected through `CredentialService`, mapped human workflow passes without an
unapproved UI redesign).

**Dependencies.** BIO-CRD-003 (typography/watermark vocabulary — mirrored), BIO-CRD-004
(barcode vocabulary mirrored; signature-asset existence is the one genuine cross-story runtime
dependency).

**Owned write paths.** `api/features/bio-crd-008/**` (new feature project), `api/Biolog.Api/
Modules/BioCrd008Module.cs`, `tests/features/bio-crd-008/**`, `web/features/bio-crd-008/**` (new),
`web/features/bio-crd-002/CardDesignerApp.tsx` / `web/features/bio-crd-003/CardTypographyApp.tsx`
/ `web/features/bio-crd-004/CardObjectsApp.tsx` (additive `onChange` prop only),
`tests/frontend/playwright/bio-crd-008-*.spec.ts`, `tests/frontend/unit/bio-crd-008-*.test.ts`,
`docs/features/bio-crd-008/**`, `evidence/BIO-CRD-008/**`.

**Shared, unlisted entry points touched:** `api/Biolog.Api/Program.cs`, `api/Biolog.Api/
Biolog.Api.csproj`, `web/lib/module-registry.ts`, `web/lib/feature-mode.ts`,
`web/next.config.mjs`, `web/app/card-designer/page.tsx` (mounts `TemplateLifecycleBar` alongside
BIO-UX-007's own unmodified `SaveDesignBar`), `tests/frontend/unit/bio-ux-005-feature-mode.test.ts`
(its own guard test updated), `scripts/quality.sh`.

**One additive change to BIO-CRD-007 (my own prior, already-accepted story).**
`api/features/bio-crd-007/Domain/ProductionJob.cs` gained exactly two new, optional,
non-`required` properties (`TemplateId`, `TemplateVersion`) — AC04's own requirement.
`ProductionService.CreateJobAsync` gained two new optional trailing parameters, placed after
`cancellationToken = default` so every existing call site is unaffected. Verified by rerunning
BIO-CRD-007's own full suite (54/54 green, including two new tests for the addition).

**Read-only shared contracts respected.** `SaveDesignBar.tsx`, `web/app/audit/page.tsx`,
`web/app/page.tsx` (Dashboard), every BIO-CRD-005/006/007 route not touched beyond the one
additive field pair — none modified beyond what's declared above.

**Security/privacy risks identified and addressed.**
- Tenant isolation: every `CardTemplate` row is looked up strictly through `ICurrentTenantProvider`
  (never request content); a cross-tenant lookup is 404, never a confirmed-to-exist 403; an empty
  tenant's own lineage listing is genuinely empty. Verified by test.
- No PII/biometric data — every field is design/lifecycle metadata (format, geometry, watermark
  preset, barcode profile, a signature *reference* by lineage/version, never signature content
  itself).
- Provenance-before-commit discipline for every lifecycle transition (create/edit/submit/validate/
  reject/publish/retire) — audited before persisting, the story title's own "provenance"
  requirement.

**Tests planned and written.** See "Test evidence" below.

## Design decisions

See `docs/features/bio-crd-008/README.md` for the full write-up. Summary: `Version` (lifecycle
identity, immutable once assigned) and `Revision` (optimistic-concurrency token) are two
deliberately separate mechanisms; in-place `Edit` while Draft/Validation/Approval, a brand-new
`CreateNextDraftVersion` row once Published/Retired (AC01/AC02); vocabulary mirrored from
BIO-CRD-002/003/004 as this project's own small whitelists (not a declared dependency for
BIO-CRD-002), except the one genuine cross-story runtime dependency (`ISignatureAssetGateway`
wrapping BIO-CRD-004's real signature-asset repository); each of the three pre-existing card-
designer canvases gained one additive, optional `onChange` prop (zero I/O of their own, unchanged
per Kemi's ruling) so a new `TemplateLifecycleBar` component — this story's own new file — could
build a governed snapshot without reopening any sibling story's owned files; AC04 enforcement
lives at the one sanctioned source (`CardTemplateService.GetPublished`), not duplicated into
BIO-CRD-007's own DI graph.

## Issues found and fixed during development (not assumed correct, verified)

1. **A genuine compile-time overload ambiguity**: two `TryParse` methods differing only by their
   `out` parameter's type are ambiguous at an `out var` call site — caught before this project
   ever compiled. Fixed by renaming the orientation overload distinctly.
2. **A missing `using` statement resolved the wrong extension method** (`ActorTypeExtensions.
   ToWireString` vs. `BarcodeProfileExtensions.ToWireString`) — the same class of bug BIO-RPT-002
   hit in its own module file. Fixed by adding the missing `using Biolog.Features.CardTemplates.
   Audit;`.
3. **A DI lifetime bug was avoided proactively** by registering `CardTemplateService` Scoped
   (matching `ReportService`'s own precedent) rather than repeating BIO-RPT-002's own
   Singleton-depending-on-Scoped mistake — confirmed clean by booting the real host early, right
   after wiring the module, not saved for a final pass.
4. **A real, LIVE-only bug**: the frontend's hand-written `transitionAction` helper
   (`web/features/bio-crd-008/live.ts`) re-serialised the raw camelCase frontend input object
   directly as the POST body for submit/validate/reject/publish/retire, but the backend's request
   DTOs use snake_case wire fields (`expected_revision`, `passed`, `note`). Every one of these five
   actions silently failed with a 422 the mock adapters could never reproduce (they never
   round-trip through JSON at all) and the backend's own integration tests could never catch either
   (they construct the request body correctly by hand). Only the real dual-server LIVE Playwright
   run caught it — the UI's status stayed stuck on "draft" no matter which button was clicked.
   Fixed by giving `transitionAction` a `toWireBody` builder per action.
5. **A stale, reused dev server produced a false-negative SIMULATION test failure** — Playwright's
   own `reuseExistingServer` picked up an old `next dev` process still running pre-story code.
   Confirmed via a direct probe script that the component itself hydrates with zero runtime errors
   once a fresh server is used. Not a code bug; fixed by killing the stale process.

None of these were review-round findings — all five were caught and fixed during my own
implementation pass, before any independent review.

## Test evidence

- **Backend unit/integration tests** (`dotnet test`, xunit): 29/29 passing
  (`tests/features/bio-crd-008/`) — see `backend-unit-test-output.txt`. Covers AC01 (immutability
  at both the domain and real-HTTP layers), AC02 (new-version mechanics, auto-retire-on-republish),
  AC03 (snapshot round-trip, signature-asset existence gateway both branches, custom-format
  validation), AC04 (`GetPublished` never returns a non-Published row), tenant isolation,
  concurrency conflicts, and a structural route-collision regression test.
- **BIO-CRD-007 regression** (my own prior story, two additive fields + one additive optional
  parameter pair): 54/54 passing, including two new tests — see `bio-crd-007-regression-output.txt`.
- **API host build**: clean, 0 errors/warnings, `BioCrd008Module` registered and DI-validated at
  startup — see `api-host-build-output.txt`.
- **`tsc --noEmit` + `next build`**: PASS, zero errors — see `web-typecheck-output.txt`,
  `web-build-output.txt`.
- **Frontend unit tests** (vitest): 413/413 passing across 56 files, including 8 new
  `bio-crd-008-template-lifecycle-mock.test.ts` tests and the updated `bio-ux-005-feature-mode.
  test.ts` multi-port guard — see `frontend-unit-test-output.txt`.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode, single-worker: 413/413 passing
  (4 new `bio-crd-008-template-lifecycle.spec.ts` tests, 10 skipped-by-design LIVE-only tests
  across the repo) — no regression across the pre-existing suite — see
  `frontend-e2e-test-output.txt`.
- **AC05 LIVE-mode dual-server proof, actually run, not just asserted**: real `.NET` backend +
  LIVE-built Next.js server, a tenant domain registered and verified through BIO-TEN-002's own
  control plane. 3/3 Playwright cases passed (`frontend-live-e2e-test-output.txt`), plus direct
  `curl` verification of the full create→submit→validate→publish→(immutable 409)→new-version cycle
  against the real backend — this run is exactly what surfaced issue #4 above.
