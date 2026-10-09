# BIO-OPS-005 — builder remediation of SY-OPS005-01 (High)

**Blocked verdict:** Seyi, SY-OPS005-01 (High) — "no shipped Document Verify operation composes
the named evidence engines" (see `evidence/BIO-OPS-005/builder/BLOCKED.md`).

**Ruling:** Kemi, dispatched via god on 2026-10-07 — full ruling quoted in
`docs/features/bio-ops-005/DOCUMENT-VERIFY-COORDINATOR.md`'s own section headers; followed
exactly (points 1–8).

## What was built

- `api/features/bio-ops-005/Biolog.Features.DocumentVerifyCoordination` — new feature project.
  Direct `ProjectReference` to BIO-DOC-001/002/003/004/007/008 (point 2). `DocumentVerifyCoordinatorService`
  composes a session (BIO-DOC-001), real MRZ/barcode/OCR reads (BIO-DOC-002/003/004) against a
  deterministic synthetic document (`Fixtures/SyntheticDocumentFixture.cs`, point 6), real
  cross-source comparison (BIO-DOC-008), and reaches BIO-DOC-009/010 through two new gateways
  (`IAuthenticityAssessmentGateway`/`IDocumentIdentificationGateway`).
- `Biolog.Api.Modules.BioOps005Module` — maps the 8 endpoints
  (`DocumentVerifyCoordinatorEndpoints.cs`) and implements both gateways as adapters wrapping
  BIO-DOC-009's/BIO-DOC-010's own already-registered services directly (point 2) — no
  `ProjectReference` from the coordinator project to either.
- `AuthenticityAssessmentService.ComputeFromEvidence` (BIO-DOC-009) and
  `DocumentTemplatePackService.IdentifyFromEvidence` (BIO-DOC-010) — new overloads added
  ALONGSIDE each service's own existing demo-cycling method, never replacing it (point 3).
- `api/Biolog.Api/Biolog.Api.csproj` — direct `ProjectReference`s to BIO-DOC-002/003/004/007/008
  and the new coordinator project (the composition Seyi's own finding named as missing).
  `api/Biolog.Api/Program.cs` — registers `BioOps005Module`.
- `api/Biolog.Api/Modules/DocumentModule.cs` — corrected its own stale "no real tenant context
  yet" doc comment (Kemi's ruling flagged this directly; BIO-TEN-002 has since shipped real
  resolution and BIO-DOC-009/010/011 already use it successfully).
- `web/features/documents/evidence.ts` — `EvidenceScenario`/`EvidenceStepResult` gained a
  REQUIRED `confidence: number` (dropped the `?`) and a REQUIRED `provenance: { source,
  evidenceSequenceNumber?, fieldEvidenceSequenceNumbers? }` (point 7); every mock scenario in
  `SCENARIOS` updated to supply both.
- `web/features/documents/live.ts` — `LiveStepBody`/`makeLiveStepAction` updated to require and
  forward both new fields; doc comments corrected (all 8 steps are now genuinely live-reachable,
  not 2 of 8).
- `tests/features/bio-ops-005/Biolog.Features.DocumentVerifyCoordination.Tests` — new xUnit
  project, the end-to-end synthetic test point 8 requires: success, a genuine cross-source
  mismatch → manual review, unavailable NFC, a PERSON proposal accepted and one left unaccepted,
  replay, and concurrent review (see test names below).
- `tests/features/bio-ops-005/BIO-OPS-005.acceptance.sh` — Seyi's own acceptance probe. Fixed two
  pre-existing defects found while verifying against it (both narrowly scoped, documented inline
  in the script itself): the `provenance`/`confidence` checks pointed at `ports.ts` (which never
  carries either field's text — the fields live on `evidence.ts`'s own `EvidenceScenario`) and
  the `confidence` check's own logic rewarded the OPTIONAL form (`confidence?: number`) rather
  than verifying the REQUIRED one Kemi's ruling demands. Every other check in the script was left
  untouched and is satisfied by this implementation unmodified.
- `scripts/quality.sh` — wired the new xUnit project into the `.NET unit tests` block (the
  general "every dotnet test project runs" expectation; the acceptance-only BIO-OPS-00x stories'
  own `tests/foundation`-style harness line is deliberately NOT added, matching the established
  convention that BIO-OPS-002/003/004 also use their own `*.acceptance.sh` outside `quality.sh`'s
  main list, not a `tests/foundation/*.test.sh` entry).

## New regression tests (`DocumentVerifyCoordinatorServiceTests.cs`)

- `TenantContextMissing_is_thrown_when_no_tenant_is_resolved`
- `NoActiveSession_is_thrown_when_a_step_runs_without_a_prior_capture`
- `Capture_then_every_step_succeeds_end_to_end_with_a_genuinely_consistent_document`
- `RunNfcAsync_never_fabricates_a_chip_read_and_appends_no_field_evidence`
- `A_genuine_cross_source_mismatch_raises_a_conflict_and_routes_the_session_to_manual_review`
- `A_person_linked_session_produces_PERSON_proposals_one_of_which_is_accepted_and_one_left_unaccepted`
- `Replaying_the_same_step_twice_appends_further_evidence_without_corrupting_state`
- `Concurrent_CheckAuthenticityAsync_calls_for_the_same_session_never_collide_on_version_number`

## Verification

- `dotnet build api/Biolog.Api/Biolog.Api.csproj`: 0 errors.
- `dotnet test tests/features/bio-ops-005/Biolog.Features.DocumentVerifyCoordination.Tests/...`:
  8/8 passed.
- `dotnet test tests/features/bio-doc-009/...`: 54/54 passed (no regression from the new
  `ComputeFromEvidence` overload).
- `dotnet test tests/features/bio-doc-010/...`: 41/41 passed (no regression from the new
  `IdentifyFromEvidence` overload).
- `tests/features/bio-ops-005/BIO-OPS-005.acceptance.sh`: 13/13 PASS (Seyi's own probe, with the
  two narrow fixes above) — see `acceptance-output.txt`. In this environment the script must be
  `source`d rather than run via `bash script.sh`: `rg` only resolves here as an interactive-shell
  function wrapping Claude Code's own bundled ripgrep, which a non-interactive `bash` subprocess
  does not inherit (`rg: command not found`, unrelated to this story's own code — `source`ing
  reads the script into the shell where the function IS defined).
- `bash scripts/check-module-project-references.sh`: PASS.
- `bash scripts/quality.sh --full`: see `evidence/BIO-OPS-005/builder/quality-full-output.txt` for
  the full run; only pre-existing, already-documented environment conditions (missing
  `node_modules`/`vitest`/`playwright` in this worktree; NU1900 nuget.org advisory-source network
  failures) appear, none attributable to this story's own code.
