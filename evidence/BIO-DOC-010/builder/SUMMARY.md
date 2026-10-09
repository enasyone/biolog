# BIO-DOC-010 — Builder evidence

**Story:** Implement Nigeria Document Intelligence Pack v1
**Commit:** `cfef942448006d324cb264b76ca63bf4868c06ef`
**Dependencies:** BIO-DOC-006 (both DONE), BIO-DOC-009 — both statuses confirmed DONE before start.

## What was built

A governed, versioned Nigeria-first document-template pack, built as a new feature project
(`api/features/bio-doc-010/Biolog.Features.DocumentTemplatePack`) on top of BIO-DOC-006's
`DocumentIntelligenceRegistry` and reading BIO-DOC-009's public `AuthenticityDisposition` enum.

- **AC01** — `DocumentTemplatePackEntry` carries country, issuer, document type, series/version,
  sides (each naming a BIO-DOC-006 template id), expected fields, MRZ/NFC metadata, security
  features and validation rules. `Create` structurally validates all of it (non-empty sides,
  non-duplicate sides, non-empty/non-duplicate expected fields, every validation rule names a
  declared field). The actual governed content (`Data/NigeriaDocumentPackV1.cs`) covers three real
  Nigerian document identities: the ePassport, the NIN slip, and the driver's licence (the only one
  with two sides, front + back).
- **AC02** — `DocumentTemplatePack` is `Draft` → `Published`, one-way. `AddEntry` and a second
  `Publish` both throw once published; there is no `RemoveEntry`/`ReplaceEntry`/`UpdateEntry`
  method at any lifecycle stage (proved by reflection in a dedicated test, not just by inspection).
  `Publish` durably audits before flipping state (the same audit-before-commit pattern every
  hard-audited feature in this epic uses) and registers every entry's sides into a caller-supplied
  `DocumentIntelligenceRegistry` — publishing this pack is what makes its governed identities
  resolvable to a classifier.
- **AC03** — `DocumentTemplateSample.IsSynthetic` is `=> true`, a structural guarantee with no
  caller-supplied flag to flip. `IDocumentTemplateSampleStore.Add` rejects a sample naming an entry
  the pack doesn't govern (`UngovernedSampleEntryException`). Three seeded samples exist, one per
  entry, and a dedicated test proves each one's field values actually satisfy its own entry's rules
  (a "happy path" sample that would itself fail validation would be a contradiction in the
  governance this story exists to provide).
- **AC04** — `DocumentTemplateVerification.Verify` always durably records
  (`document_template_pack.verification_recorded`) the exact `PackId`/`PackVersion`/`EntryId` it ran
  against before ever returning a `DocumentTemplateVerificationOutcome`. Refuses to run against an
  unpublished pack (`PackNotPublishedException`) or an entry the pack doesn't declare
  (`EntryNotInPackException`).
- **AC05** — `POST /api/v1/documents/identify` connects the `document-identification` step — the
  exact route `web/features/documents/live.ts`'s `LIVE_ENDPOINTS` has named since BIO-UX-009,
  always unconnected until now. `DocumentVerifyService` gains a third, independent port key
  (`documentIdentification`), kept separate from `primaryAction` because `identifyDocument` is
  gated on `capture` having already succeeded, and `capture` has no backend yet — sharing
  `primaryAction`'s mode would have permanently blocked this step from ever running once switched
  live. `DocumentVerifyApp.tsx` and its stylesheet are untouched (no UI redesign); the response is a
  strict superset of the frontend's existing `{ state, message, confidence? }` shape, so no
  frontend parsing code needed to change either.

## Why this project's own declared dependencies are BIO-DOC-006 and BIO-DOC-009 only

See `docs/features/bio-doc-010/NIGERIA-DOCUMENT-PACK.md`'s own section on this. In short:
BIO-DOC-006's `DocumentSide` is reused directly and `Publish` registers into its registry;
BIO-DOC-009's `AuthenticityDisposition` enum is read (never constructed as a full result — that
type's locked-down `Classification` input requires an `internal`-constructor type this project has
no access to, the same wall BIO-DOC-009 itself documents for its own BIO-DOC-004/007 boundary).

## Applying BIO-DOC-009-F01's lesson from the start

Zainab's earlier finding on BIO-DOC-009 (unauthenticated GET-by-id, an authorization-decision use
of `X-Actor-Id`) is now also codified in ADR-0006 (post-freeze addition, read directly before
building this story's own HTTP surface). This story's endpoint was designed to comply from the
start rather than needing a round-2 fix: exactly one POST route, no GET-by-id, `X-Actor-Id` used
for audit attribution only (never gates capability), the route otherwise open to any caller because
it returns only already-public governed reference-pack metadata and cycles through fixed SYNTHETIC
demo scenarios.

## A dependency-boundary discovery, worked within rather than around

Initial design considered wiring `document-identification` through BIO-DOC-006's own `Classifier`
directly. That's infeasible without also depending on BIO-DOC-002/003/004 (MRZ/barcode/OCR evidence
types `Classifier.Classify` requires) — undeclared dependencies for this story. Resolved by making
this story's own `DocumentTemplateVerification.Verify` (field-validation-rules based) the real
"verification" capability the live endpoint demos, rather than reaching past the declared
dependency boundary.

