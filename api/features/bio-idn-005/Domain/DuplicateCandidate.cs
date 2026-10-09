namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// BIO-IDN-005's own aggregate: a suspected duplicate PAIRING between two PERSON (BIO-IDN-001)
/// records in the same tenant, routed to human review (Master Technical Specification §4:
/// "Suspected duplicates go to human review; no silent merge.").
///
/// <b>No canonical id of its own kind</b> (same "hostname as natural key" shape BIO-TEN-002's
/// <c>TenantDomain</c>, BIO-IDN-002's <c>PersonProfile</c> and BIO-IDN-003's
/// <c>InstitutionalIdentifier</c> all use). The natural key is
/// <c>(TenantId, PersonIdLow, PersonIdHigh)</c> - the two person ids ordered by
/// <see cref="string.CompareOrdinal(string, string)"/> so the unordered pair
/// <c>{personA, personB}</c> always resolves to exactly one record regardless of which order a
/// caller names them in - see <see cref="OrderPair"/>.
///
/// <b>AC01 - signals accumulate, never overwrite:</b> <see cref="SignalSources"/> is a set, not a
/// single value - a pair first raised from a name/date-of-birth match can later also match on an
/// institutional identifier value (or, eventually, a biometric signal - see
/// <see cref="DuplicateSignalSource"/>'s header) without losing the earlier evidence.
///
/// <b>AC02 - review decisions are human-audited:</b> <see cref="DecidedAt"/>,
/// <see cref="DecidedByActorId"/>, <see cref="DecisionReasonCode"/> and
/// <see cref="DecisionNotes"/> are the decision's own trail on the record itself, in addition to
/// (not instead of) the append-only <see cref="Audit.DuplicateCandidateAuditEvent"/>
/// <see cref="DuplicateReviewService.DecideAsync"/> writes for every decision.
///
/// <b>AC03 - merge is never automatic:</b> see <see cref="DuplicateReviewStatus"/>'s header.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start, as every
/// story in this epic has since). <see cref="TenantId"/>, <see cref="PersonIdLow"/> and
/// <see cref="PersonIdHigh"/> are the identity of this aggregate and are <c>get</c>-only -
/// immutable for the object's whole life, including across a <c>with</c> expression. The
/// constructor is <c>internal</c>; <c>InternalsVisibleTo</c> grants the same access to
/// <c>Biolog.Api</c> (DI registration) and this project's own test assembly - see
/// <see cref="IDuplicateCandidateRepository"/> for why the repository is internal too and
/// independently re-validates at the persistence boundary.
/// </summary>
public sealed record DuplicateCandidate
{
    public string TenantId { get; }
    public string PersonIdLow { get; }
    public string PersonIdHigh { get; }
    public required IReadOnlySet<DuplicateSignalSource> SignalSources { get; init; }
    public required DuplicateReviewStatus Status { get; init; }
    public string? DecidedAt { get; init; }
    public string? DecidedByActorId { get; init; }
    public string? DecisionReasonCode { get; init; }
    public string? DecisionNotes { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal DuplicateCandidate(string tenantId, string personIdLow, string personIdHigh)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!Identifiers.CanonicalId.IsPersonId(personIdLow))
        {
            throw new ArgumentException($"'{personIdLow}' is not a well-formed person id (ADR-0001)", nameof(personIdLow));
        }

        if (!Identifiers.CanonicalId.IsPersonId(personIdHigh))
        {
            throw new ArgumentException($"'{personIdHigh}' is not a well-formed person id (ADR-0001)", nameof(personIdHigh));
        }

        if (string.CompareOrdinal(personIdLow, personIdHigh) >= 0)
        {
            throw new ArgumentException(
                "personIdLow must be strictly ordinal-less-than personIdHigh (a candidate is never self-paired, and pair order must already be canonical - see OrderPair)",
                nameof(personIdLow));
        }

        TenantId = tenantId;
        PersonIdLow = personIdLow;
        PersonIdHigh = personIdHigh;
    }

    /// <summary>Canonicalizes an unordered pair of person ids into (low, high) ordinal order - the same order for {a,b} and {b,a}, so one candidate record ever exists per pair.</summary>
    public static (string Low, string High) OrderPair(string personIdA, string personIdB) =>
        string.CompareOrdinal(personIdA, personIdB) <= 0 ? (personIdA, personIdB) : (personIdB, personIdA);
}
