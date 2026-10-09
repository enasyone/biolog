# BIO-RPT-004 round-one review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed builder `cc8374b` and evidence `da9f1fa` against BIO-RPT-004 AC01–AC05.

The dashboard service correctly obtains one tenant identifier from the trusted host context and passes it to tenant-keyed repositories. The additive ReportingService port, drill-down visibility reuse, explicit unavailable panels, and existing dashboard layout are sound. Two required criteria still fail.

## BIO-RPT-004-F01 — dashboard metrics have no authorization gate (High)

AC01 requires metrics from an **authorised** tenant scope. `DashboardEndpoints.GetOperationalHealthAsync` calls `DashboardService.GetSnapshotAsync` without resolving an actor, verifying actor provenance, or checking any permission. The service resolves a trusted tenant, which prevents caller-selected cross-tenant queries, but that is tenant scoping—not authorization.

Any role whose request receives a tenant context can call `/api/v1/dashboard/operational-health` directly and receive identity status counts, access allow/deny counts, and production-job states, even if its role has no dashboard/reporting permission. Client-side drill-down hiding does not protect the metrics endpoint.

Reproduction: send GET as a tenant role with no reporting/dashboard permission. The endpoint has no actor or permission input and returns 200 with the full snapshot.

Required: gate the endpoint with provenance-bound actor identity and a real tenant-scoped permission lookup, with negative host tests proving an unprivileged role cannot read metrics.

## BIO-RPT-004-F02 — stale/offline labelling does not model staleness (High)

AC03 requires stale/offline data to be labelled. The three real gateway panels are unconditionally marked `Live` after repository reads. They carry no source observation timestamp, age, offline state, or staleness threshold, so old repository data is always presented as live.

Conversely, the three domains with no data source are marked `Simulated` and the frontend relabels `simulated` as `Stale`. Unavailable/simulated data is not stale data; this hides the actual state from operators. A network failure yields a generic panel-level error, but no per-source offline freshness state.

Reproduction:

1. Read old identity/access/production records; the service always returns `freshness=live` regardless of age.
2. Read a card/device/sync panel with no source; the API returns `simulated`, while the UI displays `Stale`.

Required: model unavailable/simulated separately, and derive stale/offline status for real sources from meaningful timestamps/connectivity signals rather than hard-coding `Live`.

## Verification

- Structural harness: 31 passed, 0 failed; the checks assert the flawed assumptions above and therefore do not close F01/F02.
- Focused test project build: PASS, 0 warnings, 0 errors.
- Builder reports backend 11/11 and frontend 469/469 plus Playwright coverage.
- Functional: FAIL (AC03).
- Security/RBAC: FAIL (AC01).
- Tenant isolation: PASS for server-resolved single-tenant repository queries.
- Offline/resilience: FAIL (no accurate stale/offline source state).
- Regression/UI layout: PASS by inspection and builder evidence.
