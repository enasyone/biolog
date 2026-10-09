# BIO-RPT-004 round-three review

Verdict: PASS

Reviewed builder `fd854cc` and evidence `017867f` against BIO-RPT-004 AC01–AC05.

Both round-two defects are fixed. `ActorProvenanceGate` uses the canonical 32-character minimum, has no fallback key, rejects absent/malformed/mismatched proofs, and runs before the role-based access-control lookup. Failed source reads now return tenant-keyed last-known values with their original observation time, or nullable counts when no successful observation exists; no zero is fabricated.

## AC05 HYBRID boundary

The production LIVE adapter currently sends `X-Actor-Id: web-ui` without a provenance proof, so the real backend correctly returns `401 actor_provenance_required`. The opt-in LIVE Playwright spec proves that fail-closed boundary; the normal SIMULATION Playwright workflow proves the six-panel human interaction and role-filtered drill-downs.

This is accepted under AC05's explicit alternative of an “explicitly documented HYBRID state.” The missing browser provenance transport is the shared `TD-WEB-PROVENANCE-BRIDGE` infrastructure dependency, not an implementation available within BIO-RPT-004's work scope. No positive LIVE workflow is claimed before that bridge exists. BIO-RPT-004 must be re-verified against the real successful browser-to-API workflow when the bridge lands.

## Verification

- Exact reviewed HEAD before review evidence: `017867f`; source commit: `fd854cc`.
- Independent structural harness: PASS, 45/45.
- Focused .NET project build: PASS, 0 warnings / 0 errors.
- Frontend unit regression: PASS, 63 files / 470 tests.
- Functional: PASS, including the explicitly documented AC05 HYBRID state.
- Security: PASS — provenance and permission ordering are fail closed.
- Tenant isolation: PASS — live reads and last-known cache entries remain tenant-keyed.
- Offline/recovery: PASS — stale and never-observed states remain distinct and truthful.
- Concurrency: PASS by inspection for atomic per-tenant cache reads/writes.
- Regression: PASS for structural, build, and frontend unit suites.

## Verdict correction

The first version of this review marked AC05 FAIL because no positive LIVE browser workflow can currently authenticate. After the orchestrator identified the already-active shared `TD-WEB-PROVENANCE-BRIDGE` dependency, I re-applied the accepted criterion's express HYBRID alternative. The documented SIMULATION workflow plus negative real-backend proof satisfies the story now; positive LIVE re-verification remains required when the bridge ships.

