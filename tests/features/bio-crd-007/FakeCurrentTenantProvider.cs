using Biolog.Features.CardProduction.Tenancy;

namespace Biolog.Features.CardProduction.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}
