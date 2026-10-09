# BIO-BIO-006 independent review — round 1

## Verdict: FAIL

### Finding QA-01 — review queue is incorrectly limited by `maxCandidates`

`SearchAsync` scores the entire tenant gallery, but calls `RaiseOrAugmentReviewAsync` only on `ranked`, after `.Take(maxCandidates)`. A possible duplicate below the caller's result cap is therefore never routed to the human review queue. AC03 and the feature documentation say every candidate meeting the tenant threshold is raised; `maxCandidates` is an output/display cap, not a review authorization filter.

Reproduction: index two other people scoring `0.99` and `0.95`, set policy threshold `0.80`, and call `SearchAsync(..., maxCandidates: 1)`. The result contains only the `0.99` candidate and only that pair is queued; the `0.95` above-threshold pair is silently absent from review. Calling with a larger cap changes whether the same biometric observation is escalated.

This can miss duplicate candidates and makes review coverage depend on an arbitrary caller pagination/count parameter.

Category verdicts: Functional FAIL; Security PASS; Tenant isolation PASS; Offline NOT APPLICABLE; Concurrency PASS; Regression FAIL (AC03).

Feature structural harness: 39 passed, 0 failed; it does not test above-threshold candidates outside `maxCandidates`.
