# BIO-OPS-001 security review — Zainab

## Verdict: FAIL (High)

### F01 — The journey fabricates PERSON linkage instead of verifying it

AC03 requires the resulting photo, fingerprint, face, document-verification, and card records to be attached to the same PERSON. Only identity enrolment and card issue receive `personId`. `photosMockPort.primaryAction()`, `captureFingerprint()`, `captureFace()`, `documentsMockPort.capture()`, and `checkAuthenticity()` receive no tenant or PERSON identifier and return no persisted record identifier that can be resolved back to the PERSON. The test then constructs a local `receipt` and assigns the enrolment `personId` to every stage itself. The `Set` assertion therefore proves only that the test copied one string six times, not that any biometric/document record is linked to that PERSON. This can conceal cross-person or orphaned evidence association.

**Required fix:** exercise ports or repositories that persist each stage against the created tenant/PERSON, then independently read the resulting records and assert their stored tenant and PERSON ownership. Do not satisfy linkage by decorating unbound results inside the test.

### F02 — Failure and offline outcomes are retried until hidden

`runDocumentStep` retries up to four times and returns the first success. The document mock deliberately cycles through success, failure, and offline states, so the journey can encounter a blocking failure/offline result and still report a successful final receipt. The final assertion sees only the selected success and cannot prove AC05’s requirement that a failure prevents full completion.

**Required fix:** use a deterministic success scenario for the happy-path acceptance test and add explicit failure/offline journeys asserting that later stages, especially card issue, are not invoked.

The structural harness reports 6/6, but its AC03 and AC05 checks only grep for assertion text and reproduce both false assurances above.
