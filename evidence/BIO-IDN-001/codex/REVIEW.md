# BIO-IDN-001 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation commit `72b8a97` at builder evidence head `9bc2dcb`.

## BIO-IDN-001-QA-01 — High — the public repository bypasses tenant, lifecycle, and audit governance

`IPersonRepository.CreateAsync` and `ReplaceAsync` are public application services, the concrete repository is public, and `Person` has a public object initializer. A consumer can therefore write a caller-built aggregate without `PersonService`, which is the only layer that resolves tenant context, validates lifecycle transitions, and writes audit events.

The replacement boundary also checks only that the stored record matches `expectedCurrent.TenantId`; it does not require `updated.TenantId` to equal either value. The independent executable probe creates a person in tenant A, then calls `ReplaceAsync` directly with a copied record whose tenant is B and whose status jumps from `PRE_ENROLLED` to `ARCHIVED`. The call succeeds. Tenant A loses the record, tenant B gains it, and no audit sink is involved.

This violates AC02's mandatory tenant boundary, AC03's governed lifecycle, AC04's governed deletion, and AC05's requirement that all mutations are audited. It also disproves the structural harness's claim that the repository asserts tenant identity on every write.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-001/codex/RepositoryBypassProbe/RepositoryBypassProbe.csproj
```

Observed:

```text
REPRODUCED: public repository moved and archived PERSON without PersonService or audit
```

Required closure: make the unaudited persistence mutation surface inaccessible to ordinary production consumers, and enforce immutable identity/tenant fields plus valid aggregate state at the persistence boundary. Tests must attempt direct creation and replacement with an empty/foreign tenant, an altered person identifier, an illegal lifecycle jump, and a mutation that has no audit dependency. Public aggregate construction must not let callers manufacture a record that merely has the expected shape.

## Other evidence

- The shipped-host integration and service-level tenant scoping are present.
- All eight required lifecycle states exist and the service transition table is enforced.
- No hard-delete method exists; service-level archival is audited and compare-and-set protected.
- BIO-IDN-001 structural suite: 38/38 PASS, but its write-boundary assertion is a false positive exposed by the probe.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- The feature test assembly compiled locally, but the test runner was aborted because this sandbox denied its loopback socket bind; no independent xUnit result is claimed. Builder evidence records 61/61 feature tests and 54/54 integration tests.

Functional FAIL; Security FAIL; Tenant Isolation FAIL; Offline/Recovery NOT APPLICABLE; Concurrency PASS at the service boundary; Regression FAIL.
