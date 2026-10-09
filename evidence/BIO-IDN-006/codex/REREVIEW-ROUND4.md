# BIO-IDN-006 independent rereview — round 4

**Verdict: FAIL**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | PASS | Round-three approval-burn ordering is corrected for validation/no-op/audit failure paths. |
| Security | FAIL | Maker and checker endpoints accept arbitrary asserted actors; only same-actor self-approval is rejected. |
| Tenant isolation | PASS | Approval history remains tenant-keyed. |
| Offline | APPLICABLE/PASS | No offline/sync surface exists. |
| Concurrency | PASS | Single-use consumption remains protected by the per-request lock and compare-and-set repository update. |
| Regression | FAIL | An unprivileged actor can approve a sensitive request, defeating the maker-checker security criterion. |

## Closed finding

- Round-3 QA-06 approval consumption before target validation: **CLOSED**. The current code performs a read-only clearance check early and defers consumption until after validation and durable audit, immediately before repository commit.

## Finding

### BIO-IDN-006-QA-07 — maker-checker request and decision endpoints lack authorization (Critical)

`MakerCheckerService.RequestApprovalAsync` and `DecideAsync` accept the asserted `AuditActor` and only enforce that the checker is not the maker. Neither method calls `PermissionService.ActorHasPermissionAsync` (or any equivalent authority gate). The endpoint methods simply resolve `X-Actor-Id` and pass it through.

Consequences:

1. Any arbitrary actor can submit approval requests for sensitive actions.
2. Any different arbitrary actor can approve or reject another actor's request.
3. A legitimate authorized mutator can then consume an approval produced by actors who hold no checker authority. Changing the two header strings is enough to satisfy the current separation-of-duties check.

This is not fixed by the self-approval guard: separation of duties requires two authorized principals, not merely two distinct strings. It violates the story's mandatory privilege-escalation security criterion and the purpose of maker-checker controls.

Required closure: define and enforce tenant-scoped maker and checker permissions (or an equivalent role/policy) in both request and decision paths, before persistence or audit. The checker permission must be independently verified against the resolved tenant and the action/target scope; arbitrary `X-Actor-Id` values must not qualify.

## Commands

- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, 0 errors (warnings only).
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (100/100), but it verifies only self-approval and does not require checker authorization.
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using the built-in fallback.

Raw outputs and source call-sites are retained in the codex evidence directory.
