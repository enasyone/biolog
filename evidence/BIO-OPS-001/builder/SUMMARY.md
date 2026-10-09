# BIO-OPS-001 builder evidence

Result: PASS at second remediation `779507f44f8016a1e953836c991b11cbe7ce8b1e`.

The acceptance test creates one new synthetic PERSON in a fresh Vitest process, then exercises the existing local Identity, Photo, Biometric, Document Verify, and Credential services. `EnrolmentJourneyService.executeStage` passes immutable tenant/PERSON/stage context into the callback. The callback persists the produced artifact in the separate owning `LocalArtifactStore`; the service resolves the returned reference and rejects missing, orphaned, cross-tenant, cross-PERSON, or wrong-stage ownership before recording journey evidence.

This second remediation closes Zainab's repeat findings against `125e99f`: F01 is enforced by callback context plus independent artifact-reference resolution, including a cross-PERSON relabelling attack test. F02 is enforced by a stored-blocker check before every callback; tests catch the first error, deliberately attempt card issue anyway, and prove the service blocks it without invoking the callback.

## AC evidence

- BIO-OPS-001-AC01: fresh process plus explicit card-store reset; focused journey PASS 1/1.
- BIO-OPS-001-AC02: all six required stages execute and pass through existing module ports.
- BIO-OPS-001-AC03: independently queried persisted evidence has one tenant/PERSON owner across all downstream artifacts.
- BIO-OPS-001-AC04: structural harness proves `git remote` is empty; all execution is local.
- BIO-OPS-001-AC05: focused journey 4/4 includes explicit failure/offline stop proofs and cross-PERSON rejection; executable harness 3/3, frontend regression 417/417, TypeScript, traceability, module direction, and secret scan pass with zero blocking defects.

## Commands

- `bash tests/operations/BIO-OPS-001.test.sh`: PASS, 3/3, including the real focused Vitest suite rather than grep-only AC assertions.
- `vitest run unit/bio-ops-001-enrolment-journey.test.ts`: PASS, 4/4.
- full frontend `vitest run`: PASS, 56 files and 417/417 tests.
- `tsc --noEmit --project web/tsconfig.json`: PASS.
- `python3 scripts/check-traceability.py --all`: PASS, 32 implemented stories on this isolated branch.
- `bash scripts/check-module-project-references.sh`: PASS.
- `bash scripts/secret-scan.sh --all`: PASS using the built-in fallback scanner.

Dependency directories were borrowed only for package resolution through temporary local symlinks because this older isolated worktree had no installed `node_modules`; the symlinks were removed after execution and no remote or network was used.
