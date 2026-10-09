namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>AC01: "Search returns ranked candidates and scores." The whole outcome of one
/// <see cref="Service.FingerprintSearchService.SearchAsync"/> call. <see cref="RankedCandidates"/>
/// is ordered by <see cref="FingerprintSearchCandidate.Score"/> descending, highest-similarity
/// first, capped at the caller-requested candidate count.</summary>
public sealed record FingerprintSearchResult
{
    public required string SearchId { get; init; }
    public required string TenantId { get; init; }
    public required string SubjectPersonId { get; init; }
    public required IReadOnlyList<FingerprintSearchCandidate> RankedCandidates { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderVersion { get; init; }
    public required double ThresholdUsed { get; init; }
    public required string OccurredAtUtc { get; init; }
}
