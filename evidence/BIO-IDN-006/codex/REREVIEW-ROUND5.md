# BIO-IDN-006 independent rereview — round 5

**Verdict: PASS (current in-memory/local tier)**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | PASS | Baseline catalogue, tenant-scoped grants, maker-checker workflow, audit history, and approval consumption are implemented and mapped to tests. |
| Security | PASS | Role/grant/rule mutations and maker/checker participation now require tenant-scoped permissions; self-approval remains separately forbidden. |
| Tenant isolation | PASS | Permission, role, rule, and approval repositories are keyed by resolved tenant; host-channel cross-tenant checks pass. |
| Offline | APPLICABLE/PASS | No offline/sync surface exists for this story. |
| Concurrency | PASS | Per-key locks, compare-and-set persistence, and single-use approval consumption cover the reviewed same-process races. |
| Regression | PASS | Prior QA-01 through QA-07 findings are closed at the current implementation head. |

## Closed finding

### BIO-IDN-006-QA-07 — unauthorized maker/checker actors

`RequestApprovalAsync` now requires `rbac.approval_request`, and `DecideAsync` requires `rbac.approval_decide`, both resolved through `PermissionService.ActorHasPermissionAsync` before validation or persistence. The existing self-approval check remains an additional separation-of-duties rule. This closes the round-four bypass where two arbitrary distinct actor strings could create and approve a sensitive request.

## Verification

- Implementation head: `1814616`; builder evidence head: `4c39477`.
- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, 0 errors (warnings only).
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (113/113).
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using the built-in fallback.
- The filtered xUnit runner remains unavailable in this sandbox because VSTest cannot bind its loopback socket (`SocketException: Permission denied`); compiled and structural evidence is recorded without claiming live xUnit execution.

The current verdict is for the existing in-memory, same-process tier. Reopen the audit/approval atomicity review before approval state, role assignments, or audit events move to independently durable or multi-process stores.
