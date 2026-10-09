# BIO-IDN-006 round-7 specialist security re-review — Zainab

**Reviewed head:** `67bcb8e` implementation, builder evidence `1a172cd`/`4c39477`  
**Verdict:** PASS for ADR-0006 Decision 3's documented interim application-side provenance tier

## Findings and closure

The prior Critical finding ZA-IDN006-01 was that caller-controlled `X-Actor-Id` directly fed role, permission, and maker-checker authorization. Round 7 adds `ActorProvenanceGate` and `HmacActorProvenanceStore`.

- The gate verifies HMAC-SHA256 over the exact actor ID already resolved by `ActorResolver`; a proof for actor A cannot authorize an assertion for actor B.
- The gate runs immediately after actor resolution and before authorization on role assign/revoke, permission-grant configuration, sensitive-action configuration, and approval request/decision.
- Configuration is out-of-band (`Rbac:ActorProvenanceSecret`), not writable over HTTP; absent configuration fails closed; comparison is constant-time.
- Wrong-actor proof, missing proof, and unconfigured-store paths are covered by the new tests and fail before privilege lookups.
- The remaining shared-secret holder, replay, and true maker/checker identity limitations are explicitly disclosed in ADR-0006 Decision 3 and the implementation comments. They are the trusted-edge/BIO-INT-001 boundary, not an unreported defect in this interim application-side closure.

## Verification

- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, 0 errors (warnings only).
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS 121/121.
- In-process xUnit executable — PASS 113/113.
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using fallback scanner.

No implementation code was changed during this review.
