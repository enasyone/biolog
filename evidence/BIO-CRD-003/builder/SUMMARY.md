# BIO-CRD-003 — Builder evidence

**Story:** Implement card typography, name fields and watermarks
**Owner:** Ngozi — Credential & Card Engineer
**Conflict group:** `card-designer`
**Dependencies:** BIO-CRD-002 (now merged to `develop`; sequencing and a read-only import of its
exported pure format helpers, `STANDARD_FORMATS`/`mmToPx` — no other coupling)

## Source hierarchy check

No conflict found between the Master Technical Specification, accepted ADRs (including the new
ADR-0006 asserted-actor accommodation, which this feature never uses — no capability is gated on
actor identity) and the approved story/AC text. No in-repo specification names exact starter
values for the STAFF/STUDENT/VISITOR watermark presets — the same "no v0.3 spec file actually
exists" gap BIO-CRD-001/BIO-PHO-001/BIO-CRD-002 each found and disclosed for their own citations
of it; the values chosen are this story's own, pinned by tests and documented in
`docs/features/bio-crd-003/README.md`.

## AC-to-evidence map

- **AC01 (each name field binds independently):** `web/features/bio-crd-003/nameFields.ts` —
  `NameFieldElement.binding` is a plain, freely-settable field independent of every other
  element's own binding; `setBinding` never touches typography. Tests:
  `bio-crd-003-name-fields.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC01).
- **AC02 (font family/size/colour/bold/italic/underline/case/alignment/spacing/opacity editable
  per field):** `web/features/bio-crd-003/typography.ts`'s `TypographyStyle` names all ten
  properties; `updateTypography` is the single validated entry point for any change. Tests:
  `bio-crd-003-typography.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC02).
- **AC03 (copy style does not copy data binding):** `nameFields.ts`'s `copyStyleTo(source,
  target)` — its own signature only ever reads `source.typography` and writes `target.typography`;
  it never reads or writes `.binding` on either argument. Tests: `bio-crd-003-name-fields.test.ts`
  (including an A→B→C chain-of-copies test), `bio-crd-003-card-typography.spec.ts` (AC03, a real
  UI flow: copy style from one field to another, assert the target's binding is unchanged).
- **AC04 (text/image watermarks support 0-100% opacity, rotation and layer control):**
  `web/features/bio-crd-003/watermarks.ts`'s `WatermarkElement` — opacity validated 0-100 both at
  construction and on every later `setOpacity` call; rotation normalised to `[0,360)` at
  construction (a real gap found and fixed during development — see "Decisions" below); layer
  control inherited from the shared, generic `canvasElementOps.ts` base since a watermark is a
  `BaseElement` like any other. Tests: `bio-crd-003-watermarks.test.ts`,
  `bio-crd-003-canvas-element-ops.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC04).
- **AC05 (STAFF/STUDENT/VISITOR starter watermark behaviors available):**
  `WatermarkPresetCatalog.cs` (backend, served via `GET /api/v1/card-designer/watermark-presets`)
  and `WATERMARK_PRESETS` (frontend, mirrored by hand) both name the three presets with their
  default text/opacity/rotation; `createWatermarkFromPreset` builds a starting watermark from any
  of them, still freely editable afterwards. Tests: `WatermarkPresetCatalogTests.cs`,
  `WatermarkPresetEndpointsIntegrationTests.cs`, `bio-crd-003-watermarks.test.ts`,
  `bio-crd-003-card-typography.spec.ts` (AC05).

## Decisions and assumptions (read `docs/features/bio-crd-003/README.md` for the full reasoning)

