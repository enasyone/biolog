# BIO-IDN-006 independent rereview — round 2

**Verdict: FAIL**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | FAIL | Maker-checker now has a consumer, but approvals are reusable indefinitely rather than bound to one mutation. |
| Security | FAIL | The exact `SUPER_ADMIN` path is closed; equivalent privilege escalation remains through every other role/grant endpoint, and any actor can disable maker-checker rules. |
| Tenant isolation | PASS | All affected data remains keyed by the resolved tenant. |
| Offline | APPLICABLE/PASS | No offline/sync surface exists; build and structural checks are local. |
| Concurrency | FAIL | Clearance is checked separately from mutation and never consumed atomically; an approved record permanently clears later mutations for the same action/target. |
| Regression | FAIL | The original reproduction passes, but broader mandatory privilege-escalation and maker-checker bypass cases fail. |

## Remediation status

- Round-1 QA-01 exact path: **CLOSED**. Assigning or revoking `SUPER_ADMIN` requires an existing active `SUPER_ADMIN` actor.
- Round-1 QA-02 exact inert-hook issue: **CLOSED**. `SUPER_ADMIN` assign/revoke now calls `IsActionClearedAsync`.

## Findings

### BIO-IDN-006-QA-03 — authority-changing endpoints remain open to arbitrary actors (Critical)

The fix protects only the `SUPER_ADMIN` enum value. Any caller supplying an arbitrary `X-Actor-Id` can still:

1. assign `TENANT_ADMIN` or any other operational role to a person; and
2. grant that role any permission key, including a high-impact key such as `tenant.delete_all_data`.

`RoleAssignmentService` branches to authorization only when `role == SuperAdmin`. `PermissionService.SetGrantAsync` rejects configuration for `SUPER_ADMIN`, but performs no authorization for every configurable role. The permission model then reports the attacker-selected permission as granted.

The checked-in adversarial HTTP regression requires both operations to return `403 Forbidden`; both implementation paths return `200 OK`. This is the same privilege-escalation class as round 1, not a safe lower privilege: permission keys are open-ended, so a non-`SUPER_ADMIN` role can be configured with any concrete authority.

Required closure: authorize all role assignment/revocation and permission-grant mutations, not only the special enum value. The actor's tenant-scoped role/permission must be checked before target lookup, audit, or commit.

### BIO-IDN-006-QA-04 — any asserted actor can disable maker-checker (Critical)

`PUT /api/v1/sensitive-actions/{actionKey}/rule` still requires only an arbitrary `X-Actor-Id`. `MakerCheckerEndpoints.SetRuleAsync` passes that string to `MakerCheckerService.SetActionRuleAsync`, which performs validation, audit, and persistence without any authorization check.

An unprivileged caller can therefore set `requires_second_approval=false` for `rbac.super_admin_assign` or `rbac.super_admin_revoke`, defeating the newly added control before a legitimate administrator performs the mutation. The checked-in adversarial regression requires `403 Forbidden`; the implementation path returns `200 OK`.

Required closure: rule configuration and approval decisions need explicit tenant-scoped authorization. Separation of maker/checker identities alone is insufficient when arbitrary callers can administer the control.

### BIO-IDN-006-QA-05 — one approval permanently clears repeated sensitive mutations (High)

`IsActionClearedAsync` returns true whenever the latest request for `(tenant, action, target)` is `APPROVED`. The sensitive mutation neither marks that approval consumed nor binds it to a mutation identifier/version. After one approved `rbac.super_admin_assign`, later revoke/reassign cycles for the same target reuse the old approval indefinitely.

Required closure: reserve and consume one approval atomically with one sensitive mutation, with replay/idempotency semantics that cannot authorize a distinct later action.

## Commands

- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, including the new adversarial regressions.
- Filtered `dotnet test` — environment blocked before execution because VSTest could not bind its loopback socket; not counted as product behavior.
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (82/82), but it checks only the special `SUPER_ADMIN` branch and presence of a clearance call.
- Traceability, module-reference, and secret scans — PASS.

Raw outputs and call-site captures are stored beside this report.
