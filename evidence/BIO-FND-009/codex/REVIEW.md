# BIO-FND-009 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `bcc2d8f` at evidence head `067b6d6`.

AC01–AC03 wiring is present and the supplied foundation suite passes 24/24. OpenTelemetry tracing/metrics, correlation and tenant scope, and the local developer view are implemented. AC04 is not satisfied.

## Finding

### BIO-FND-009-QA-01 — High — Default formatter leaks classified payloads unless their property name matches a denylist

`SensitiveLogKeys` classifies only the structured property's name. A secret or biometric object logged under a generic name such as `Payload` is serialized through `ToString()` into both `message` and `properties` without redaction. `BiologJsonConsoleFormatter` also writes `Exception.ToString()` verbatim, so exception messages containing credentials, tokens, connection strings, or biometric data bypass the key matcher entirely.

The independent executable probe logged a synthetic biometric template under `Payload` and a synthetic password in an exception. Both exact values appeared in the emitted JSON. This contradicts AC04's requirement that secrets and biometric payloads are excluded from logs by default and ADR-0003's classification of biometric templates as secret/never emitted.

The implementation documentation explicitly acknowledges that an unrecognized field name is logged in full, so this is the designed boundary rather than an isolated missing synonym.

Required closure: make the default logging boundary exclude classified payloads independently of caller-chosen field names, and sanitize or suppress exception detail. Add executable regressions for generic/nested payload names, custom state/formatters, and exceptions; no raw classified value may reach message, properties, scope, or exception output.

## Reproduction

```sh
bash evidence/BIO-FND-009/codex/independent-review.sh
bash tests/foundation/BIO-FND-009.test.sh
```

Independent executable probe: FAIL, leaked biometric payload and exception secret. Supplied foundation suite: 24/24 PASS, demonstrating the missing boundary coverage.

## Category verdicts

- Functional: FAIL — AC04 is not met.
- Security: FAIL — secret and biometric payloads reach the default log sink.
- Tenant isolation: PASS for correlation/tenant scope wiring; classified tenant data leakage remains covered by Security FAIL.
- Offline/recovery: PASS for the structural baseline.
- Concurrency: not applicable.
- Regression: FAIL — redaction tests cover sensitive names only, not classified values under generic names or exceptions.
