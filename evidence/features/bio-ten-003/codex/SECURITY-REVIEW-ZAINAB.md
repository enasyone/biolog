# BIO-TEN-003 security review — round 1

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** implementation `6ce66bf`, evidence `56f04fd`

## Blocking findings

### ZB-TEN003-01 — Critical — arbitrary cross-tenant configuration read/write

`GET` accepts any canonical tenant ID from the route without authenticating or authorizing the caller. `PATCH` adds only `X-Actor-Id`, which is caller-controlled; there is no ADR-0006 provenance proof, no tenant role-based access control permission lookup, no ambient-tenant match, and no tenant-registry existence check. Any caller can therefore read another tenant's feature posture, enable Face 1:N or other modules for that tenant, create configuration for a nonexistent tenant, and forge the audit actor.

Required: bind tenant-plane requests to authenticated tenant context (or use a separately authorized control-plane administration contract), verify actor provenance, require a live tenant-admin permission for reads/writes, and verify target tenant existence. Add forged privileged actor, unprivileged actor, cross-tenant route, and nonexistent-tenant negatives.

### ZB-TEN003-02 — High — disabled Biometrics does not fail closed for Face 1:N

The live BIO-BIO-007 adapter returns only `Face1NIdentificationEnabled`. The implementation explicitly permits that child flag to be true while `Biometrics.Enabled` is false. Consequently the one-line DI swap enables `FaceSearchService` even though the parent Biometrics module is disabled, contradicting AC04's fail-closed module boundary.

Required: effective Face 1:N enablement must require both `Biometrics.Enabled` and `Face1NIdentificationEnabled`. Preserve the independently configurable stored values, but compute the runtime policy as parent AND child. Add a regression proving disabling the parent immediately denies search even when the child flag remains true.

### ZB-TEN003-03 — High — AC04 is not enforced for five configured modules

Card production, visitor, attendance, transport and Document Verify configuration is stored and exposed through a gateway, but none of their API/UI consumers consults it. Those modules therefore continue operating when this story reports them disabled. A future seam is not the required fail-closed boundary.

Required: wire each configured module into its actual API/action boundary or narrow the approved story before acceptance. Add composed-host negatives showing a disabled module/subfeature denies its operation, including after revocation.

## Adapter assessment

The `BioBio007Module` edit is mechanically narrow and preserves `IFaceSearchFeatureFlagPolicy`; the adapter is tenant-keyed and defaults unknown tenants to false. The issue is its effective-policy semantics, not the DI substitution itself.

## Other results

- Configuration updates are serialized per tenant, version-checked, audited before commit and committed non-cancellably after confirmed audit.
- Stored configuration and cache use structural tenant keys; no delimiter-collision issue found.
- Local .NET test execution was not repeated because this managed environment has consistently denied `vstest` loopback socket binding; builder evidence records 28 feature tests and 23 unchanged BIO-BIO-007 tests passing.
