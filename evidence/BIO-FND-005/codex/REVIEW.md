# BIO-FND-005 Independent Review — Seyi

Date: 2026-09-20  
Reviewed commit: `bb4f4ba`  
Verdict: **FAIL**

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 | PASS | Exactly the required 12 unique named agents exist, each with non-empty role, engine, model, and permissions. The supplied check rejects a 13th agent. |
| AC02 | FAIL | Dispatch eligibility is an unstructured prose value and the check only searches for words; a rule explicitly allowing incomplete dependencies and ignoring conflict locks still passes. |
| AC03 | PASS | Only Kemi has `approve_shared_contracts: true` and only Kemi's write scope includes migrations. A second approver is rejected. |
| AC04 | FAIL | The check rejects only the boolean literal `true`; a non-boolean permission value granting self-acceptance passes. There is no permission schema or operational enforcement. |
| AC05 | FAIL | The live pre-push guard blocks correctly and the current roster has `git_push: false`, but the story check accepts `git_push: true` and an already configured Git remote. Remote creation is not prevented or detected. |

## Findings

### BIO-FND-005-QA-01 — dispatch eligibility is not machine-enforced

Severity: High  
Files: `governance/munder-agents.json`, `tests/foundation/BIO-FND-005.test.sh`

Replacing `rules.dispatch_eligibility` with the contradictory value below still produces 5/5 passing tests:

```text
Chinedu may dispatch whether dependencies are DONE or not; conflict locks are ignored.
```

The test merely finds `dependencies are DONE` in JSON and `eligib` in unchanged documentation. It does not validate dependency completion or the conflict lock required by the assignment.

### BIO-FND-005-QA-02 — malformed self-accept permission bypasses the check

Severity: High  
File: `tests/foundation/BIO-FND-005.test.sh`

Setting Emeka's `self_accept` permission to the string `"allowed"` passes because the test rejects only values that are exactly the JSON boolean `true`. No schema requires this permission to be boolean or explicitly false.

### BIO-FND-005-QA-03 — publication restrictions are not verified

Severity: High  
File: `tests/foundation/BIO-FND-005.test.sh`

Both of these states still produce 5/5 passing tests:

- Emeka has `git_push: true`.
- The repository already has a configured Git remote.

The AC05 check only searches for `github_push_enabled` in the rules and tests that a hook file exists. It does not validate every agent's permission, absence of remotes, hook activation/executability, or blocking behavior. The real guard currently is active and blocks, but the required regression control permits prohibited states.

## Passing probes

- Exact roster and required agent attributes.
- Thirteenth agent rejected.
- Second shared-contract approver rejected.
- Kemi alone owns migration scope.
- Current agents all have `git_push: false`.
- `core.hooksPath=.githooks`; hook is executable and blocks under the false W15 flag.
- Builder suite passes 5/5 on current data but permits the bypasses above.

## Category verdicts

- Functional: FAIL.
- Security/RBAC: FAIL.
- Tenant isolation: NOT APPLICABLE.
- Offline/recovery: PASS; controls are local.
- Concurrency: FAIL; conflict-lock eligibility is not machine-verified.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-FND-005/codex/independent-review.sh
```

Expected review exit status: `1` until the findings are fixed. No implementation files were changed.