## A frontend gotcha found and designed around, not hit at runtime

`identifyDocument` is gated on `capture` having already succeeded
(`DocumentVerifyApp.tsx`'s own `requiresCapture` rule). If `documentIdentification` had shared
`primaryAction`'s mode (the simplest-looking option), switching it LIVE would also switch `capture`
LIVE — and `capture` has no backend of its own yet, so it would always fail and permanently block
`identifyDocument` from ever running. Caught during design (by reading `DocumentVerifyApp.tsx`
directly, not assumed), fixed by giving `documentIdentification` its own independent port key
before writing any frontend code, mirroring — and explaining, in `feature-mode.ts`'s own comment —
the exact mechanism BIO-DOC-009 established for `authenticity`.

## Test/quality evidence

- `dotnet-build-output.txt` — `dotnet build api/Biolog.Api/Biolog.Api.csproj`: 0 errors, 0 warnings.
- `dotnet-test-output.txt` — the feature's own xUnit suite: 41/41 passed.
- `foundation-test-output.txt` — `tests/foundation/BIO-DOC-010.test.sh`: 42/42 structural checks passed.
- `module-reference-check-output.txt` — `scripts/check-module-project-references.sh`: PASS.
- `web-typecheck-build-output.txt` — `pnpm build` (production build + TypeScript): clean.
- `frontend-unit-test-output.txt` — `pnpm --dir tests/frontend test:unit`: 56 files, 419 tests, all passed.
- `frontend-e2e-live-output.txt` — the real two-server LIVE proof (`bio-doc-010-identify-live.spec.ts`
  against a production Next.js build with `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_DOCUMENTIDENTIFICATION=LIVE`
  and a real running `Biolog.Api`): 4/4 passed, covering all three real computed states
  (Consistent/Failed/Inconclusive), the real `pack_id`/`pack_version`/`entry_id` fields on the wire,
  the evidence table reflecting the live result, and no axe violations.
- `frontend-full-e2e-suite-output.txt` — the default `pnpm --dir tests/frontend test:e2e` run:
  412 passed, 14 skipped (includes both this story's own and BIO-DOC-009's live specs, correctly
  skipped — not failed — without their opt-in env vars), 1 failed
  (`bio-ux-011-admin-journeys.spec.ts` "Audit has no axe violations" — see below).
- `quality-output.txt` — the full `scripts/quality.sh --full` run.

**Two classes of pre-existing flakiness observed, neither caused by this story (verified by
isolated re-run):**

- An initial full run (both of the quality gate and of the e2e suite alone) hit a wide wave of
  timeouts across dozens of unrelated specs (bio-ux-006 through bio-ux-015). Root cause found: a
  stale Next.js dev server process, left running on port 3000 from an earlier session, had been
  reused by Playwright's own `reuseExistingServer` option across runs and was in a degraded state.
  Killing it and letting Playwright start a fresh server made every one of those specs pass again
  (`docs/features/bio-doc-010/NIGERIA-DOCUMENT-PACK.md` doesn't document this — it's an environment
  fact, not a story fact — but it fully explains that wave; none of it touched Document Verify code).
- With a fresh server, two further single-spec flakes surfaced on different runs
  (`bio-ux-009-document-verify.spec.ts`'s human-review-queue tests once; `bio-ux-011-admin-journeys.spec.ts`'s
  "Audit has no axe violations" once). Neither is in this story's own owned files. Both were re-run
  in isolation (3x repeat each) and passed every time — consistent with the same class of
  environment-load-dependent flakiness BIO-DOC-009's own evidence already documented for this
  codebase, not a regression this story introduced.
- `.NET mrz-engine unit tests` (`MrzRegionDetector.FindColumnCells`, BIO-DOC-002 — not a file this
  story touches) threw `InvalidOperationException: Collection was modified` once, under the full
  suite's parallel test execution. Re-ran in isolation 3x: passed every time. A genuine pre-existing
  latent bug in already-accepted BIO-DOC-002 code, out of this story's scope to fix.

## Scope notes (deliberately not built)

- No live wiring for the cross-reference between a computed BIO-DOC-009 `AuthenticityAssessmentResult`
  and this pack's version (`correlatedAuthenticityDisposition` exists and is tested, but no HTTP
  route exercises it): BIO-DOC-009's own current demo fixtures always compute with
  `Classification: null`, so `TemplateId` is always `null` on that path today — building HTTP
  surface for a cross-reference that can never fire against the only real caller today would be
  dead surface, not evidence. Kept as a tested service-layer seam, the same "seam kept, endpoint
  exposure deferred" shape this epic already established twice (BIO-DOC-001's `IDocumentSessionStore`,
  BIO-DOC-009's own `Get`/`ApplyOverride`).
- No draft/test/approval workflow for the pack lifecycle (just Draft→Published) — AC02 only
  requires immutability once published; the fuller multi-stage lifecycle is explicitly BIO-DOC-011's
  own acceptance criterion, not this story's.
