# BIO-FND-005 Independent Re-review — Round 2

Date: 2026-09-20  
Reviewed commit: `24a72e4`  
Verdict: **FAIL**

## Result

The originally reported value-based bypasses are closed: contradictory typed eligibility, non-boolean self-accept, `git_push: true`, and a configured remote are rejected. However, the new strict validator still permits omissions and substitutions that violate AC01, AC03, AC04, and AC05.

## Findings

### BIO-FND-005-QA-04 — missing boolean permissions silently default to allowed validation

Severity: High  
File: `scripts/validate-governance.py`

The validator uses:

```python
permissions.get("self_accept", False)
permissions.get("git_push", False)
```

Therefore deleting either key passes validation even though the validator contract says both permissions must be explicit boolean false for every agent. The live configuration already omits `self_accept` from Chinedu and still reports `GOVERNANCE CHECK: PASS`.

Independent mutations confirmed both of these false successes:

- delete Emeka's `self_accept` key;
- delete Emeka's `git_push` key.

### BIO-FND-005-QA-05 — required named roster is not enforced

Severity: High  
File: `scripts/validate-governance.py`

AC01 requires the specific twelve Nigerian-named agents. The validator checks only count and uniqueness. Replacing `Chinedu` with `Alice` retains 12 unique non-empty agents and passes the full developer suite.

### BIO-FND-005-QA-06 — Kemi's migration-sequencing authority is not enforced

Severity: High  
File: `scripts/validate-governance.py`

AC03 covers both shared-contract approval and migration sequencing. The validator checks Kemi as shared-contract approver and a prose `sole_approver` field, but never validates migration scope. Removing `"migrations"` from Kemi's `write_scope` passes the full developer suite.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 exact 12 named agents and assignments | FAIL |
| AC02 typed dependency/conflict eligibility | PASS |
| AC03 Kemi-only contracts and migration sequencing | FAIL |
| AC04 no builder self-accept | FAIL |
| AC05 no remote or push before W15 | FAIL |

## Passing probes

- Thirteenth agent rejected.
- Second shared-contract approver rejected.
- Contradictory eligibility values rejected.
- Non-boolean self-accept grant rejected when present.
- `git_push: true` rejected when present.
- Configured Git remote rejected.
- Live pre-push guard remains active, executable, and blocking.
- Builder suite passes 8/8 on its covered cases.

## Category verdicts

- Functional: FAIL.
- Security/RBAC: FAIL.
- Tenant isolation: NOT APPLICABLE.
- Offline/recovery: PASS.
- Concurrency: PASS for typed conflict-lock rule.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-FND-005/codex/independent-review.sh
```

Expected review exit status: `1`. No implementation files were changed.
