# BIO-FND-009 independent re-review — round 4

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed QA-03 remediation `f328577` at evidence head `8ca5478`.

QA-01 through QA-03's structured-value cases are closed: category and scope-key labels no longer grant trust, and only explicit `SafeLogValue` values pass `Classify`. A separate message path still bypasses `Classify` entirely.

## Finding

### BIO-FND-009-QA-04 — High — Custom log state delegates classified output to the caller's formatter

When a log entry's state is not the standard structured list ending in `{OriginalFormat}`, `RenderMessage` returns null and `Write` calls `logEntry.Formatter(...)` verbatim. That caller-controlled string is written as `message` without withholding, redaction, or `SafeLogValue` classification.

The extended executable probe creates `LogEntry<string>` with opaque custom state and a formatter returning a synthetic biometric value. The exact value appears in the JSON message while all structured reproductions from QA-01 through QA-03 are blocked.

This path is already described by the implementation as its fallback for custom `TState`, but its supplied test asserts the fallback output is preserved rather than checking the AC04 boundary. It contradicts the claimed single decision point for every value written and the original QA-01 closure requirement covering custom state/formatters.

Required closure: default-withhold messages that cannot be parsed through the controlled structured template path, or require a non-forgeable/explicit safe message contract. Add regressions for custom `TState`, caller formatters, literal classified message templates, and malformed templates; raw classified text must not reach `message`.

## Reproduction

```sh
bash evidence/BIO-FND-009/codex/independent-review.sh
bash tests/foundation/BIO-FND-009.test.sh
```

Independent probe: FAIL, leaked custom formatter payload. Supplied foundation suite: 35/35 PASS.

## Category verdicts

- Functional: FAIL — AC04 remains bypassable.
- Security: FAIL — caller-controlled formatter output reaches logs verbatim.
- Tenant isolation: PASS for correlation/tenant scope; arbitrary custom-message confidentiality remains covered by Security FAIL.
- Offline/recovery: PASS.
- Concurrency: not applicable.
- Regression: PASS for QA-01 through QA-03 structured paths; FAIL for custom state/formatter coverage.
