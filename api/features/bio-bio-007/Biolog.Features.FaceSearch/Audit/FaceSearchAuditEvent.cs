namespace Biolog.Features.FaceSearch.Audit;

/// <summary>
/// AC05: "Population scope and audit are explicit." One immutable, append-only record of a
/// single 1:N face search attempt - the durable trail that makes AC05's "explicit" concrete: a
/// reviewer reading this record can see exactly WHO searched, WHEN, against WHICH population
/// (<see cref="PopulationScope"/>/<see cref="PopulationSize"/>) and HOW MANY candidates came
/// back, without re-deriving any of it from application logs.
///
/// Unlike BIO-BIO-006's own deliberate "no separate audit sink" scope decision (none of that
/// story's five ACs mentioned auditing), this story's own AC05 explicitly requires it - so this
/// feature applies the SAME audit-durability discipline every mutating service in this codebase
/// uses (ADR-0003 Decision 7, element 4): a write whose durability cannot be confirmed is a
/// genuine failure, not a fire-and-forget best-effort log line - see <c>FaceSearchService</c>'s
/// own remarks.
/// </summary>
public sealed record FaceSearchAuditEvent
{
    public required string EventId { get; init; }
    public required string SearchId { get; init; }
    public required string TenantId { get; init; }
    public required string ActorId { get; init; }
    public required string SubjectPersonId { get; init; }
    public required string PopulationScope { get; init; }
    public required int PopulationSize { get; init; }
    public required int CandidateCount { get; init; }
    public required string OccurredAtUtc { get; init; }
}