- **Two coordinated tools on one shared route, not yet one merged canvas.** This story's write
  scope is `web/features/bio-crd-003/**` only. Rather than reopening BIO-CRD-002's own
  `CardDesignerApp.tsx`/`elements.ts`/`cardDesignState.ts` (outside this story's declared scope),
  `CardTypographyApp.tsx` renders as an independent second section on the shared, unowned
  `web/app/card-designer/page.tsx` (the same role `Program.cs` plays for backend stories),
  composed alongside BIO-CRD-002's unchanged canvas.
- **`canvasElementOps.ts` is a fully independent, generic copy** of BIO-CRD-002's own
  move/resize/rotate/lock/layer logic, not an import of it — TypeScript has no C#-style one-way
  `ProjectReference` enforcement, so importing across the story boundary would make this story's
  correctness depend on a file outside its own write scope. The one legitimate reuse of
  BIO-CRD-002 is a read-only import of its already-exported pure `cardFormats.ts` helpers to size
  this story's own canvas.
- **A real bug found and fixed during development:** the initial `createWatermarkElement` stored
  `rotationDeg` verbatim instead of normalising it to `[0, 360)` at construction (the invariant
  was only actually enforced inside `rotateElement`'s own mutation path). A preset-derived
  watermark (e.g. VISITOR, default rotation `-30`) would have violated its own documented
  invariant from the moment it was created. Fixed by calling the now-exported `normaliseDegrees`
  inside `createWatermarkElement` itself, not by weakening the test that caught it.
  `bio-crd-003-watermarks.test.ts` pins the fix.
- **A real cross-story accessibility regression found and fixed via real-browser verification:**
  after composing this story's own `<aside>` sidebar onto the shared `/card-designer` route
  alongside BIO-CRD-002's own `<aside>`, axe correctly flagged both as indistinguishable
  landmarks (`landmark-unique`, moderate impact) — a defect that only exists once both tools share
  one page. Fixed within this story's own file only: `aria-label="Card typography layers and
  properties"` added to this story's own `<aside>`.
- **A real cross-story Playwright regression found and fixed via a full-suite rerun:** running
  the whole `tests/frontend/playwright/` suite together (not just this story's own spec) revealed
  that BIO-CRD-002's own `bio-crd-002-card-designer.spec.ts` broke — its unscoped
  `getByText('Select an element to edit')` locator started matching two elements once this
  story's own "no selection" hint paragraph, which began with the same words, was added to the
  same page. `bio-crd-002-card-designer.spec.ts` is BIO-CRD-002's own declared file, outside this
  story's write scope, so it was not edited. Instead this story's own hint text was reworded (no
  behavioural change) to remove the collision. Re-verified: the full BIO-CRD-002 +
  BIO-CRD-003 Playwright run (26/26) and the full `quality.sh --full` Playwright step (42/42)
  both pass clean afterwards.
- **Backend is deliberately lean**, matching BIO-CRD-002's own precedent: a stateless,
  non-tenant-scoped preset catalogue only — no AC asks for server-side persistence of typography,
  bindings or watermark placement, so no tenant context, repository, lock registry or audit sink
  exists anywhere in this feature.
- **"Dependencies: BIO-CRD-002" is sequencing plus one narrow, deliberate, read-only reuse** — not
  a runtime coupling. Confirmed by direct inspection: no BIO-CRD-002 file outside `cardFormats.ts`
  is referenced anywhere in this feature's code.

## Test results (commit `28418d6d8685d15934606b03cbd2bcc56155e95f`)

- `bash tests/features/bio-crd-003/BIO-CRD-003.test.sh` — 28/28 PASS
- `dotnet test tests/features/bio-crd-003/Biolog.Features.CardTypography.Tests.csproj` — 8/8 PASS
- `pnpm --dir tests/frontend test:unit` — 96/96 PASS (39 new BIO-CRD-003 tests + 57 pre-existing,
  unaffected)
- `pnpm --dir tests/frontend test:e2e` (verified directly against a scratch-port `next dev`
  instance, since port 3000 was occupied by an unrelated stray process throughout development) —
  BIO-CRD-003 spec alone: 12/12 PASS, including zero axe violations. BIO-CRD-002 + BIO-CRD-003
  together: 26/26 PASS (confirms the cross-story regression described above is fully resolved).
  Full suite via `scripts/quality.sh --full`: 42/42 PASS.
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 200/200 PASS
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 104/104 PASS
  when run alone or filtered to `ObservabilityTests` only (9/9). Inside `scripts/quality.sh
  --full`'s combined parallel run, `ObservabilityTests` produced a single failure on **three
  separate full-suite runs**, each time a **different** specific test method
  (`AC04_a_plain_string_under_a_generic_name_is_withheld_by_default_through_the_real_wired_console_provider`,
  then `AC04_a_classified_object_under_a_generic_name_is_withheld_by_the_real_wired_console_provider`,
  then `AC01_a_real_request_logs_correlation_and_tenant_scope_to_the_console_view`), always the
  identical `System.ArgumentOutOfRangeException` inside `StringBuilder.ToString()`. This is the
  same pre-existing flake documented in BIO-PHO-002's and BIO-CRD-002's own builder evidence — a
  race in `ObservabilityTests`' own shared console-capture buffer under xUnit's parallel test
  execution, in a file this story (and BIO-CRD-002 before it) never touches, owned by BIO-FND-009.
  Not a BIO-CRD-003 regression; not this story's file to fix.
- `pnpm typecheck` (`web/`) — clean, 0 errors
- `next build` (`web/`) — succeeds, `/card-designer` still generated as a single static route
  composing both tools
- `bash scripts/check-module-project-references.sh` — PASS
- `python3 scripts/check-traceability.py --all` — PASS (23 implemented stories; this story is
  correctly outside the `governance/stories/*.json` gate, same as every project-per-feature
  sibling)
- `bash scripts/quality.sh` (base tier) — **PASS**, see `evidence/BIO-CRD-003/builder/quality-output.txt`
- `bash scripts/quality.sh --full` (run with `BIOLOG_BASE_URL` pointed at a scratch-port `next
  dev` instance in this worktree) — every check this story owns passes: `BIO-CRD-003 card
  typography harness` PASS, `.NET card typography unit tests` PASS, `frontend unit tests` PASS
  (96/96), `Playwright tests` PASS (42/42), `.NET vulnerability audit` PASS, `.NET unit tests`
  PASS (200/200), `Flutter tests` PASS. The only failing line is `.NET integration tests`,
  entirely attributable to the pre-existing `ObservabilityTests` flake described above. See
  `evidence/BIO-CRD-003/builder/quality-full-output.txt`.

Full transcripts captured alongside this file.
