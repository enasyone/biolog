# BIO-DOC-009 — Builder evidence

**Story:** Build automatic authenticity assessment orchestrator
**Owner:** Ifeanyi — Document Intelligence Engineer
**Worktree:** `biolog-worktrees/BIO-DOC-009`, branch `feature/BIO-DOC-009` off `develop`
**Dependencies:** BIO-DOC-006 (DONE), BIO-DOC-008 (DONE)
**Conflict group:** `doc-auth`

## Round 2 — Zainab's security FAIL (High, BIO-DOC-009-F01), fixed

Zainab's security review FAILed the round-1 submission: `GET /api/v1/documents/authenticity/{id}`
had no actor/permission/tenant check at all (any caller who knew/guessed a session id got the full
assessment), and `POST .../{id}/override` treated a caller-supplied, non-authenticated `X-Actor-Id`
header as if it were authentication/authorization, letting any caller durably rewrite a disposition
under a forged identity. Her review's own remediation options were: build a real trusted-actor-
provenance/RBAC/tenant-context boundary, or keep the endpoints inaccessible until one exists.
Neither route is required by AC06 (only the compute route is UI-connected) or by AC05's own wording
(the override *capability*, not an HTTP surface, is what AC05 requires) — and building real RBAC ad
hoc for one story would itself be an unapproved shared-authorization-contract change. Fixed by
removing `GetAsync`/`OverrideAsync` and their route mappings entirely from `AuthenticityEndpoints.cs`
(not left unmapped-but-present) while keeping `AuthenticityAssessmentService.Get`/`ApplyOverride` as
the tested seam a later, authorization-boundary-building story maps real endpoints onto — the same
shape `DocumentModule` (BIO-DOC-001) already established for `IDocumentSessionStore`. Verified with
`curl`: both removed routes now 404 (not routed at all), the compute route still 200s.

Removing the HTTP handlers left `AuthenticityAssessmentService.Get`/`ApplyOverride` with no caller
anywhere - added `AuthenticityAssessmentServiceTests.cs` (7 new tests) to exercise them directly,
which caught a REAL, independent latent bug: `SyntheticAuthenticityFixtures` owned a single
process-wide *static* `IVersionStore`, which desynchronised from a caller's own per-session version
bookkeeping the moment more than one independent `AuthenticityAssessmentService` instance (one per
test) computed for the same well-known demo session id — BIO-DOC-008's own version-sequence check
correctly rejected the resulting stale claim. Fixed by having the fixtures take the version store/
audit sink as parameters instead of owning static singletons, so each service instance's state stays
consistent and isolated. Full suite re-verified after the fix: 54/54 xUnit (up from 47), 54/54
structural checks (6 new, naming the F01 fix directly), full `Biolog.Api` host still builds clean,
`scripts/check-module-project-references.sh` still PASS, frontend typecheck/build clean (untouched
by this round), and the live AC06 Playwright spec still 4/4 against the retrofitted backend.

## What was built (round 1)

A pure-policy orchestrator (`api/features/bio-doc-009/Biolog.Features.AuthenticityAssessment`)
that sits ON TOP of BIO-DOC-006's classifier and BIO-DOC-008's cross-comparison engine, computing
one explainable authenticity assessment: six individually-reported checks
(`AuthenticityCheckId`: MRZ, barcode, OCR, classification, cross-source, passive authentication)
plus one overall `AuthenticityDisposition` (`DocumentAuthentic`/`DocumentNotAuthentic`/
`Inconclusive`). `AuthenticityPolicyEngine.DetermineDisposition` structurally enforces AC02 —
promotion to `DocumentAuthentic` only ever reads `CrossSourceFieldComparison`/
`PassiveAuthentication`, never `BarcodeDecode` — and AC03's "not performed, not passed" via
`AuthenticityCheckOutcome.NotPerformed` being the enum's own safe default everywhere.

`AuthenticityAssessmentResult.Compute` mirrors BIO-DOC-008's own audit-before-commit discipline
(actor/timestamp/correlation-id validated, a durable audit event confirmed before returning,
ambiguous-write resolution via `ExistsAsync`) and records AC04's evidence/template/ruleset
versions — reusing BIO-DOC-008's own already-controlled `CrossComparisonResult.VersionNumber`
rather than minting a second counter. `AuthenticityAssessment` (the mutable, CAS-versioned
aggregate) and `AuthenticityManualOverride` implement AC05 the same two-tier shape BIO-DOC-001's
`DocumentConflict`/`ManualReviewState` established: an override is a separate field the aggregate
writes, never a mutation of the immutable `Result`/`Checks` — proved with a same-object-reference
assertion, not just equal-looking values.

