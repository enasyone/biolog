# BIO-FND-010 independent re-review — round 3

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation `3ae102a` at evidence head `f3f0030`.

QA-01 and QA-02 remain closed. QA-03 is closed: the checker now enforces a non-removable builder plus adversarial minimum independently of the mutable story spec.

## Finding

### BIO-FND-010-QA-04 — High — One evidence artifact can impersonate multiple required stages

Stage is supplied only by each story-spec declaration, not bound inside the evidence artifact. The same metadata path can therefore be declared twice, once as `builder` and once as `adversarial`. With one reviewer and one test command, the checker marks both stages valid and creates a PASS acceptance record.

This does not prove the builder and independent Codex checks both occurred; it only relabels one artifact twice. AC04 and the Definition of Done require independent review, not duplicate references to one result.

Required closure: require distinct evidence artifacts and appropriate distinct reviewer identities for independently required stages, bind stage to the evidence metadata or protected evidence location, and reject duplicate resolved evidence paths across required stages.

## Reproduction

```sh
python3 evidence/BIO-FND-010/codex/independent-review.py
bash tests/foundation/BIO-FND-010.test.sh
python3 scripts/check-traceability.py --all
```

Independent probes: QA-01/02/03 pass; duplicate-stage alias probe fails. Supplied harness: 14/14 pass. Inventory: 13/13 stories pass.

## Category verdicts

- Functional: FAIL — AC04 remains bypassable through QA-04.
- Security: FAIL — untrusted declarations can relabel one artifact as multiple controls.
- Tenant isolation: not applicable.
- Offline/recovery: PASS.
- Concurrency: not applicable.
- Regression: PASS — prior findings remain closed.
