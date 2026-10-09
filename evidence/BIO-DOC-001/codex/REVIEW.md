# BIO-DOC-001 independent review

**Verdict: FAIL**

Reviewed implementation `3054aec` at builder-evidence head `7775f89` against BIO-DOC-001-AC01 through AC05.

## Findings

### QA-01 — High — conflict evidence does not prove a conflict (AC05)

`InMemoryDocumentSessionStore.RaiseConflict` checks only that the cited sequence numbers exist. It does not verify that each cited `DocumentFieldEvidence.FieldName` equals the conflict's `fieldName`, or that their normalized values disagree. A caller can therefore open a `date_of_birth` conflict using equal values belonging to `given_names` and `document_number`. This moves the session into manual review and gates person-field proposals using false provenance.

Required closure: require every cited item to belong to the named field and require at least two distinct normalized values; add negative tests for unrelated fields and equal values.

### QA-02 — Medium — bounding boxes accept non-finite coordinates (AC03)

`BoundingBox.Create` uses range comparisons that all evaluate false for `double.NaN`. It therefore accepts `NaN` for x/y/width/height, producing invalid location metadata that cannot define a source-image region. Explicitly reject non-finite values before range and sum checks.

## Reproduction

```sh
bash evidence/BIO-DOC-001/codex/independent-review.sh
```

The probe constructs both invalid records through public production APIs and exits non-zero.

## Category verdicts

- Functional: FAIL — AC05 accepts fabricated conflict relationships.
- Security: APPLICABLE/PASS — no secret material or person mutation path was found.
- Tenant isolation: PASS — store resolution checks tenant ownership and fails cross-tenant access as not-found.
- Offline/failure recovery: NOT APPLICABLE — no transport or durable persistence exists in this foundation.
- Concurrency: PASS — same-session operations serialize and version checks reject stale mutations.
- Regression: FAIL — supplied tests omit adversarial relationship and non-finite-number cases.

## Baseline

- Builder structural suite: 43/43 passed.
- Builder compiled xUnit executable: 56/56 passed.
- Unrelated pre-existing untracked `tests/flutter/pubspec.lock` was preserved and not included.
