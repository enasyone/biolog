namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>
/// Storage for <see cref="ImportConflictReview"/>, keyed by <c>(TenantId, MappingId, ExternalKey)</c>.
/// <c>internal</c> from the start (BIO-IDN-001-QA-01's lesson) - <c>InternalsVisibleTo</c> in
/// this project's <c>.csproj</c> grants access to <c>Biolog.Api</c> (DI registration) and this
/// project's own test assembly. <see cref="InMemoryImportConflictReviewRepository"/>
/// independently re-validates identity immutability and initial state at the persistence
/// boundary - the same dual-boundary discipline every sibling repository in this epic applies.
///
/// Every read and write method takes <c>tenantId</c> explicitly and asserts a matching record's
/// own <see cref="ImportConflictReview.TenantId"/> before returning it - tenant isolation
/// enforced at this boundary too, not only at the gateway that supplies conflict signals.
/// </summary>
internal interface IImportConflictReviewRepository
{
    Task<ImportConflictReview?> GetAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default);

    /// <summary>AC02's review queue: every conflict review for this mapping, tenant-scoped.</summary>
    Task<IReadOnlyList<ImportConflictReview>> ListForMappingAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default);

    Task<ImportConflictReview> CreateAsync(ImportConflictReview review, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2).</summary>
    Task<ImportConflictReview> ReplaceAsync(ImportConflictReview expected, ImportConflictReview updated, CancellationToken cancellationToken = default);
}