AC06 required building this epic's first BIO-DOC HTTP surface: `POST/GET /api/v1/documents/
authenticity[/{id}[/override]]` (`Api/AuthenticityEndpoints.cs`), registered via
`api/Biolog.Api/Modules/BioDoc009Module.cs` (one new file + one new `Program.cs` line, the
established additive pattern). Connected the ALREADY-REVIEWED, unmodified BIO-UX-009 frontend
(`web/features/documents/live.ts`'s `checkAuthenticity`, unchanged) to this real backend by:
extending `DocumentVerifyService` to a genuine two-port module (`primaryAction` + a new
`authenticity` key, mirroring `ReportingService`'s own precedent) so the module reaches real
`HYBRID` — only the authenticity step is LIVE-capable, the other seven stay SIMULATION since no
backend exists for them yet; and adding a `next.config.mjs` rewrite forwarding this story's own
`/api/v1/documents/*` route family to the .NET host (no rewrite existed before — every prior
live-wiring story only ever observed its own fetch calls fail closed to `offline`, since no real
backend was ever actually running to reach).

## Why "individual checks" needed synthetic HTTP demo fixtures

`/document-verify` never uploads a real document or collects a document-session id (BIO-UX-009's
own design, unmodified) — the live `checkAuthenticity` call is a bare `POST` with no body.
`AuthenticityAssessmentService` resolves this with three named synthetic document sessions
(`SyntheticAuthenticityFixtures.cs`) cycled deterministically by a shared call counter, each
producing a genuinely computed (not canned) result: `FullyVerified` → `DocumentAuthentic` (two
independently-supplied fields agree), `BarcodeOnly` → `Inconclusive` (AC02's own scenario, proven
live over HTTP, not just in a unit test), `ConflictDetected` → `DocumentNotAuthentic` (a real field
mismatch). Manually verified end to end: three consecutive `curl` calls against a running host
returned exactly these three dispositions in order, and a fourth call (via the Next.js rewrite)
correctly proxied through to the same backend. See `docs/features/bio-doc-009/AUTHENTICITY-ORCHESTRATOR.md`
for the full table and the AC05 override transcript (`effective_disposition` flips while
`disposition`, the raw policy result, stays untouched).

## A real dependency-boundary constraint, worked within rather than around

This story's declared DEPENDENCIES are BIO-DOC-006 and BIO-DOC-008 only — not BIO-DOC-002/003/004/
005/007. Consequence: this project cannot itself construct a `CrossComparisonResult` with
`NfcCorroboration.Available == true` (needs a real BIO-DOC-007 `ChipReadOutcome`) or a
`DocumentClassificationOutcome` with a non-empty candidate (needs a real BIO-DOC-004
`TemplateOcrResult`) — both are locked-constructor types only their OWN declaring assembly, or a
caller holding the real upstream engine result, can produce. Rather than add an undeclared
`ProjectReference` to get around this, `AuthenticityPolicyEngineTests` proves the two
disposition-promoting checks' rules directly against `DetermineDisposition` using hand-built
`AuthenticityCheckResult` lists (enabled by this project's own self-contained
`InternalsVisibleTo` grant to its own test assembly — zero cross-story coupling). Documented in
this project's own `.csproj` comment and `AUTHENTICITY-ORCHESTRATOR.md`, not left as a silent gap.

## A real bug found by actually running the full suite, not by trusting the design

My first cut of `bio-doc-009-authenticity-live.spec.ts` only DOCUMENTED, in a comment, that it
needed the special two-server LIVE setup and was "not part of the default full-suite run" — it had
no actual code enforcing that. Running `scripts/quality.sh --full` caught this immediately: the
default `pnpm --dir tests/frontend test:e2e` run starts its own SIMULATION-only server (no
`NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE`), so `checkAuthenticity`
resolved to the mock adapter there and three of my spec's four tests failed against mock data
(`"Consistent"`/`"Not checked"` instead of the real backend's `"Authentic"`/`"Failed"`/
`"Inconclusive"`) instead of skipping cleanly. Fixed with an actual `test.skip(!process.env.
BIOLOG_DOC009_LIVE_TESTS, ...)` guard at the top of the file — re-ran the full default e2e suite
afterward and confirmed all four of this file's tests now report `skipped`, not `failed`, with zero
other regressions attributable to this story (see below).

## Test/quality evidence

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` — clean; confirms the new module registers into
  the full host without breaking any of the other 34 already-registered modules
  (`web-typecheck-build-output.txt`... see `dotnet-build-output.txt`).
- `dotnet test tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests` — 47/47
  pass (`dotnet-test-output.txt`).
- `bash tests/foundation/BIO-DOC-009.test.sh` — 48/48 structural checks pass
  (`foundation-test-output.txt`).
- `bash scripts/check-module-project-references.sh` — PASS (no `Biolog.Api.csproj` back-reference).
- `pnpm --dir web typecheck && pnpm --dir web build` — clean, `/document-verify` still
  prerendered among all 34 routes (`web-typecheck-build-output.txt`).
- `pnpm --dir tests/frontend test:unit` — 398/398 pass, including the new
  `bio-doc-009-documents-authenticity-port.test.ts` (6 tests) and ONE pre-existing test
  (`bio-ux-005-feature-mode.test.ts`'s "every module with more than one port key is a deliberate,
  noticed multi-port module") deliberately updated to include `DocumentVerifyService` — exactly
  what that test's own header comment says it exists to catch (`frontend-unit-test-output.txt`).
- `bash tests/frontend/playwright bio-ux-009-document-verify.spec.ts` (SIMULATION mode, unchanged
  server) — 13/13 still pass, confirming zero regression to BIO-UX-009's own already-accepted
  behaviour from this story's frontend changes (`frontend-e2e-simulation-output.txt`).
- **The real AC06 proof** — two servers running together (`dotnet run` on the real `Biolog.Api`
  host + a `next build`/`next start` production server with
  `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE` baked in and
  `BIOLOG_API_BASE_URL` pointed at the `.NET` host), `bio-doc-009-authenticity-live.spec.ts`
  (`--workers=1`, see that file's own header for why): 4/4 pass — three consecutive live calls
  cover all three real dispositions, the raw HTTP response carries genuine AC04 version fields,
  the evidence-comparison table reflects the live result, zero axe violations
  (`frontend-e2e-live-output.txt`).
- `pnpm --dir tests/frontend test:e2e` (the full default suite, re-run after the `test.skip` fix
  above) — 404 passed, 4 skipped (this story's own live spec, correctly), 1 failed
  (`frontend-full-e2e-suite-output.txt`). The 1 failure
  (`bio-ux-010-operational-journeys.spec.ts`, a visitor check-in timeout) is in a file this story
  never touches — verified pre-existing suite flakiness, not a regression: the FIRST
  `scripts/quality.sh --full` run (before the `test.skip` fix, still captured in
  `quality-output.txt`) failed a DIFFERENT, also-untouched test
  (`bio-ux-013-axe-baseline.spec.ts`'s `/audit` dark-mode check) instead — two different specs
  failing across two runs, neither one this story's own code, is exactly what pre-existing
  intermittent flakiness looks like, not a real defect this story introduced.
- `bash scripts/quality.sh --full` — full repo suite, output captured in full
  (`quality-output.txt`). The only OTHER failure recorded there,
  `Biolog.IntegrationTests.ObservabilityTests.AC04_a_secret_in_a_request_header_does_not_appear_anywhere_in_captured_output`,
  is the same pre-existing, nondeterministic flake already documented in this epic's own prior
  evidence (BIO-DOC-006's builder rounds) — unrelated to observability/logging, which this story
  never touches.

## Scope notes (deliberately not built)

- No real capture/upload pipeline — out of scope for every BIO-UX-009-connected story until a
  dedicated capture story exists; the three named synthetic demo sessions are this story's own
  honest, documented stand-in (see above), not a hidden shortcut.
- No `/document-verify` UI change — AC06 explicitly forbids an unapproved redesign;
  `DocumentVerifyApp.tsx`/`document-verify.module.css` (BIO-UX-009, already reviewed) are untouched.
- No per-step live wiring for MRZ/barcode/OCR/document-identification/NFC/selfie-comparison — each
  remains SIMULATION-only, genuinely HYBRID rather than forced all-or-nothing; those engines'
  own HTTP endpoints are each their own later story's job (the same "one story, one owned
  surface" discipline every prior BIO-DOC-00x story followed).
