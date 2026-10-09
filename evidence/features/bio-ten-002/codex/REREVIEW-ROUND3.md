# BIO-TEN-002 remediation re-review — round 3

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed shared-host integration through implementation commit `bec96a5f719152f0c30b1130b52e40d2ac2033f1` at builder evidence head `b3d5301`.

## Prior finding status

- QA-01 closed: `BioTen002Module` is registered in the packaged `Biolog.Api`; `TenantContextMiddleware` calls `TenantDomainService.ResolveAsync` with the real request host; verified, unknown, and pending-host behavior is covered in the real `WebApplicationFactory<Program>` pipeline.
- QA-02 remains closed: malformed colon/port suffixes fail exact resolution.

## BIO-TEN-002-QA-03 — High — resolved tenant context is publicly forgeable and replaceable

The integration turns the former placeholder `TenantContext` into a live tenant-security boundary, but leaves both creation and mutation public:

```csharp
public sealed record TenantContext(string TenantId, string BindingKey, IReadOnlyList<string> Channels);

public interface ITenantContextAccessor
{
    TenantContext? Context { get; set; }
}
```

Any downstream component that can inject the scoped `ITenantContextAccessor` can replace the middleware-resolved tenant during the request with `new TenantContext(victimTenant, ...)`. The public record constructor accepts noncanonical tenant ids and caller-selected bindings/channels, and the `IReadOnlyList<string>` can still wrap a mutable array whose contents change after resolution. Existing tests demonstrate the setter directly by preloading and replacing contexts.

This directly violates accepted ADR-0002 Decision 3: after resolution there must be “No setter, no swap,” nested/asynchronous work must inherit the same frozen object, and changing context mid-request must fail as `tenant_context_immutable`. It also defeats Decision 4.1's requirement that a context be provably the product of resolution rather than an object that merely has the right shape. This is now reachable by tenant-plane code: `DeviceService` trusts `ITenantContextAccessor.Context` as its tenant authority.

### Reproduction

```sh
rg -n 'TenantContext\? Context \{ get; set; \}' api/Biolog.Api/Tenancy/ITenantContextAccessor.cs
rg -n 'public sealed record TenantContext\(' api/Biolog.Api/Tenancy/TenantContext.cs
rg -n 'Context = new TenantContext' tests/dotnet api/Biolog.Api
```

Required closure: expose a read-only accessor to consumers; restrict context construction and the one authoritative set operation to the resolver boundary; enforce set-once/no-swap semantics; freeze/copy the channels collection; reject malformed tenant identifiers; and add tests proving forged construction, reassignment, and collection mutation cannot change the tenant seen by downstream work. Test-only direct-context setup must use an explicit test seam rather than production's mutable authority.

## Regression evidence

- BIO-TEN-002 structural suite: 37/37 PASS.
- BIO-FND-008 structural suite: 39/39 PASS.
- Builder evidence records API build clean, feature 90/90, unit 152/152, integration 49/49, and BIO-TEN-001 51/51.
- Local .NET replay was unavailable because the sandbox denied MSBuild/testhost pipe/socket creation; no independent runtime PASS is claimed.

Functional PASS; Security FAIL; Tenant Isolation FAIL; Offline/Recovery NOT APPLICABLE; Concurrency PASS; Regression FAIL.
