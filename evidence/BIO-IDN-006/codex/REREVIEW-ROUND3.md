# BIO-IDN-006 independent rereview — round 3

**Verdict: FAIL**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | FAIL | A failed sensitive mutation can consume its valid approval, so the caller cannot retry the same approved action. |
| Security | PASS | Round-three authorization gates cover role assignment, permission grants, and sensitive-action rule changes in the inspected implementation. |
| Tenant isolation | PASS | Authorization and approval lookups remain resolved-tenant scoped. |
| Offline | APPLICABLE/PASS | No offline/sync surface exists. |
| Concurrency | FAIL | Approval consumption is atomic by itself, but not atomic with target validation, audit, and the role mutation. |
| Regression | FAIL | The new approval-burn path violates safe retry and audit/mutation atomicity despite the structural harness passing. |

## Closed findings

- Round-1 QA-01: arbitrary `SUPER_ADMIN` assignment — closed.
- Round-1 QA-02: maker-checker had no production consumer — closed.
- Round-2 QA-03: non-`SUPER_ADMIN` role/grant escalation — closed.
- Round-2 QA-04: arbitrary maker-checker rule disable — closed.
- Round-2 QA-05: indefinitely reusable approval — closed for successful repeated mutations.

## Finding

### BIO-IDN-006-QA-06 — approval is consumed before the sensitive mutation can succeed (High)

`RoleAssignmentService.AssignAsync` calls `RequireSuperAdminMutationAuthorityAsync` first. That helper calls `MakerCheckerService.TryConsumeApprovalAsync`, which persists `ConsumedAt`, before `AssignAsync` validates the target identifier, proves target PERSON existence, acquires the role lock, writes the audit event, or commits the assignment.

If the target is unknown/foreign, the operation returns `person_not_found` after the approval has already been consumed. If audit or repository commit fails after consumption, the same approved request is also burned. A caller cannot retry the approved operation, and a checker-approved action can be permanently lost without a corresponding mutation.

The independent source probe `round3-order-probe.sh` reproduces the ordering: the authority/consume call precedes the target existence check (lines 119 and 131 at the frozen head). The worktree's adversarial test `ADVERSARIAL_failed_sensitive_mutation_does_not_consume_its_approval` captures the required invariant: a failed mutation must leave `ConsumedAt` null. It is currently an uncommitted builder-side test and was not modified by this review.

Required closure: validate all deterministic target and request preconditions before consuming; then atomically reserve/consume approval with the sensitive mutation and its audit/outbox work, or retain an explicit pending/retry state so any post-consumption failure cannot strand the approved action.

## Commands

- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, 0 errors (warnings only).
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (100/100), but it checks the presence and single-use shape, not consumption ordering against failed target/mutation paths.
- `python3 scripts/check-traceability.py --all` — PASS.
- `bash scripts/check-module-project-references.sh` — PASS.
- `bash scripts/secret-scan.sh --all` — PASS using the built-in fallback.

Raw outputs and the ordering probe are stored beside this report.
