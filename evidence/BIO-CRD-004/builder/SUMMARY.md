# BIO-CRD-004 — Builder evidence

**Story:** Implement dynamic barcode/QR/signature/card objects
**Owner:** Ngozi — Credential & Card Engineer
**Conflict group:** `card-designer`
**Dependencies:** BIO-CRD-002 (now merged; sequencing plus one narrow, deliberate, read-only
import of its exported pure `cardFormats.ts` helpers — no other coupling)

## Source hierarchy check

No conflict found between the Master Technical Specification, accepted ADRs and the approved
story/AC text. ADR-0001's registered-identifier list (`ten`/`per`/`crd`/`dev`/`dcs`/`evt`, "a
feature story may not invent a prefix locally") was checked explicitly before designing
`SignatureAsset` — it deliberately mints no new identifier type, following `PhotoMaster`'s and
`BrandAsset`'s own precedent of a natural composite key instead. No in-repo specification names
exact barcode quiet-zone/size/contrast thresholds — the same "no v0.3 spec file exists" gap every
prior card-designer sibling found and disclosed for its own catalogue; this story's own values
are disclosed and pinned by tests (see `docs/features/bio-crd-004/README.md`).

## AC-to-evidence map

- **AC01 (default ID-card barcode profile is alphanumeric):** `BarcodeProfileCatalog.cs` (backend)
  and `DEFAULT_BARCODE_PROFILE` (frontend) both pin `Alphanumeric`; `createBarcodeElement` starts
  every new barcode object on it. Tests: `BarcodeProfileCatalogTests.cs`,
  `bio-crd-004-barcode-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC01).
- **AC02 (QR/PDF417/Data Matrix/Aztec are optional, never default):** `OptionalProfiles`/
  `OPTIONAL_BARCODE_PROFILES` name exactly the four 2D symbologies; each has its own explicit
  toolbar button — none is ever auto-selected. Tests: `BarcodeProfileCatalogTests.cs`,
  `bio-crd-004-barcode-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC02).
- **AC03 (barcode objects validate quiet zone/size/contrast):** `BarcodeValidation.cs`/
  `barcodeObjects.ts`'s `validateBarcode` — three independent checks aggregated into named
  violations, never an exception for a well-formed request (mirrors BIO-CRD-002-AC03's own
  "validation is data, not an HTTP error" convention). Tests: `BarcodeValidationTests.cs`,
  `bio-crd-004-barcode-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC03).
- **AC04 (signature assets are versioned):** `SignatureAsset.cs`/`ISignatureAssetRepository` —
  immutable, append-only per ADR-0001 §3.3, tenant-scoped, versioned by `(TenantId, LineageId,
  Version)` starting at 1, optimistic-retry version assignment (no lock class needed). A canvas
  `SignatureElement` pins a specific version, never "always latest". Tests:
  `SignatureAssetRepositoryTests.cs` (including a 50-concurrent-append race-safety test),
  `SignatureAssetEndpointsIntegrationTests.cs` (including real-HTTP cross-tenant isolation),
  `bio-crd-004-signature-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC04).
- **AC05 (conditional visibility supports profile/card class rules):** `visibility.ts`'s
  `VisibilityRule` — independent optional card-class and barcode-profile allowlists, combined
  with AND, `null` meaning unrestricted; shared by every card object this story defines (barcode,
  signature, logo). Tests: `bio-crd-004-visibility.test.ts`, `bio-crd-004-card-objects.spec.ts`
  (AC05).

## Decisions and assumptions (read `docs/features/bio-crd-004/README.md` for the full reasoning)

- **A third coordinated tool on the shared `/card-designer` route.** This story's write scope is
  `web/features/bio-crd-004/**` only. `CardObjectsApp.tsx` renders as a third independent section
  beneath BIO-CRD-002's canvas and BIO-CRD-003's typography tool on the shared, unowned
  `web/app/card-designer/page.tsx` — never reopening either sibling's own component.
- **`canvasElementOps.ts` is a third independent, generic copy** of the same move/resize/rotate/
  lock/layer logic BIO-CRD-002's and BIO-CRD-003's own files already implement — not an import,
  for the same write-scope-isolation reasoning BIO-CRD-003's own README already established. The
  one legitimate reuse of a sibling story is a read-only import of BIO-CRD-002's exported
  `cardFormats.ts` helpers.
- **Signature-asset versioning deliberately follows BIO-CRD-001's own `BrandAsset` precedent**
  (tenant-scoped, in-memory, optimistic-retry, no audit sink or lock registry) rather than
  BIO-PHO-001's heavier `PhotoMaster` audit-atomicity/lock-registry apparatus — no AC here asks
  for an audit trail, and no cross-story identity existence-gateway dependency is declared for
  this story. This was a deliberate architectural choice made before writing any code, checked
  against both in-epic precedents.
