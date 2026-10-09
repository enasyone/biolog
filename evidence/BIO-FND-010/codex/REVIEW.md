# BIO-FND-010 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `65896f5` at builder-evidence head `c09a96f`.

The supplied harness passes 10/10 and the full local quality command passes. Independent probes found two acceptance-boundary failures.

## Findings

### BIO-FND-010-QA-01 — High — Invalid evidence can authorize acceptance

The checker treats every non-empty evidence file as valid coverage. It does not inspect the BIO-FND-004 evidence metadata, bind evidence to the story or reviewed commit, require `result: PASS`, or require independent evidence when independent verification applies.

The independent reproduction supplies an evidence manifest for another story, commit `0000000000000000000000000000000000000000`, command `false`, result `FAIL`, and reviewer `Builder`. The tool still exits zero and creates a `PASS` acceptance record. This violates AC03/AC04 and the controlling traceability rule that stale, different-commit, failed, or builder-only evidence must block acceptance.

Required closure: validate declared evidence using the accepted BIO-FND-004 structure, require story and accepted/reviewed commit binding, require successful results, and enforce required reviewer classes before acceptance can be recorded.

### BIO-FND-010-QA-02 — High — No repository-wide implemented-story inventory

The user story requires automated checks for every implemented story, and AC01 refers to story spec files. The repository currently has 13 implemented foundation test harnesses but only one `governance/stories/*.json` file. The checker accepts one caller-selected spec and has no discovery/inventory mode, so omitted implemented stories are invisible rather than reported as missing traceability.

Required closure: derive the implemented-story inventory from an authoritative repository source, require a story spec for each in-scope implemented story, and make the normal quality/acceptance gate validate the complete inventory.

## Reproduction

```sh
python3 evidence/BIO-FND-010/codex/independent-review.py
bash tests/foundation/BIO-FND-010.test.sh
bash scripts/quality.sh
```

Independent result: missing evidence blocks as expected; invalid evidence authorization and repository-wide inventory fail. Builder harness: 10/10 pass. Full quality: PASS.

## Category verdicts

- Functional: FAIL — QA-01 and QA-02.
- Security: PASS — path escape and exclusive record creation checks pass.
- Tenant isolation: not applicable.
- Offline/recovery: PASS — checks run locally and missing files fail closed.
- Concurrency: not applicable; exclusive creation prevents overwrite.
- Regression: PASS — full local quality suite passes.
