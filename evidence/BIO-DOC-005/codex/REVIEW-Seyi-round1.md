# BIO-DOC-005 independent review — Seyi

Date: 2026-09-22
Verdict: FAIL

## Finding BIO-DOC-005-F01 — parser validation regex has no timeout (High)

`ParserExecutor.Validate` calls `Regex.IsMatch(rawValue, rule.Pattern)` with a pattern supplied by
the parser author and no timeout. Tenant-authorable parser rules therefore let a permitted tenant
store a catastrophic-backtracking expression (for example `^(a+)+$`) and make every execution
against a sufficiently long barcode field consume unbounded CPU. A malicious or compromised tenant
can use this as a denial of service against document parsing. The raw payload is untrusted too, so
the execution path is attacker-triggerable even when parser publication is otherwise authorized.

### Reproduction

1. Publish a tenant parser with validation pattern `^(a+)+$` for a field mapped to a long payload.
2. Call `ParserExecutor.Apply` with a non-matching string such as 30,000 `a` characters followed by `!`.
3. `Regex.IsMatch` performs catastrophic backtracking without a deadline or cancellation and can
   monopolize the process instead of returning a validation failure.

Use a bounded regex timeout (and reject/contain timeout failures) or a safe non-backtracking
validation strategy before this story can pass.

## Checks

- Independent source review found the issue above; test fixtures/build were not treated as proof of
  resistance to hostile tenant-authored patterns.
- Tenant visibility/authority and published-only execution paths otherwise appear correctly scoped.

Category verdicts: Functional PASS; Security FAIL; Tenant Isolation PASS; Offline PASS; Concurrency FAIL (unbounded parser CPU); Regression FAIL pending mitigation.
