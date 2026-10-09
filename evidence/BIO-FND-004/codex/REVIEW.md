# BIO-FND-004 Independent Review — Seyi

Date: 2026-09-20  
Reviewed implementation commit: `2f68525`  
Reviewed branch head: `7abe80a`  
Verdict: **FAIL**

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 | PASS | The canonical template contains builder, adversarial, security, and acceptance stage directories. |
| AC02 | FAIL | Required fields and basic formats are enforced, but a wrong locally existing commit is accepted without proving it is the commit under review. |
| AC03 | FAIL | Missing story/stage/metadata and invalid JSON are reported, but failed stage verdicts still produce a clean traceability result. |
| AC04 | PASS | `SYNTHETIC` and `NO_TEST_DATA` are the only accepted classifications; `PRODUCTION` was rejected. |

## Findings

### BIO-FND-004-QA-01 — wrong locally existing commit passes traceability

Severity: High  
File: `scripts/check-evidence.py`

The checker verifies only that `commit_hash` resolves to any local commit. It has no expected-commit input and does not compare the metadata hash with the commit being accepted. Replacing one stage's correct hash with `HEAD~1` still yields `EVIDENCE CHECK: PASS`.

This permits evidence from an older or unrelated locally reachable commit to support acceptance of newer code, defeating the commit-level traceability required by AC02.

### BIO-FND-004-QA-02 — `result: FAIL` is treated as complete passing evidence

Severity: High  
File: `scripts/check-evidence.py`

The checker validates that `result` is either `PASS` or `FAIL`, but does not treat `FAIL` as an acceptance gap. A synthetic story with all four canonical stages present and all four metadata files set to `"result": "FAIL"` returns exit `0` and prints `EVIDENCE CHECK: PASS`.

The documentation states local acceptance requires all four stages and a clean checker exit. Therefore this false success can approve evidence that explicitly records failed verification.

## Passing probes

- Complete valid four-stage evidence passed.
- Builder-only evidence failed under the default all-stage check and passed only with explicit `--required-stages builder` scope.
- Missing stages, malformed JSON, partial metadata, and nonexistent commit hashes failed.
- Production-data classification failed.
- Current BIO-FND-004 all-stage check correctly reports the three not-yet-due stage directories missing.
- Builder developer suite passed 7/7 but does not cover the two false-success paths.

## Category verdicts

- Functional: FAIL.
- Security: NOT APPLICABLE.
- Tenant isolation: NOT APPLICABLE.
- Offline/recovery: PASS; validation is local and malformed inputs fail closed.
- Concurrency: NOT APPLICABLE.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-FND-004/codex/independent-review.sh
```

Expected review exit status: `1` until both findings are fixed. No implementation files were changed.
