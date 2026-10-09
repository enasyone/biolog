# BIO-IDN-006 independent rereview — round 7

**Verdict: PASS (ADR-0006 interim provenance scope)**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | PASS | Role catalogue, tenant-scoped permissions, maker-checker, audit history, and single-use approvals remain intact. |
| Security | PASS (scoped) | ADR-0006 Decision 3 provenance proof is bound to the exact actor ID and checked before authorization; its shared-secret residual is explicitly accepted until BIO-INT-001. |
| Tenant isolation | PASS | Existing tenant-scoped lookups and host-channel checks pass. |
| Offline | APPLICABLE/PASS | No offline/sync surface exists. |
| Concurrency | PASS for current tier | Existing locks and compare-and-set checks pass at the same-process tier. |
| Regression | PASS | QA-01 through QA-08 are addressed or explicitly accepted under the authoritative ADR. |

## QA-08 verification

The prior bare operator-key gate was removed. `ActorProvenanceGate` now verifies an HMAC-SHA256 proof over the **same** `X-Actor-Id` value that `ActorResolver` resolved, and all four RBAC mutation surfaces invoke it before authorization. Missing, malformed, or wrong proofs fail closed. This matches ADR-0006 Decision 3's required application-side closure.

ADR-0006 explicitly records that this is not authentication and cannot restore genuine maker/checker separation while the shared provenance secret is the trust root. That residual is accepted and tracked to BIO-INT-001; it is not silently presented as closed.

## Verification

- Implementation head: `67bcb8e`; builder evidence head: `1a172cd`.
- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, 0 errors (warnings only).
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (121/121).
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using the built-in fallback.
- xUnit remains sandbox-blocked by VSTest loopback socket denial; no live xUnit claim is made.

Reopen the identity/provenance residual when BIO-INT-001 replaces the asserted-actor accommodation, and reopen audit/approval atomicity before independent durability or multi-process sharing.
