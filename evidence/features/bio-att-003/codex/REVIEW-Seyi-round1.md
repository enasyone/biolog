# BIO-ATT-003 round-one review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed implementation `88c05fa` and builder evidence `f80cc6b2` against the approved user story and AC01–AC05.

Clock-pair validation, overtime derivation, append-only adjustments with mandatory reasons, tenant-keyed repositories, provenance/RBAC on writes, and the additive UI wiring are structurally sound. Two blockers remain.

## BIO-ATT-003-F01 — lateness and absence are not interpreted (High)

The approved user story requires “clock-in/out, shift, lateness, overtime and absence interpretation.” The implementation covers clock pairing and overtime only.

`WorkforceShift.GraceMinutes` is persisted and displayed but has no consumer anywhere in the feature. `WorkforceDailySummary` exposes pairs and overtime only; it has no lateness or absence outcome. A first check-in after `StartTime + GraceMinutes` is therefore indistinguishable from an on-time arrival, and a day with no clock events is merely an empty pair list rather than an absence interpretation.

Reproduction: configure an 08:00 shift with 10 minutes grace and evaluate (a) a first check-in at 08:11 and (b) no events. Neither summary contains late/absent status or minutes-late evidence.

Required: derive explicit on-time/late/absent interpretation from the selected shift, grace period, and validated pairs, including overnight/boundary tests. Keep it derived; do not mutate raw events.

## BIO-ATT-003-F02 — AC05 relies on an obsolete pre-bridge boundary (High)

The builder based this story before merged bridge `7ac4876` and documents that authenticated browser writes cannot complete. That dependency had already shipped before builder-complete. The LIVE Playwright spec still asserts only a successful open GET and a 401 write failure.

AC05 requires the mapped human workflow to pass. The current evidence does not prove an authorised shift create/update or exception adjustment through the real browser bridge, nor the corresponding unprivileged denial. The cross-story re-verification already recorded this proof pattern as mandatory after the bridge shipped.

Required: update onto the merged bridge and add a real signed-session LIVE Playwright workflow using the confined synthetic tenant and minimum story grants, plus an unprivileged negative.

## Independent verification

- Direct xUnit executable: 46/46 passed, confirming the builder suite does not cover the missing interpretations.
- Module-reference direction check: PASS.
- Functional: FAIL (F01).
- Security/tenant isolation: PASS for write boundaries by inspection.
- Concurrency/data integrity: PASS for scoped locks, append-only adjustment storage, and audit-before-commit paths by inspection.
- UI/regression: FAIL (F02); existing approved section is preserved, but the mapped positive LIVE workflow is missing.

