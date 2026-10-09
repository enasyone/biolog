namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>Persistence seam for <see cref="FingerprintDuplicateReviewEntry"/> (AC03). This story
/// ships only <see cref="InMemoryFingerprintDuplicateReviewRepository"/>; a later story can
/// implement this against a real datastore without <see cref="Service.FingerprintSearchService"/>
/// changing shape. Internal - reachable only from this assembly, <c>Biolog.Api</c> and this
/// feature's own test project (BIO-IDN-001-QA-01's governance shape).</summary>
internal interface IFingerprintDuplicateReviewRepository
{
    Task<FingerprintDuplicateReviewEntry> CreateAsync(FingerprintDuplicateReviewEntry entry, CancellationToken cancellationToken);

    Task<FingerprintDuplicateReviewEntry?> GetAsync(
        string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken);

    Task<FingerprintDuplicateReviewEntry> ReplaceAsync(
        FingerprintDuplicateReviewEntry expected, FingerprintDuplicateReviewEntry updated, CancellationToken cancellationToken);

    /// <summary>AC03's review queue: every pending-or-decided entry for one tenant. Empty (never
    /// null) when nothing has been raised.</summary>
    Task<IReadOnlyList<FingerprintDuplicateReviewEntry>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);
}
