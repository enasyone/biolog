namespace Biolog.Features.Integrations.Domain;

/// <summary><c>internal</c> - the explicit, auditable seam for <c>Biolog.Api</c> (DI registration) and this project's own test assembly, the same BIO-IDN-001-QA-01 discipline every sibling feature applies.</summary>
internal interface IImportSourceMappingRepository
{
    Task<ImportSourceMapping> CreateAsync(ImportSourceMapping mapping, CancellationToken cancellationToken = default);

    Task<ImportSourceMapping?> GetAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportSourceMapping>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 Decision 3.2).</summary>
    Task<ImportSourceMapping> ReplaceAsync(ImportSourceMapping expectedCurrent, ImportSourceMapping updated, CancellationToken cancellationToken = default);
}
