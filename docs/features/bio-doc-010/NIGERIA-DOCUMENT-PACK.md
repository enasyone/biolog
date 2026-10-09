# BIO-DOC-010 — Nigeria Document Intelligence Pack v1

**User story:** As BIOLOG, we need a governed Nigeria-first document-template pack that can expand
to West Africa, Africa and global coverage.

## Acceptance criteria and where each is met

| AC | Text | Where met |
|---|---|---|
| AC01 | Pack entries include country, issuer, document type, series/version, sides, expected fields, MRZ/barcode/NFC/security metadata and validation rules. | `Model/DocumentTemplatePackEntry.cs` (the full record shape, validated by `Create`); `Model/MrzMetadata.cs`, `Model/NfcMetadata.cs`, `Model/FieldValidationRule.cs`, `Model/DocumentTemplatePackSide.cs`; the actual governed content in `Data/NigeriaDocumentPackV1.cs` (passport, NIN slip, driver's licence). |
| AC02 | Templates are immutable once published. | `Model/DocumentTemplatePack.cs`: `Status` is a one-way `Draft` → `Published` transition (`Publish`); `AddEntry` and a second `Publish` both throw once published; there is no `RemoveEntry`/`ReplaceEntry`/`UpdateEntry` method at any lifecycle stage (ADR-0001 §3.3). |
| AC03 | Reference/test samples are governed and synthetic where required. | `Model/DocumentTemplateSample.cs` (`IsSynthetic` is a structural guarantee - `=> true`, no caller-supplied flag); `Store/IDocumentTemplateSampleStore.cs` (`UngovernedSampleEntryException` if a sample names an entry the pack doesn't govern); seeded samples in `Data/NigeriaDocumentPackV1.cs`. |
| AC04 | Pack version is recorded on every verification. | `Model/DocumentTemplateVerification.cs`'s `Verify` always durably audits (`Audit/DocumentTemplatePackAuditEventBuilder.BuildVerificationRecorded`) and returns a `DocumentTemplateVerificationOutcome` stamped with the exact `PackId`/`PackVersion`/`EntryId` it ran against, before ever returning. |
| AC05 | The approved web surface (`/document-verify`) is connected through `DocumentVerifyService` to this story's live implementation ... and the mapped end-to-end human workflow passes without an unapproved UI redesign. | `POST /api/v1/documents/identify` (`Api/DocumentTemplatePackEndpoints.cs`), the exact route the existing `document-identification` step's `live.ts` `LIVE_ENDPOINTS` entry already targeted since BIO-UX-009 (previously always unconnected). A new, independent `documentIdentification` port key (`web/lib/feature-mode.ts`) lets it go LIVE without forcing `capture` (which has no backend yet) live too. |

## Why this project's own declared dependencies are BIO-DOC-006 and BIO-DOC-009 only

- **BIO-DOC-006** (`Biolog.Features.DocumentClassifier`): `DocumentSide` is reused directly (AC01's
  "sides"). `DocumentTemplatePack.Publish` turns every entry's declared sides into a real
  `DocumentIntelligenceRegistryEntry` and registers it into a caller-supplied
  `DocumentIntelligenceRegistry` - publishing this pack is what makes its governed Nigeria
  identities resolvable to a classifier, honouring BIO-DOC-006's own monotonic-version
  supersession rule. `ExpectedSymbologies` is passed as `[]` (a target-typed collection
  expression) rather than duplicated per side - this project never names `BarcodeSymbology`
  anywhere in its own source, since barcode corroboration is BIO-DOC-006's own declared dependency
  (BIO-DOC-003), not this story's.
- **BIO-DOC-009** (`Biolog.Features.AuthenticityAssessment`): used narrowly, for its public
  `AuthenticityDisposition` enum only (`DocumentTemplateVerification.Verify`'s optional
  `correlatedAuthenticityDisposition` parameter). This project cannot construct a real
  `AuthenticityAssessmentResult` itself - that type's own locked-down `Classification` input
  requires a `DocumentClassificationOutcome`, whose constructor is `internal` to BIO-DOC-006
  (granted only to `Biolog.Api` and BIO-DOC-006's own tests), the same "cannot construct a
  sibling's locked-down aggregate from outside its own boundary" wall BIO-DOC-009's own `.csproj`
  comment documents for its own BIO-DOC-004/007 boundary. Cross-referencing an already-computed
  BIO-DOC-009 result therefore happens with plain primitives at the HTTP/service boundary, not the
  locked-down result type itself.

Deliberately NOT BIO-DOC-001/002/003/004/005/007/008: none is a declared dependency of this story.

## The governed Nigeria pack content (v1)

Three entries, chosen to exercise every AC01 metadata kind across the set:

- **Nigerian ePassport** (`nga-passport-2021`) — Nigeria Immigration Service, single front side,
  MRZ (TD3) supported, NFC (BAC) supported.
- **NIN Slip** (`nga-nin-slip-2014`) — National Identity Management Commission, single front side,
  neither MRZ nor NFC (paper slip).
- **Driver's Licence** (`nga-drivers-licence-2018`) — Federal Road Safety Corps, two sides (front +
  back), neither MRZ nor NFC (polymer card).

Every field value anywhere in `Data/NigeriaDocumentPackV1.cs` is a fabricated placeholder
(SYNTHETIC data only) — no real document numbers, names or identifiers.

## No real capture/upload/classification pipeline exists yet — the HTTP demo's own shape

`DocumentTemplatePackService.IdentifyNextDemo` cycles deterministically through three named
scenarios, one per pack entry (mirroring `AuthenticityAssessmentService`'s own demo-cycle shape),
so `POST /api/v1/documents/identify` returns a genuinely computed — not canned — verification
outcome every time, covering all three `DocumentTemplateVerificationState` values (Consistent,
Failed, Inconclusive) without needing a real capture/OCR pipeline (none exists yet — the same
disclosed boundary BIO-DOC-009's own demo fixtures already document for their own scope).

## Genuine HYBRID mode, not all-or-nothing (AC05)

`DocumentVerifyService` gains a third, independent port key, `documentIdentification`
(`web/lib/feature-mode.ts`'s `MODULE_PORT_KEYS`), the same mechanism BIO-DOC-009 established for
`authenticity`. It is deliberately its own key rather than sharing `primaryAction`'s mode for one
more reason beyond the usual "other six steps have no backend yet": `identifyDocument` is gated on
`capture` having already succeeded (`DocumentVerifyApp.tsx`'s own `requiresCapture` rule). If this
key shared `primaryAction`'s mode, switching it LIVE would also switch `capture` LIVE — and
`capture` has no backend of its own yet, so it would always fail and permanently block
`identifyDocument` from ever running. Keeping it independent lets `capture` stay on its SIMULATION
mock (always succeeds) while `documentIdentification` alone reaches the real governed-pack
backend.

No frontend routing/parsing code needed to change to connect it: `live.ts`'s own
`LIVE_ENDPOINTS['document-identification']` has pointed at `/api/v1/documents/identify` since
BIO-UX-009 (always unconnected until this story), and this endpoint's JSON response is a strict
superset of the `{ state, message, confidence? }` shape that step's generic live handler already
parses (the same reasoning BIO-DOC-009's own `AuthenticityAssessmentResponse` documented for
itself).

## Deliberately only one route, no GET-by-id (Zainab's BIO-DOC-009-F01 lesson applied from the start)

`DocumentTemplatePackEndpoints.Map` registers exactly one route: `POST /api/v1/documents/identify`.
`X-Actor-Id` is resolved via `ActorResolver.ResolveOrDefault` for audit-attribution only (ADR-0006
Decision 1/2) — never an authorization decision — and the route is otherwise open to any caller: it
returns only already-public, non-PII governed reference-pack metadata and cycles deterministically
through three fixed SYNTHETIC demo scenarios, the same shape Zainab's BIO-DOC-009-F01 review
approved for `AuthenticityEndpoints.ComputeAsync`.

## Proving AC05 (manual verification, actively excluded from the default full-suite run)

```bash
# Terminal 1 — the real .NET backend
cd api/Biolog.Api && dotnet run --urls http://127.0.0.1:5080

# Terminal 2 — a production Next.js build with document-identification's LIVE mode baked in
# (NEXT_PUBLIC_* vars are inlined at BUILD time, not read at runtime)
cd web
NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_DOCUMENTIDENTIFICATION=LIVE \
BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm build && pnpm start -p 3010

# Terminal 3 — the spec itself, single worker (see the spec file's own header comment for why),
# with BIOLOG_DOC010_LIVE_TESTS=1 to lift the test.skip guard
cd tests/frontend
BIOLOG_DOC010_LIVE_TESTS=1 BIOLOG_BASE_URL=http://127.0.0.1:3010 \
  npx playwright test bio-doc-010-identify-live.spec.ts --workers=1
```

Actually run (ports 5188/3011): all four tests passed (`4 passed`); the same two live specs
(BIO-DOC-009's and this one) were then re-run together with no live env vars set, confirming both
report `8 skipped`, never a failure, in the default configuration.

Also verified directly with `curl` against the running backend (all three demo scenarios, cycling
correctly; GET on the route returns `405`, not a hidden read surface).

## Tests

- `tests/foundation/BIO-DOC-010.test.sh` — structural checks mapped to every AC.
- `tests/features/bio-doc-010/Biolog.Features.DocumentTemplatePack.Tests/` — xUnit: entry
  construction/validation (AC01), publish/immutability/registry-registration (AC02), sample
  governance (AC03), verification/audit recording (AC04), the demo service (AC01/AC05), the real
  Nigeria pack seed content (AC01/AC03/AC04), and a standing reflection-based lockdown regression
  test.
- `tests/frontend/unit/bio-doc-010-documents-document-identification-port.test.ts` — the new
  independent port key.
- `tests/frontend/playwright/bio-doc-010-identify-live.spec.ts` — live, end-to-end (excluded from
  the default suite via `test.skip`, opted into via `BIOLOG_DOC010_LIVE_TESTS=1`).

## Security/privacy

- Synthetic data only — no real document numbers, names, NINs or licence numbers anywhere.
- No PII is stored or logged beyond the same SYNTHETIC placeholders the pack itself declares.
- The one HTTP route is read/validate-only, open to any caller (no differential authority), the
  same audit-attribution-only shape ADR-0006 sanctions and Zainab's BIO-DOC-009-F01 review already
  approved for this epic's other demo-cycle compute route.
