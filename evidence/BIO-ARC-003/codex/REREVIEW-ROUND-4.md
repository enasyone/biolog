# BIO-ARC-003 independent re-review — round 4

- Reviewer: Seyi
- Fix commit: `64d986f`
- Reviewed head: `9abd05b`
- Verdict: **PASS**

## Result

QA-04 is closed. Raw path segments escape `\\` and `.` before joining, so flat dotted
keys and nested paths have distinct encodings. When both shapes coexist and a raw dotted
key resolves to a real nested own-property chain, the projector force-withholds both
locations rather than guessing which the allowlist intended.

The exact reported reproduction now passes. Independent probes also covered non-root and
multi-level collisions, partial matches, prototype-adjacent keys, backslash/dot path
encoding, and injectivity across generated one-, two-, and three-segment paths.

QA-01 through QA-03 remain closed: nested/unregistered values fail closed, projected
values are copied and deeply frozen, and required device/causation fields remain aligned.

## Evidence

`bash evidence/BIO-ARC-003/codex/independent-review.sh` passes, including the added path
encoding probe. Foundation checks pass 34/34 and the contract suite passes 185/185.

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | PASS | Audit and domain-event required fields and shapes align. |
| Security | PASS | Recursive projection, classification, and ambiguous paths fail closed. |
| Tenant isolation | PASS | ARC-002 denial and tenant-pair regressions remain green. |
| Offline | PASS | Delivery dedupe and event-ID conflict behavior remain green. |
| Concurrency | PASS | Projected content is copied and deeply frozen before persistence. |
| Regression | PASS | Foundation 34/34 and contract 185/185 pass. |
