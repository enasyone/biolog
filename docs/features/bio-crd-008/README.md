# BIO-CRD-008 — Published card-template lifecycle and provenance

**Story:** Implement published card-template lifecycle and provenance
**Wave:** W12 (dispatched out of formal batch order, human priority — see the hive-protocol
dispatch reasoning in evidence).

## User story

As card administrators, I need Draft → Validation → Approval → Published → Retired lifecycle with
immutable published versions.

## Acceptance criteria

- **AC01** — Published template is immutable.
- **AC02** — Edit creates new version.
- **AC03** — Template retains card size/orientation, objects/fonts/watermarks/assets/photo spec/
  barcode/signature rules.
- **AC04** — Production references exact published version.
- **AC05** — The approved web surface (`/cards`, `/cards/issue`, `/cards/replace`,
  `/card-designer`, `/credentials`) is connected through `CredentialService` to this story's live
  implementation or an explicitly documented HYBRID state, and the mapped end-to-end human
  workflow passes without an unapproved UI redesign.

**Dependencies.** BIO-CRD-003 (card typography/watermark vocabulary — mirrored, not gatewayed),
BIO-CRD-004 (barcode vocabulary mirrored; signature-asset existence is the one genuine cross-story
runtime dependency, reached through `ISignatureAssetGateway`).

## Owned write paths

