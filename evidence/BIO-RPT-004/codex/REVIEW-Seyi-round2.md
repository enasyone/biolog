# BIO-RPT-004 round-two review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed the superseding implementation `b0f62e0` (builder remediation `308ce8a`, evidence `d9ae548`, late evidence `6046afa`) against BIO-RPT-004 AC01–AC05.

Round two correctly separates `Live`, `Stale`, and `Unavailable`, isolates source failures per panel, adds timestamps, and fixes the dashboard locator collision. The original unauthorised endpoint and freshness-semantics findings are not fully closed.

## BIO-RPT-004-F03 — permission check trusts a caller-asserted actor identity (Critical)

The endpoint now checks a real permission store, but `ActorIdHeader.Resolve` accepts raw `X-Actor-Id` and uses it directly as the permission subject. There is no ActorProvenanceGate or other authentication binding before `PermissionService.HasPermissionAsync`.

An attacker who knows or guesses a privileged PERSON identifier can send that identifier in `X-Actor-Id`; the real permission lookup will return the victim's grant and disclose the dashboard. A real RBAC lookup does not make a caller-controlled principal trustworthy.

Reproduction: choose a PERSON holding `dashboard.view_operational_health`, send GET with that PERSON id in `X-Actor-Id` but no provenance proof. The endpoint authorizes and reads all health gateways.

Required: verify a cryptographic actor-provenance proof before the permission lookup, following the established ADR-0006 Decision 3 pattern, and test spoofing a privileged actor fails before any gateway call.

## BIO-RPT-004-F04 — failed reads fabricate zero-valued “stale” snapshots (High)

On any real gateway exception, `ReadAsync` creates a `Stale` panel whose counts are all zero and whose `ObservedAt` is `UtcInstant.Now()`—the time the failed read was handled. No last successful values or their observation timestamp are retained.

This is not stale data. It is unavailable data presented as a zero-valued observation, which can make an outage look like zero people, zero access decisions, or zero production jobs. The new timestamp compounds the problem by making the fabricated zeros appear freshly observed.

Required: either retain and label the last successful snapshot with its true observation time, or report the panel unavailable/offline without numeric counts. Do not manufacture zero metrics on failure.

## Verification

- Structural harness: 37 passed, 0 failed; its F01/F02 checks do not test privileged-actor spoofing or last-known-data semantics.
- Focused build: PASS, 0 warnings, 0 errors.
- Builder reports backend 22/22, frontend 470/470, targeted Playwright 7/7, and regenerated visual baselines.
- Functional/offline: FAIL (F04).
- Security/RBAC: FAIL (F03).
- Tenant isolation: PASS for server-resolved tenant scope; actor authenticity fails.
- Regression/UI: PASS for the late CSS locator fix.
