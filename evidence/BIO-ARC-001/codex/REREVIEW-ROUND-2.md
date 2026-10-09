# BIO-ARC-001 Independent Re-review — Round 2

Date: 2026-09-20  
Reviewed fix commit: `8e39e91`  
Reviewed branch head: `e657f26`  
Verdict: **FAIL**

## Prior finding

- QA-01: **CLOSED**. Impossible dates such as non-leap February 29, February 30, and April 31 are rejected by the code and schemas. Valid leap dates for 2000, 2024, and 2028 remain accepted. The supplied regression suite covers month lengths, ordinary leap years, and century rules.

## New blocking finding

### BIO-ARC-001-QA-02 — code and schemas disagree on year-zero leap day

Severity: High  
Files: `contracts/js/clock.mjs`, `contracts/schemas/utc-instant.schema.json`, `contracts/schemas/calendar-date.schema.json`, `contracts/js/contracts.test.mjs`

The reference code applies its stated proleptic Gregorian leap rule to all four-digit years and accepts `0000-02-29` in both `isCalendarDate()` and `isUtcInstant()`; `parseInstant()` also round-trips it unchanged. Both published schema patterns reject the same date. Their other year-zero dates, including `0000-02-28` and `0000-03-01`, are accepted by both layers.

This leaves language bindings generated from the schemas stricter than the JavaScript reference implementation, violating AC04's shared-contract requirement. The result is inconsistent cross-surface validation at a four-digit boundary explicitly allowed by the current code shape. Either year zero must be rejected consistently or its leap day accepted consistently. Regression parity tests must include the lower boundary.

Reproduction:

```sh
bash evidence/BIO-ARC-001/codex/independent-review.sh
```

Expected result: all QA-01 and AC01/AC03 checks pass; the year-boundary code/schema parity probe fails and the script exits `1`.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 canonical identifier strategy | PASS |
| AC02 UTC internally and explicit local rendering | PASS in code; schema boundary is inconsistent |
| AC03 optimistic concurrency/version rules | PASS |
| AC04 shared code/schema/tests | FAIL |

## Regression summary

- Builder foundation suite: 22/22 passed.
- JavaScript contract suite: 53/53 passed.
- Original independent impossible-date and valid-leap probes passed.
- AC01 identifier and AC03 missing/stale-version probes passed.
- Current schema/code parity tests sweep 1900, 2000, 2024, and 2026 but omit the lower four-digit boundary where the definitions diverge.

## Category verdicts

- Functional: FAIL.
- Security: PASS.
- Tenant isolation: PASS.
- Offline/recovery: PASS.
- Concurrency: PASS.
- Regression: FAIL.

No implementation files were changed.
