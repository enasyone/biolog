# BIO-OPS-001 independent review — Seyi

Date: 2026-10-06
Builder commit reviewed: `adb7e9e` (implementation `121f00e`)

## Verdict

FAIL — AC03 is not demonstrated. The test fabricates PERSON linkage in its receipt instead of obtaining linked evidence from the photo, fingerprint, face, or document operations.

## Finding BIO-OPS-001-F01 — downstream evidence is not bound to the enrolled PERSON

Severity: Blocking

The journey obtains `personId` from `identityMockPort.submitEnrolment`, but then calls:

- `photosMockPort.primaryAction()`
- `biometricsMockPort.captureFingerprint('right_index', 'good')`
- `biometricsMockPort.captureFace('good')`
- `documentsMockPort.capture()` / `checkAuthenticity()`

None of those calls receives the enrolled `personId`, and their result contracts do not return a PERSON identifier. The test then constructs a new local `receipt` and manually copies the same `personId` into every stage. The final `Set` assertion therefore proves only that the test repeated one variable; it does not prove that any downstream evidence traces to that PERSON.

The card step is the sole downstream operation that genuinely receives `personId` and confirms it in its message.

## Reproduction

1. Run `bash tests/operations/BIO-OPS-001.test.sh`; it reports 6/6 PASS because AC03 is checked only by grepping for the receipt `Set` assertion.
2. Inspect `tests/frontend/unit/bio-ops-001-enrolment-journey.test.ts`: the photo, biometric, and document calls have no `personId` argument.
3. Inspect the corresponding port contracts in `web/features/biometrics/ports.ts` and `web/features/documents/ports.ts`; those operations expose no PERSON linkage in their request or result.

## Required remediation

Use a real local enrolment/session aggregate or explicit correlation contract so every produced photo, fingerprint, face, document-verification, and card artifact carries or can be queried by the same enrolled PERSON. Assert linkage from returned/persisted evidence, not from a receipt assembled by the test itself.

## Other categories

- Functional: FAIL (AC03; therefore AC05 blocking-defects-zero also fails).
- Security/privacy: PASS/APPLICABLE; fixtures are synthetic and no secrets were found in the changed files.
- Tenant isolation: NOT PROVEN by this journey; only identity/card calls receive `tenantId`.
- Offline/concurrency: NOT APPLICABLE to this acceptance-only local simulation.
- Regression: Builder-reported suites are green, but they do not cure the missing cross-stage PERSON linkage.
