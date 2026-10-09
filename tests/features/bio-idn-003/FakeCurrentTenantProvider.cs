using Biolog.Features.InstitutionalIdentifiers.Tenancy;

namespace Biolog.Features.InstitutionalIdentifiers.Tests;

/// <summary>
/// Test double for <see cref="ICurrentTenantProvider"/> - see BIO-IDN-001's identically-named
/// class for the full rationale (proving this feature's own logic in isolation is one half of
/// ADR-0002 Decision 6's evidence requirement; the real packaged-host half is
/// <c>tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs</c>).
/// </summary>
public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}
