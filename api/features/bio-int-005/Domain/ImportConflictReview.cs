namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>
/// BIO-INT-005's own aggregate: one external import row's conflict, routed to human review
/// (Master Technical Specification: "conflicting external records routed to human review").
///
/// <b>No canonical id of its own kind</b> (same "natural key" shape BIO-TEN-002's
/// <c>TenantDomain</c>, BIO-IDN-002's <c>PersonProfile</c>, BIO-IDN-003's
/// <c>InstitutionalIdentifier</c> and BIO-IDN-005's <c>DuplicateCandidate</c> all use). The
/// natural key is <c>(TenantId, MappingId, ExternalKey)</c> - one review record per conflicted
/// row per mapping; a pair re-conflicting, or re-confirmed by a later import batch, augments the
/// SAME record rather than creating a second one.
///
/// <b>AC01 - conflict compares source versus BIOLOG values:</b> <see cref="Conflicts"/> carries
/// exactly the field-level comparison BIO-INT-002's own <c>ImportBatchService</c> already
/// computed (existing PERSON value vs. incoming source value) - this story never re-derives or
/// re-compares anything itself, only routes an already-detected disagreement to a human.
///
/// <b>AC02/AC03 - decisions are human-audited:</b> <see cref="DecidedAt"/>,
/// <see cref="DecidedByActorId"/>, <see cref="DecisionReasonCode"/> and
/// <see cref="DecisionNotes"/> are the decision's own trail on the record itself, in addition to
/// (not instead of) the append-only <see cref="Audit.ImportConflictReviewAuditEvent"/>
/// <see cref="ImportConflictReviewService.DecideAsync"/> writes for every decision.
///
/// <b>AC04 - no automatic destructive overwrite:</b> see <see cref="ImportConflictReviewStatus"/>'s header.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start, as every
/// story in this epic has since). <see cref="TenantId"/>, <see cref="MappingId"/> and
/// <see cref="ExternalKey"/> are the identity of this aggregate and are <c>get</c>-only -
/// immutable for the object's whole life, including across a <c>with</c> expression. The
/// constructor is <c>internal</c>; <c>InternalsVisibleTo</c> grants the same access to
/// <c>Biolog.Api</c> (DI registration) and this project's own test assembly - see
/// <see cref="IImportConflictReviewRepository"/> for why the repository is internal too and
/// independently re-validates at the persistence boundary.
/// </summary>
public sealed record ImportConflictReview
{
    public string TenantId { get; }
    public string MappingId { get; }
    public string ExternalKey { get; }
    public string? PersonId { get; init; }
    public required IReadOnlyList<ImportFieldConflict> Conflicts { get; init; }
    public required ImportConflictReviewStatus Status { get; init; }
    public required string SourceBatchId { get; init; }
    public required string RaisedAt { get; init; }
    public required string LastSeenAt { get; init; }
    public string? DecidedAt { get; init; }
    public string? DecidedByActorId { get; init; }
    public string? DecisionReasonCode { get; init; }
    public string? DecisionNotes { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal ImportConflictReview(string tenantId, string mappingId, string externalKey)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(mappingId))
        {
            throw new ArgumentException("mappingId must not be blank", nameof(mappingId));
        }

        if (string.IsNullOrWhiteSpace(externalKey))
        {
            throw new ArgumentException("externalKey must not be blank", nameof(externalKey));
        }

        TenantId = tenantId;
        MappingId = mappingId;
        ExternalKey = externalKey;
    }
}
