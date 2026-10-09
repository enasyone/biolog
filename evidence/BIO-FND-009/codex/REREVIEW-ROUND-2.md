# BIO-FND-009 independent re-review — round 2

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed QA-01 remediation `3f7ed20` at evidence head `9fb1352`.

The original caller-defined object and exception-message reproductions are closed: unknown object types are withheld and exception messages are suppressed. AC01–AC03 remain intact. AC04 remains bypassable.

## Finding

### BIO-FND-009-QA-02 — High — Generic string payloads are unconditionally trusted and logged

The new type allowlist labels every `string` as a definitely safe scalar. A caller can therefore log the same raw biometric template, token, password, or connection string under a generic key such as `PayloadText`; the formatter emits it verbatim into both `message` and `properties`. Arrays of strings inherit the same trust.

The extended independent executable probe uses the same synthetic biometric content as the original finding, represented as a normal string under `PayloadText`. The object and exception values are now absent, but the generic string appears unchanged in the emitted JSON.

Strings have no inherent data classification, so type checking cannot establish that they are safe. This still contradicts AC04 and ADR-0003's rule that secret biometric templates are never emitted.

Required closure: do not treat arbitrary strings or string sequences as inherently safe. Require a positive, enforceable safe-field/value contract at the logging boundary (or an equivalent design that defaults unknown values to withheld), and test generic string/byte/collection payloads across message, properties, scope, custom state, and exceptions.

## Reproduction

```sh
bash evidence/BIO-FND-009/codex/independent-review.sh
bash tests/foundation/BIO-FND-009.test.sh
```

Independent probe: FAIL, leaked generic string payload. Supplied foundation suite: 28/28 PASS.

## Category verdicts

- Functional: FAIL — AC04 remains bypassable.
- Security: FAIL — classified strings are emitted by default under generic names.
- Tenant isolation: PASS for context wiring; tenant payload confidentiality remains covered by Security FAIL.
- Offline/recovery: PASS.
- Concurrency: not applicable.
- Regression: PASS for the original object/exception cases; FAIL for the adjacent scalar boundary.
