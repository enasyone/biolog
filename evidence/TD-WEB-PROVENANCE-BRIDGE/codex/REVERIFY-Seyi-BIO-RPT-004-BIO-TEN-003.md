# TD-WEB-PROVENANCE-BRIDGE tracked re-verification

Verdict: FAIL — the bridge shipped, but the required story workflows are not yet positively proven.

Reviewed merged bridge `7ac4876` and the current BIO-RPT-004/BIO-TEN-003 LIVE Playwright specifications against the tracked obligation.

## BIO-RPT-004

`bio-rpt-004-operational-health-live.spec.ts` still contains only the pre-bridge negative case. It expects HTTP 401 `actor_provenance_required` and never establishes a signed session, tenant permission grant, successful six-panel snapshot, or role-filtered drill-down.

## BIO-TEN-003

`bio-ten-003-feature-configuration-live.spec.ts` likewise still expects HTTP 401, an error alert, no module table, and no toggle controls. It never proves an authorised GET/PATCH/GET workflow or the required unprivileged denial after the bridge.

## Bridge evidence limitation

`td-web-provenance-bridge-live.spec.ts` is not a substitute for either story workflow. Its positive case targets BIO-INT-003 and, as its own comments state, stops before a tenant permission decision. The merged round-five tenant-confinement tests prove both admin and viewer claims reach the same synthetic tenant-scoped permission denial; they do not provision the story permissions or exercise either mapped UI.

## Required closure

For each story, add and run an opt-in LIVE Playwright workflow against the merged bridge and real backend that:

1. obtains a real signed demo session through the production issuance path;
2. provisions only the synthetic tenant and the minimum story permission grants;
3. proves the authorised mapped browser workflow succeeds end to end;
4. proves an unprivileged session is denied; and
5. removes or supersedes the obsolete negative-only 401 claim.

No story implementation was changed during this independent re-verification.

