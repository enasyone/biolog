namespace Biolog.Features.ImportConflictReview.Tenancy;

/// <summary>This feature's own minimal interface onto the already-shipped ambient tenant resolution (ADR-0002) - implemented in <c>Biolog.Api.Modules.BioInt005Module</c> by wrapping <c>ITenantContextAccessor</c> directly, the same dependency-inversion pattern every tenant-scoped feature in this codebase uses.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
