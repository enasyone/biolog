# BIO-ARC-001 Independent Review

Date: 2026-09-20  
Reviewed implementation commit: `fc2f7cd`  
Reviewed branch head: `0560e38`  
Verdict: **FAIL**

## Blocking finding

### BIO-ARC-001-QA-01 — impossible calendar timestamps are accepted and normalized

Severity: High  
Files: `contracts/js/clock.mjs`, `contracts/schemas/utc-instant.schema.json`, `contracts/schemas/calendar-date.schema.json`, `contracts/js/contracts.test.mjs`

The clock contract validates only numeric ranges for month and day. It does not validate that the day exists in that month or year. The reference implementation therefore accepts values such as:

- `2026-02-29T12:00:00.000Z`
- `2026-02-30T12:00:00.000Z`
- `2026-04-31T12:00:00.000Z`

`isUtcInstant()` returns `true`, and `parseInstant()` silently normalizes those values to March 1, March 2, and May 1 respectively. `isCalendarDate()` and both published schemas accept the equivalent impossible dates as well.

This contradicts ADR-0001's boundary rule that invalid timestamps are rejected rather than guessed. It can change a supplied audit or device time into a different instant without reporting an error. The contract must validate real Gregorian dates, preserve valid leap days, align code and schemas, and add regression tests.

Reproduction:

```sh
bash evidence/BIO-ARC-001/codex/independent-review.sh
```

Expected result on the reviewed commit: the AC02 impossible-date probes fail and the script exits `1`.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 canonical identifier strategy | PASS |
| AC02 UTC internally and explicit local rendering | FAIL |
| AC03 optimistic concurrency/version rules | PASS |
| AC04 shared code/schema/tests | FAIL — the shared clock contract and its tests permit QA-01 |

## False-success and regression checks

- Builder foundation suite: 20/20 passed.
- JavaScript contract suite: 45/45 passed.
- Deliberately weakening the UTC matcher caused the JavaScript suite to fail.
- Removing `version` from the mutable-resource required fields caused the JavaScript suite to fail.
- The supplied suite nevertheless passes while impossible calendar timestamps are accepted, exposing the missing boundary case.

## Category verdicts

- Functional: FAIL.
- Security: FAIL — invalid claimed/audit instants can be silently changed.
- Tenant isolation: PASS for the documented identifier/tenant-boundary rule.
- Offline/recovery: FAIL — offline device `occurred_at` claims are affected.
- Concurrency: PASS.
- Regression: FAIL.

No implementation files were changed.
