# BIO-ATT-003 independent review — round 2

**Verdict: FAIL**

Reviewed builder `6d7e433` / evidence `6046885` against the round-1 F01/F02 findings. F02 is closed: the route table, configuration-gated synthetic RBAC grants, real-host admin 201/viewer 403 coverage, and opt-in LIVE browser workflow now prove the signed-session path. F01 remains incorrect for a common boundary case.

## Blocking finding

### SY-ATT003-03 — High — an early arrival is classified as almost 24 hours late

`AttendanceInterpretationEvaluator` computes:

```text
minutesAfterStart = ((arrivalMinutes - startMinutes) % 1440 + 1440) % 1440
```

and treats any result beyond grace as late. For a normal 08:00–17:00 shift with 10 minutes grace, a 07:55 check-in produces `1435` minutes after start and is reported **Late by 1425 minutes**, rather than On Time. The same defect affects every pre-start arrival on an ordinary daytime shift.

The modulo operation was added to support after-midnight arrivals for overnight shifts, but it is applied unconditionally and has no shift-date/calendar context with which to distinguish “early before today's shift” from “after yesterday's overnight start.” The 11 new evaluator tests cover exact start, post-start grace/late, absence, and overnight wrapping, but omit any early arrival.

**Reproduction:**

- shift: `start_time=08:00`, `end_time=17:00`, `grace_minutes=10`
- first CheckIn: `2026-10-07T07:55:00Z`
- current result: `status=late`, `minutes_late=1425`
- required interpretation: early/on-time, never late

**Required remediation:** handle ordinary pre-start arrivals as on-time (or expose a separate early status if approved). For overnight shifts, use explicit date/shift-instance context rather than treating every negative wall-clock delta as a next-day arrival. Add ordinary early-arrival and overnight-before-start boundary tests.

## Category verdicts

- Functional: **FAIL** — early arrivals are materially misclassified.
- Security: **PASS** — signed provenance and real RBAC differentiation are now wired.
- Tenant isolation: **PASS** — ambient tenant remains confined to the signed synthetic session in reverify coverage.
- Offline/recovery: **NOT APPLICABLE** to this remediation.
- Concurrency: **PASS** for unchanged shift/adjustment paths.
- Regression: focused feature assembly **61/61 PASS**.

No production code was changed by this review.
