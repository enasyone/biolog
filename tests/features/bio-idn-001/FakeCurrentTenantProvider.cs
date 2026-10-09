using Biolog.Features.PersonRegistry.Tenancy;

namespace Biolog.Features.PersonRegistry.Tests;

/// <summary>
/// Test double for <see cref="ICurrentTenantProvider"/>. In production this is backed by the
/// real, packaged host's <c>Biolog.Api.Modules.BioIdn001Module.CurrentTenantProviderAdapter</c>,
/// which reads the real <c>ITenantContextAccessor</c> populated by
/// <c>TenantContextMiddleware</c> - proven by
/// <c>tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs</c> through the actual
/// packaged host, not merely against this double. This double exists to prove
/// <see cref="PersonService"/>'s own logic in isolation (ADR-0002 Decision 6's "injected-
/// resolved-context test proving the feature's own logic is correct given a tenant" half),
/// mutable per test so a test can simulate a missing tenant context by setting it to null.
/// </summary>
public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}
