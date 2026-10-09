namespace Biolog.Features.FaceSearch.Domain;

/// <summary>
/// AC01: "Search returns ranked candidates with similarity." The whole outcome of one
/// <see cref="Service.FaceSearchService.SearchAsync"/> call. <see cref="RankedCandidates"/> is
/// ordered by <see cref="FaceSearchCandidate.Score"/> descending, highest-similarity first,
/// capped at the caller-requested candidate count.
///
/// <b>AC05 - "population scope ... are explicit".</b> <see cref="PopulationScope"/> and
/// <see cref="PopulationSize"/> name exactly what this search ran against, visibly, on the
/// result itself (and on the matching <c>Audit.FaceSearchAuditEvent</c>) - never an implicit,
/// undocumented default a caller has to infer.
/// </summary>
public sealed record FaceSearchResult
{
    public required string SearchId { get; init; }
    public required string TenantId { get; init; }
    public required string SubjectPersonId { get; init; }
    public required IReadOnlyList<FaceSearchCandidate> RankedCandidates { get; init; }
    public required string PopulationScope { get; init; }
    public required int PopulationSize { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderVersion { get; init; }
    public required string OccurredAtUtc { get; init; }
}