`api/features/bio-crd-008/**` (new feature project), `api/Biolog.Api/Modules/BioCrd008Module.cs`,
`tests/features/bio-crd-008/**`, `web/features/bio-crd-008/**` (new), `web/features/bio-crd-002/
CardDesignerApp.tsx` / `web/features/bio-crd-003/CardTypographyApp.tsx` / `web/features/bio-crd-004/
CardObjectsApp.tsx` (additive `onChange` prop only — see "Building one snapshot from three
canvases" below), `tests/frontend/playwright/bio-crd-008-*.spec.ts`,
`tests/frontend/unit/bio-crd-008-*.test.ts`, `docs/features/bio-crd-008/**`,
`evidence/BIO-CRD-008/**`.

**One additive change to BIO-CRD-007 (Ngozi's own prior story).**
`api/features/bio-crd-007/Domain/ProductionJob.cs` gained exactly two new, optional, non-`required`
properties (`TemplateId`, `TemplateVersion`) — AC04's own "production references exact published
version." `ProductionService.CreateJobAsync` gained two new optional trailing parameters
(`templateId = null, templateVersion = null`), placed *after* `cancellationToken = default` so
every existing positional call site (every test in `tests/features/bio-crd-007/`) is completely
unaffected. Verified by rerunning BIO-CRD-007's own full suite (54/54 green, including the two new
tests this story adds for the additive fields).

**Shared, unlisted entry points touched** (the same role `Program.cs`/`module-registry.ts` play for
every story in this epic): `api/Biolog.Api/Program.cs`, `api/Biolog.Api/Biolog.Api.csproj`,
`web/lib/module-registry.ts`, `web/lib/feature-mode.ts`, `web/next.config.mjs`,
`web/app/card-designer/page.tsx` (mounts `TemplateLifecycleBar` alongside — not replacing —
BIO-UX-007's own `SaveDesignBar`), `tests/frontend/unit/bio-ux-005-feature-mode.test.ts` (its own
"documents the current shape" guard test updated), `scripts/quality.sh`.

## Design decisions

### The lifecycle: two different mechanisms for "content can't be lost"

`CardTemplate` (`api/features/bio-crd-008/Domain/CardTemplate.cs`) separates two concepts that are
easy to conflate:

- **`Version`** — the lifecycle-facing identity AC04 needs: assigned once when a row is created,
  never reused, never changes. `(TemplateId, Version)` is the exact pair a production job
  references.
- **`Revision`** — a plain optimistic-concurrency token (ADR-0001 §3.2) for in-place edits to one
  row while it is still Draft/Validation/Approval.

**AC01/AC02 rule:** while `Status` is Draft/Validation/Approval, `Edit` mutates the row in place
(bumping `Revision`) — normal pre-publish iteration; nothing has been published yet, so nothing
needs preserving. Once `Status` reaches Published or Retired, `Edit` always refuses
(`CardTemplateStatusExtensions.IsImmutable`) — the only way to change content from there is
`CreateNextDraftVersion`, which produces a brand-new row (a new `Version` under the same
`TemplateId` lineage), leaving the original exactly as it was, forever. Publishing a new version of
an existing lineage auto-retires whichever version was previously Published (`CardTemplateService.
PublishAsync`) — only one Published version per lineage at a time, matching the "one production
reference resolves unambiguously" requirement AC04 depends on.

### Vocabulary is mirrored, not gatewayed — except the one genuine cross-story dependency

`CardFormatKind`/`CardOrientation` (mirrors BIO-CRD-002's wire values — sequencing only, not a
declared dependency), `WatermarkPresetKind` (mirrors BIO-CRD-003), `BarcodeProfileKind` (mirrors
BIO-CRD-004) are all small, fixed enums this project owns independently, the same "not a declared
dependency, so own the vocabulary" shape BIO-RPT-002 established for `ReportDataset`. The **one**
genuine cross-story runtime read is a required signature rule: `ISignatureAssetGateway.Exists`
(implemented in `BioCrd008Module.cs`, wrapping BIO-CRD-004's own `ISignatureAssetRepository.
GetVersion` directly — both types are public, so no `InternalsVisibleTo` grant was needed, unlike
BIO-CRD-007's own `ICardIssueGateway`) — a template that claims to require a signature must
reference one that actually exists, checked live, not just structurally.

### Building one snapshot from three canvases (the AC05 gap god flagged)

`/card-designer` mounts three independent, pre-existing canvases (BIO-CRD-002's `CardDesignerApp`,
BIO-CRD-003's `CardTypographyApp`, BIO-CRD-004's `CardObjectsApp`), each with its own internal
`useState` and, per Kemi's ruling (`docs/features/bio-ux-007/README.md`), zero I/O of its own. God's
dispatch note confirmed this directly: `CardDesignerApp.tsx` had no save/load wiring at all — a
real, substantial gap, not just a backend lifecycle to add.

This story closes it **without breaking Kemi's ruling**: each of the three canvas components gained
one new, **optional**, additive `onChange` callback prop (default `undefined`, zero behaviour
change for any existing caller/test). Each component still does no I/O itself — it only reports its
own state upward via a `useEffect`. Only the **page** (`web/app/card-designer/page.tsx` — already
the shared, unlisted coordination point every sibling story in this conflict group edits) lifts
that state into local `useState`, and only this story's own new `TemplateLifecycleBar` component
reads it to build a `CardTemplateSnapshot`.

Building one snapshot from three still-independent canvases required a disclosed simplification:
until a later story merges them into one true front/back surface, every BIO-CRD-003/004 element
(name fields, watermarks, barcodes, signatures, logos) is placed on the **front** side of the
snapshot; BIO-CRD-002's own front/back split is preserved as-is. Every field AC03 names is still
captured — format/orientation/custom size, objects (as `CardTemplateElement`s), fonts (name-field
typography → `fontFamily`/`fontSizePx`), watermarks (inferred from the first watermark element's
`content` matching a known preset's text, defaulting to `none` for a custom watermark not tied to a
preset), photo spec (a small dedicated control on `TemplateLifecycleBar` itself, since no existing
canvas or declared dependency owns photo-spec UI), barcode profile and signature rule (the first
matching element of each kind on the BIO-CRD-004 canvas) — just not yet independently per side for
the two newer canvases. This is a disclosed scope boundary, not a hidden data-loss bug.

### AC04: enforcement lives at the source, not duplicated into another story's DI graph

`CardTemplateService.GetPublished` is the **one sanctioned source** of "the exact published
version" — it never returns a Draft/Validation/Approval/Retired row. `ProductionJob`'s new
`TemplateId`/`TemplateVersion` fields are a plain, optional pass-through on BIO-CRD-007's own job
creation (no new cross-feature gateway wired into BIO-CRD-007's own DI graph — that would be an
invasive change to another already-accepted story's module for a Draft-lifecycle story that only
needed to *record* a reference, not *validate* one at the point of consumption). The documented
workflow: resolve the exact published `(templateId, version)` via this story's own
`GET /card-templates/{id}/published` endpoint, then pass that exact pair into BIO-CRD-007's job
creation call. Enforcement that only a Published version is ever resolvable happens once, at the
one place that hands out the reference — not duplicated at every consumer.

## Issues found and fixed during development (not assumed correct, verified)

1. **Two `TryParse` overloads that differed only by `out` parameter type were genuinely ambiguous**
   at an `out var` call site (`CardFormatExtensions.TryParse(string?, out CardFormatKind)` vs.
   `TryParse(string?, out CardOrientation)`). The C# compiler cannot disambiguate two otherwise
   identical overloads using only the inferred `out var` type — a real compile error, caught before
   this project ever built. Fixed by renaming the orientation overload to `TryParseOrientation`.
2. **A missing `using` statement caused the wrong extension method to resolve.**
   `CardTemplateDtos.cs` called `actor.Type.ToWireString()` (an `Audit.ActorTypeExtensions` method)
   without importing the `Audit` namespace; the compiler instead found `Domain.
   BarcodeProfileExtensions.ToWireString(BarcodeProfileKind)` — a same-named extension method on
   the wrong type — and reported a confusing mismatch error. The exact same class of bug BIO-RPT-002
   hit in its own `BioRpt002Module.cs`. Fixed by adding the missing `using`.
3. **A DI lifetime bug was avoided proactively, not discovered the hard way.** `ReportJobRunner`'s
   own Singleton-depending-on-Scoped bug from BIO-RPT-002 was a direct lesson applied here from the
   start: `CardTemplateService` is registered `Scoped` (matching `ReportService`'s own precedent, not
   `ProductionService`'s), and depends only on Singletons (`ICardTemplateRepository`,
   `ISignatureAssetGateway`, `ICardTemplateAuditSink`) — no Singleton-capturing-Scoped edge exists.
   Confirmed by actually booting the real host early (`dotnet build api/Biolog.Api/Biolog.Api.csproj`
   right after wiring the module, not saved for a final manual pass) — clean, 0 errors.
4. **A real, live-only bug: the frontend sent camelCase JSON, the backend expected snake_case.**
   `web/features/bio-crd-008/live.ts`'s first draft of `transitionAction` re-serialised the raw
   frontend `TemplateVersionInput` object (`{ templateId, version, expectedRevision }`) directly as
   the POST body for submit/validate/reject/publish/retire. The backend's own request DTOs use
   `[JsonPropertyName("expected_revision")]`-style snake_case wire fields
   (`RevisionOnlyRequest`/`RecordValidationRequest`/`RejectRequest`) — `expectedRevision` never
   matched `expected_revision`, so `System.Text.Json`'s `required`-member check failed every one of
   these five actions with a 422 `invalid_format`, silently leaving the UI's status stuck on
   `draft`. **Not caught by any SIMULATION-mode test** (the mock adapters never round-trip through
   JSON at all) and not caught by the backend's own integration tests (which construct the request
   body correctly, in snake_case, directly). Only surfaced by actually running the real dual-server
   LIVE Playwright spec against the real backend — the second story in this repository (after
   BIO-RPT-002) where the LIVE proof itself is what catches a class of bug no other test layer can
   reach. Fixed by giving `transitionAction` a `toWireBody` builder per action, producing the exact
   snake_case body each endpoint expects.
5. **A stale, port-3000 dev server from a previous test run caused a false-negative test failure.**
   The first attempt at `tests/frontend/playwright/bio-crd-008-template-lifecycle.spec.ts` failed
   with "no `template-lifecycle-bar` element" — Playwright's own `reuseExistingServer` (true when
   not `CI`) had reused a *stale* `next dev` process still running old code from an earlier attempt,
   rather than picking up this story's own changes. Confirmed via a direct probe script
   (`page.on('pageerror')`/`page.getByTestId(...).count()`) that the component itself hydrates fine
   with zero runtime errors once a fresh server is used. Fixed by killing the stale process before
   rerunning — not a code bug, but worth recording: always confirm no leftover dev server is bound
   to the Playwright config's own port before trusting a failure as real.

## Test evidence

- **Backend unit/integration tests** (`dotnet test`, xunit): 29/29 passing
  (`tests/features/bio-crd-008/`) — full lifecycle happy path, every invalid transition refused,
  immutability enforced both at the domain layer and over real HTTP, AC02's new-version mechanics
  including auto-retire-on-republish, AC03's signature-asset existence gateway (both branches),
  custom-format validation, tenant isolation (cross-tenant lookup is 404 never a confirmed-403,
  empty listing under a different tenant), concurrency-conflict detection, and a structural
  regression test proving no route collision with any sibling card module.
- **BIO-CRD-007 regression** (Ngozi's own prior story, two additive fields + one additive optional
  parameter pair added): 54/54 passing, including the two new tests for the additive change.
- **API host build**: clean, 0 errors/warnings, `BioCrd008Module` registered and DI-validated at
  real host startup.
- **`tsc --noEmit` + `next build`**: PASS, zero errors.
- **Frontend unit tests** (vitest): 413/413 passing across 56 files, including 8 new
  `bio-crd-008-template-lifecycle-mock.test.ts` tests (missing-input guards, the full happy path,
  immutability refusing a second publish, AC02's new-version + auto-retire mechanics, a rejected
  validation returning to draft, an unknown template id, a stale-revision concurrency conflict) and
  the updated `bio-ux-005-feature-mode.test.ts` multi-port guard.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode, single-worker: 413/413 passing
  (4 new `bio-crd-008-template-lifecycle.spec.ts` tests: mounted alongside Save design, the full
  create→submit→validate→publish→retire→new-version lifecycle through the real UI, a rejected
  validation, axe zero-violations), 10 skipped-by-design LIVE-only tests across the repo (7
  pre-existing + 3 new). No regression across the pre-existing suite. `/card-designer` is not part
  of the visual-regression baseline set, so no snapshot update was needed.
- **AC05 LIVE-mode dual-server proof, actually run, not just asserted**: real `.NET` backend + a
  Next.js server built with `NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE=LIVE` baked in + a tenant
  domain registered and verified through BIO-TEN-002's own control plane (the second LIVE spec in
  this repository, after BIO-RPT-002, to need that step). 3/3 Playwright cases passed, plus direct
  `curl` verification of the full create→submit→validate→publish→(immutable 409)→new-version cycle
  against the real backend — this is exactly what surfaced issue #4 above before it ever reached
  independent review.

## Proving AC05 (exact commands)

```bash
# 1. Build and start the real .NET API host
dotnet build api/Biolog.Api/Biolog.Api.csproj --nologo
ASPNETCORE_URLS="http://127.0.0.1:5080" dotnet api/Biolog.Api/bin/Debug/net10.0/Biolog.Api.dll &

# 2. Register AND verify a tenant domain for the host the LIVE Next.js server will run on
#    (RegisterAsync alone leaves it PENDING_VERIFICATION; ResolveAsync only accepts VERIFIED)
curl -X POST http://127.0.0.1:5080/api/v1/control-plane/tenant-domains \
  -H "Content-Type: application/json" -H "X-Actor-Id: live-e2e-setup" \
  -d '{"hostname":"127.0.0.1","tenant_id":"ten_018f2e5a-1234-7abc-89ab-0123456789ab"}'
curl -X PUT http://127.0.0.1:5080/api/v1/control-plane/tenant-domains/127.0.0.1/status \
  -H "Content-Type: application/json" -H "X-Actor-Id: live-e2e-setup" \
  -d '{"version":1,"status":"VERIFIED"}'

# 3. Build and start Next.js with the LIVE flag baked in (Next.js inlines NEXT_PUBLIC_* at build time)
cd web
NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE=LIVE BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm exec next build
NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE=LIVE BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm exec next start -p 3002 -H 127.0.0.1 &

# 4. Run the LIVE spec against it
cd ../tests/frontend
BIOLOG_CRD008_LIVE_TESTS=1 BIOLOG_BASE_URL=http://127.0.0.1:3002 pnpm exec playwright test bio-crd-008-template-lifecycle-live.spec.ts --workers=1
```
