# BIO-IDN-006 independent review

**Verdict: FAIL**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | FAIL | The maker-checker state machine exists, but no sensitive operation calls its clearance check. |
| Security | FAIL | An arbitrary asserted actor can assign `SUPER_ADMIN`; the mandatory privilege-escalation negative case is not enforced. |
| Tenant isolation | PASS | Assignment, permission, rule, and approval storage keys include the resolved tenant; the supplied cross-tenant structural and host-channel coverage passed. |
| Offline | APPLICABLE/PASS | The feature is process-local and has no offline client or sync surface; deterministic local checks compile without network access. |
| Concurrency | PASS | Per-natural-key locks and compare-and-swap repository updates cover concurrent role, grant, rule, and approval mutations. |
| Regression | FAIL | Required security behavior fails even though the builder's 66-check structural harness passes. |

## Findings

### BIO-IDN-006-QA-01 — arbitrary actor can grant `SUPER_ADMIN` (Critical)

The role-assignment endpoint treats possession of an arbitrary `X-Actor-Id` string as sufficient authority. `RoleAssignmentEndpoints.AssignAsync` resolves that untrusted string and immediately calls `RoleAssignmentService.AssignAsync`; neither layer invokes `PermissionService.HasPermissionAsync` or applies any authorization policy. `SUPER_ADMIN` then implicitly holds every permission.

Reproduction is checked in as `ADVERSARIAL_unprivileged_asserted_actor_cannot_grant_SUPER_ADMIN_to_a_person`. It sends:

```http
POST /api/v1/persons/{personId}/roles/SUPER_ADMIN
X-Actor-Id: unprivileged_attacker
```

Expected: `403 Forbidden`. The implementation path returns `200 OK` and creates the assignment. The existing positive endpoint test already confirms the same unrestricted path returns 200 for an arbitrary asserted actor; inspection shows no role-specific branch before persistence.

This violates the story's mandatory privilege-escalation negative requirement. Deferring authentication does not justify deferring authorization: the story supplies a permission verification service and the actor seam, but never connects them.

### BIO-IDN-006-QA-02 — maker-checker control is inert (High)

`MakerCheckerService.IsActionClearedAsync` is never called by a production action or endpoint. Repository-wide call-site evidence shows only the method declaration and builder tests. Enabling `requires_second_approval` therefore does not block, defer, or otherwise govern any sensitive action. A tenant can configure the rule and complete an approval workflow, but the protected action is unchanged.

This does not satisfy AC03's operational statement that sensitive actions can require second approval. At minimum, an owned sensitive action must consume the hook, or the implementation must expose an enforceable integration boundary used by the shipped host.

## Commands

- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, including the adversarial regression.
- Filtered `dotnet test` — environment blocked before execution because VSTest could not bind its loopback socket (`SocketException: Permission denied`); this is recorded and is not counted as product behavior.
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (66/66), but it does not test caller authorization or a production consumer of maker-checker clearance.
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using the built-in fallback.

Raw outputs and call-site captures are stored beside this report.
