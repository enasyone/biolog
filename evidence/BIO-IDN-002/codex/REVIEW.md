# BIO-IDN-002 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation commit `29a2fd3` at builder evidence head `7c20444`.

## BIO-IDN-002-QA-01 — High — profiles can attach to a missing or foreign-tenant PERSON

`PersonProfileService.AssignAsync` validates only that `personId` has canonical `per_` syntax. It never proves that BIO-IDN-001 contains that PERSON in the resolved tenant. The packaged `BioIdn002Module` declares an ordering dependency on `PersonRegistry` but deliberately has no runtime existence dependency.

The independent compiled probe assigns a `STUDENT` profile in tenant B to an arbitrary canonical PERSON identifier without creating that PERSON anywhere. Assignment succeeds, the orphan profile is stored, and an `ALLOWED / profile_assigned` audit event is permanently emitted. The same path lets tenant B attach its own profile data to a known tenant-A PERSON identifier because canonical identifiers carry no authority.

This violates the master model and aggregate contract that profiles attach to the permanent PERSON, defeats AC04's existing-person premise, creates orphan operational history, and breaks the mandatory tenant boundary inherited from BIO-IDN-001. The structural suite currently marks the absence of an existence check as PASS, so it positively enshrines the defect.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-002/codex/OrphanProfileProbe/OrphanProfileProbe.csproj
```

Observed:

```text
REPRODUCED: profile assigned and audited without proving PERSON exists in the resolved tenant
```

Required closure: expose a narrow, read-only BIO-IDN-001 existence gateway and wire it through the packaged host; before audit or profile commit, require the PERSON to exist in the resolved tenant and return the same not-found response for missing and foreign-tenant identifiers. Add composed-host tests for a real same-tenant PERSON success, an unknown canonical identifier failure, and a tenant-B attempt against a tenant-A PERSON failure. No failure may leave a profile or false-success audit event.

## Other evidence

- Multiple different profile types coexist and duplicate `(person, type)` assignment is atomically rejected.
- Organisation, campus, and branch values round-trip as optional bounded strings.
- Per-profile activation, expiry, revocation, and concurrency behavior are independently represented.
- BIO-IDN-002 structural suite: 42/42 PASS, but its AC04 existence assertion is a false-positive policy choice exposed by the probe.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Builder evidence records API build clean, feature tests 45/45, unit tests 154/154, integration tests 57/57, and full quality PASS.

Functional FAIL; Security FAIL; Tenant Isolation FAIL; Offline/Recovery NOT APPLICABLE; Concurrency PASS for the profile repository; Regression FAIL.
