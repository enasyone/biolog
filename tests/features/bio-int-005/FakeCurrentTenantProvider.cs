using Biolog.Features.ImportConflictReview.Tenancy;

namespace Biolog.Features.ImportConflictReview.Tests;

/// <summary>A per-test, directly-settable stand-in for the host's own ambient tenant resolution (ADR-0002) - mirrors every sibling feature test project's own identically-named fake.</summary>
public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}
