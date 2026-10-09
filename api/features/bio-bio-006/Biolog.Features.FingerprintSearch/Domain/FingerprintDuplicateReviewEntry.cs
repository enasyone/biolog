namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>
/// AC03: "Possible duplicate routes to human review." One suspected-duplicate PAIRING between
/// two PERSON ids in the same tenant, raised automatically by a 1:N search whose score met the
/// tenant's duplicate-review threshold, and resolved only by an explicit human decision (AC04:
/// never resolved by this feature merging anything).
///
/// <b>Natural key, not a registered identifier.</b> <see cref="TenantId"/>/<see cref="PersonIdLow"/>/
/// <see cref="PersonIdHigh"/> - the two person ids ordered by <see cref="string.CompareOrdinal(string, string)"/>
/// so the unordered pair always resolves to exactly one record regardless of which order a
/// caller/search names them in (see <see cref="OrderPair"/>) - the identical shape BIO-IDN-005's
/// own <c>DuplicateCandidate</c> uses for the same reason.
///
/// <b>Signals accumulate; a decided pair is never silently reopened.</b> <see cref="Score"/> is
/// updated to the highest score any search has observed for this pair (never lowered by a later,
/// weaker match); once <see cref="Status"/> leaves <see cref="FingerprintDuplicateReviewStatus.PendingReview"/>,
/// a later search that would otherwise raise/augment this pair leaves it untouched instead - a
/// human already reviewed it (see <see cref="Service.FingerprintSearchService"/>'s remarks).
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start). The
/// constructor is <c>internal</c>; <c>InternalsVisibleTo</c> grants the same access to
/// <c>Biolog.Api</c> and this project's own test assembly.
/// </summary>
public sealed record FingerprintDuplicateReviewEntry
{
    public string TenantId { get; }
    public string PersonIdLow { get; }
    public string PersonIdHigh { get; }
    public required double Score { get; init; }
    public required FingerprintDuplicateReviewStatus Status { get; init; }
    public string? DecidedAtUtc { get; init; }
    public string? DecidedByActorId { get; init; }
    public required int Version { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }

    internal FingerprintDuplicateReviewEntry(string tenantId, string personIdLow, string personIdHigh)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("must be a non-empty string", nameof(tenantId));
        }
        if (string.IsNullOrWhiteSpace(personIdLow) || string.IsNullOrWhiteSpace(personIdHigh))
        {
            throw new ArgumentException("must be a non-empty string", nameof(personIdLow));
        }
        if (string.CompareOrdinal(personIdLow, personIdHigh) >= 0)
        {
            throw new ArgumentException(
                "personIdLow must be strictly ordinal-less-than personIdHigh (an entry is never self-paired, and pair order must already be canonical - see OrderPair)",
                nameof(personIdLow));
        }

        TenantId = tenantId;
        PersonIdLow = personIdLow;
        PersonIdHigh = personIdHigh;
    }

    /// <summary>Canonicalizes an unordered pair of person ids into (low, high) ordinal order - the
    /// same order for {a,b} and {b,a}, so one entry ever exists per pair.</summary>
    public static (string Low, string High) OrderPair(string personIdA, string personIdB) =>
        string.CompareOrdinal(personIdA, personIdB) <= 0 ? (personIdA, personIdB) : (personIdB, personIdA);
}
