# BIO-IDN-006 independent rereview — round 6

**Verdict: FAIL**

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | PASS | Operator-key gate is applied to all mutation routes and fails closed when absent or wrong. |
| Security | FAIL | `X-Actor-Id` remains caller-controlled; the shared operator key is not an authenticated principal binding. |
| Tenant isolation | PASS | Existing tenant-scoped role, permission, and approval checks remain intact. |
| Offline | APPLICABLE/PASS | No offline/sync surface exists. |
| Concurrency | PASS for current tier | Existing locks and compare-and-set checks remain unchanged. |
| Regression | FAIL | A caller possessing the operator secret can impersonate a privileged PERSON and pass every RBAC gate. |

## Finding

### BIO-IDN-006-QA-08 — operator key is only a coarse shared gate, not identity authentication (Critical)

The new `OperatorKeyGate` checks one server-configured shared secret before mutation endpoints, then `ActorResolver` still copies the caller-selected `X-Actor-Id` directly into the authorization subject. The implementation documentation explicitly acknowledges that a caller with the operator secret may assert **any** PERSON id, including a privileged administrator, maker, or checker.

Therefore the security boundary is still bypassable by a caller who obtains or shares the operator key:

- impersonate a known or guessed `SUPER_ADMIN` and perform privileged role mutations;
- assert two different privileged IDs across maker/checker requests to satisfy separation of duties; or
- impersonate a delegated permission holder for role, grant, rule, maker, or checker operations.

The gate reduces anonymous reachability but does not satisfy the required authenticated-principal binding. The specialist security review correctly identifies this as a live Critical finding; the implementation calls it an interim mitigation and defers the real closure to BIO-INT-001. The Definition of Done disallows unresolved Critical/High issues, so this story cannot pass local acceptance while the mutation endpoints trust a caller-supplied identity.

Required closure: authenticate the principal (or trusted service identity) before RBAC resolution and bind the effective actor to that verified identity. A caller-provided header may only be accepted when injected by a trusted authenticated edge and cryptographically/provenance checked; a shared operator secret alone is insufficient.

## Verification

- Implementation head: `6414fb8`; builder evidence head: `19148cf`.
- `dotnet build tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj --no-restore --nologo` — PASS, 0 errors (warnings only).
- `bash tests/features/bio-idn-006/BIO-IDN-006.test.sh` — PASS (113/113), including missing/wrong operator-key tests, but no privileged-ID impersonation negative.
- Traceability, module-reference, and secret scans — PASS.
- The checked-in specialist report `evidence/BIO-IDN-006/codex/SECURITY-REVIEW-ZAINAB.md` independently records the same Critical residual.

This verdict remains for the current in-memory/same-process tier and does not reject the operator-key mitigation as a defense-in-depth measure; it rejects treating that mitigation as authentication closure.
