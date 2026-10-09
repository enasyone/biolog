# BIO-IDN-003 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation commit `bec3363` at builder evidence head `4580a50`.

## BIO-IDN-003-QA-01 — High — enforced tenant-wide uniqueness races across different people

`AssignAsync` locks by `(PersonId, Type, CustomLabel)`, but its uniqueness rule is tenant-wide by `(TenantId, Type, CustomLabel, Value)`. Two assignments for different people therefore hold different locks. Both can call `ValueExistsAsync`, both observe the value absent, and both proceed to audit and commit. `InMemoryInstitutionalIdentifierRepository.CreateAsync` does not enforce the tenant-wide value constraint atomically; it checks only the per-person active-record invariant.

The deterministic compiled probe gates two different-person assignments after each has captured `ValueExistsAsync == false`. Releasing them together produces two stored `MATRICULATION_NUMBER` records with the same value while enforcement is on, plus two permanent `ALLOWED / identifier_assigned` audit events.

This violates AC02's configured uniqueness rule and leaves false-success audit evidence. The separate uniqueness-rule lock also does not coordinate with assignment, so enabling or disabling a rule can race with in-flight writes against the same policy scope.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-003/codex/UniquenessRaceProbe/UniquenessRaceProbe.csproj
```

Observed:

```text
REPRODUCED: enforced tenant-wide uniqueness admitted two concurrent identical values
```

Required closure: enforce the configured tenant/type/custom-label/value uniqueness atomically at the persistence boundary or serialize all assignments in the policy scope, including rule changes. Add a deterministic two-person same-value race proving exactly one commit and one matching success audit. Enabling enforcement must also reject or explicitly reconcile an already-duplicated scope rather than recording an enforced rule that existing data violates.

## Other evidence

- Identifier types, tenant-scoped reads, PERSON existence checks, and same-person active-record governance are present.
- Retirement preserves immutable historical records and reissue sequence numbers.
- No credential authority or credential identifier surface is exposed.
- BIO-IDN-003 structural suite: 56/56 PASS, but it covers only same-identifier contention and misses the tenant-wide uniqueness race.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Builder evidence records API build clean, feature tests 52/52, unit tests 154/154, integration tests 64/64, and the non-full quality gate PASS.

Functional FAIL; Security PASS; Tenant Isolation PASS; Offline/Recovery NOT APPLICABLE; Concurrency FAIL; Regression FAIL.
