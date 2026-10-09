using Biolog.Features.Photography.Tenancy;

namespace Biolog.Features.Photography.Tests;

/// <summary>Test double for <see cref="ICurrentTenantProvider"/> - see BIO-IDN-001's identically-named class for the full rationale.</summary>
public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}