- **No canonical identifier type minted for a signature asset** — ADR-0001 registers exactly six
  types and forbids a story from inventing a local prefix; `SignatureAsset` is keyed by the
  natural composite `(TenantId, LineageId, Version)`, matching `PhotoMaster`'s and `BrandAsset`'s
  own "no canonical id of its own kind" choice.
- **A real accessibility regression found and fixed via a full cross-story Playwright run:**
  running all three card-designer specs together (not just this story's own) surfaced no new
  regression this time, but the check was still performed proactively — the exact lesson recorded
  in BIO-CRD-003's own evidence after that story's own composition broke a sibling's test. This
  story's own `<aside>` carries a unique `aria-label` from the start, applying that lesson
  pre-emptively rather than discovering the same `landmark-unique` violation again.
- **A real test-authoring bug found and fixed before committing:** `getByLabel('Barcode profile')`
  originally collided with a separately-added "Preview barcode profile" select and a "Restrict to
  specific barcode profiles" checkbox (Playwright's default substring, case-insensitive label
  matching). Fixed by renaming the two non-primary labels ("Preview symbology", "Restrict to
  specific symbologies") rather than weakening the test with `exact: true` (which would not have
  worked anyway, since the target `<select>`'s own computed accessible name concatenates its
  option text). The same reasoning applied to "Lineage id"/"Version", which legitimately appear
  twice (toolbar vs. properties panel) — the toolbar's own inputs were renamed "New signature
  lineage id"/"New signature version" instead.

## Test results (commit `b8d57e6c747d5cda56af6991002108e10470f2f0`)

- `bash tests/features/bio-crd-004/BIO-CRD-004.test.sh` — 36/36 PASS
- `dotnet test tests/features/bio-crd-004/Biolog.Features.CardObjects.Tests.csproj` — 45/45 PASS
- `pnpm --dir tests/frontend test:unit` — 133/133 PASS (37 new BIO-CRD-004 tests + 96
  pre-existing, unaffected)
- `pnpm --dir tests/frontend test:e2e` (verified against a scratch-port `next dev` instance on
  port 3101, since port 3000 was occupied by the same unrelated stray process documented in every
  prior card-designer story's own evidence, and port 3100 was in use by this session's own
  BIO-CRD-003 worktree dev server) — BIO-CRD-004 spec alone: 13/13 PASS, including zero axe
  violations. All three card-designer specs together (BIO-CRD-002 + BIO-CRD-003 + BIO-CRD-004):
  39/39 PASS, confirming no cross-story regression this time. Full suite via `scripts/quality.sh
  --full`: 55/55 PASS.
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 200/200 PASS
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 104/104
  PASS when run alone. Inside `scripts/quality.sh --full`'s combined parallel run,
  `ObservabilityTests` produced 2 failures this run
  (`AC04_a_secret_in_a_request_header_does_not_appear_anywhere_in_captured_output`,
  `AC04_a_forged_framework_category_does_not_grant_trust_through_the_real_wired_console_provider`),
  both the identical `System.ArgumentOutOfRangeException` inside `StringBuilder.ToString()` — the
  same pre-existing flake documented in every prior card-designer sibling's own builder evidence
  (BIO-PHO-002, BIO-CRD-002, BIO-CRD-003), a race in `ObservabilityTests`' own shared
  console-capture buffer under xUnit's parallel test execution, in a file this story never
  touches, owned by BIO-FND-009. Not a BIO-CRD-004 regression; not this story's file to fix.
- `pnpm typecheck` (`web/`) — clean, 0 errors
- `next build` (`web/`) — succeeds, `/card-designer` still generated as a single static route
  composing all three tools
- `bash scripts/check-module-project-references.sh` — PASS
- `python3 scripts/check-traceability.py --all` — PASS (24 implemented stories; this story is
  correctly outside the `governance/stories/*.json` gate, same as every project-per-feature
  sibling)
- `bash scripts/quality.sh` (base tier) — **PASS**, see `evidence/BIO-CRD-004/builder/quality-output.txt`
- `bash scripts/quality.sh --full` — every check this story owns passes: `BIO-CRD-004 card
  objects harness` PASS, `.NET card objects unit tests` PASS, `frontend unit tests` PASS
  (133/133), `Playwright tests` PASS (55/55), `.NET vulnerability audit` PASS, `.NET unit tests`
  PASS (200/200), `Flutter tests` PASS. The only failing line is `.NET integration tests`,
  entirely attributable to the pre-existing `ObservabilityTests` flake described above. See
  `evidence/BIO-CRD-004/builder/quality-full-output.txt`.

Full transcripts captured alongside this file.
