namespace Biolog.Features.Integrations.Domain;

/// <summary><c>internal</c> - same auditable seam every sibling story's own simple stores use. Write-once (see <see cref="ImportBatch"/>'s own header) - no replace/update method.</summary>
internal interface IImportBatchRepository
{
    Task<ImportBatch> CreateAsync(ImportBatch batch, CancellationToken cancellationToken = default);

    Task<ImportBatch?> GetAsync(string tenantId, string batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportBatch>> ListByMappingAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default);
}
