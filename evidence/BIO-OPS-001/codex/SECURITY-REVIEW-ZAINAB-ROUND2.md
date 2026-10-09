# BIO-OPS-001 security re-review — Zainab

## Verdict: FAIL (High)

Commit `49cb5c1` does not close F01 or F02.

### F01 remains — ownership is still decoration, not verified linkage

`EnrolmentJourneyService.executeStage` accepts tenant/PERSON strings alongside a callback, but it does not pass them into the callback or verify the returned artifact. The photo, fingerprint, face, and document operations remain unbound. The test supplies hard-coded `local://...` strings for artifacts that do not exist, then the wrapper stores those strings under the caller-selected PERSON. This is the same trust problem moved into a new class: any result can be labelled as belonging to any tenant/PERSON.

**Required fix:** the stage operation itself must create or return a real stored artifact with immutable tenant/PERSON ownership. The journey must independently resolve that artifact from its owning store and reject mismatched or missing ownership; arbitrary caller-provided references are insufficient.

### F02 remains — blocking is test-side control flow

`executeStage` records a blocker and throws, but it never checks existing blockers before executing a later stage. A caller can catch the error and immediately call `executeStage(..., 'card-issue', ...)`, which will run and persist successfully. The test avoids calling its spy using its own `if (blockers.length === 0)` condition; it does not prove the service prevents card issue.

**Required fix:** add a journey-level ordered orchestration method, or make `executeStage` reject subsequent stages when an unresolved blocker exists. Test that attempting card issue after a caught failure/offline error is rejected by production logic and that its callback is never invoked.

The structural harness still passes 6/6 because it greps for test text, not the security properties.
