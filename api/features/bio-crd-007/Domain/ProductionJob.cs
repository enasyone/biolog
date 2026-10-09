using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Identifiers;

namespace Biolog.Features.CardProduction.Domain;

/// <summary>BIO-CRD-007-AC02's own three physical/data checks, matching the master specification's own "render, encode, print, read-back/QC and activation" stage list (render already happened at design time - BIO-CRD-002/003/004's own concern; activation is this story's own separately-gated action).</summary>
public sealed record ProductionStepResult
{
    public required bool Passed { get; init; }
    public string? Detail { get; init; }
    public required string RecordedAt { get; init; }
}

/// <summary>
/// One production attempt for a physical card issue (BIO-CRD-007), keyed by BIO-CRD-005's own
/// natural composite <c>(TenantId, PersonId, CardClass, SequenceNumber)</c> plus this project's
/// own <see cref="JobSequenceNumber"/> - a card issue may be produced more than once (a reprint
/// after a failed/rejected attempt), and AC04 explicitly requires a recorded reprint reason for
/// each one, so every attempt is its own durable row, never overwritten.
///
/// Mutable and CAS-versioned (ADR-0001 §3.2): a job genuinely changes state as its steps are
/// recorded and finally resolves to <see cref="ProductionJobStatus.Passed"/>/<see cref="ProductionJobStatus.Failed"/>/
/// <see cref="ProductionJobStatus.Rejected"/>/<see cref="ProductionJobStatus.Cancelled"/>.
///
/// <b>Construction is internal</b> (the same BIO-IDN-001-QA-01 lesson every mutable aggregate in
/// this codebase applies from the start).
/// </summary>
public sealed record ProductionJob
{
    public string TenantId { get; }
    public string PersonId { get; }
    public string CardClass { get; }
    public int SequenceNumber { get; }

    /// <summary>Starts at 1, never reused or reset - the same "governed deletion, no delete operation" principle every sequence in this codebase already follows.</summary>
    public int JobSequenceNumber { get; }

    public required ProductionJobStatus Status { get; init; }
    public required int Version { get; init; }

    public required string PrinterRef { get; init; }
    public required AuditActor Operator { get; init; }
    public required string WorkstationRef { get; init; }

    /// <summary>AC04: required when <see cref="JobSequenceNumber"/> &gt; 1 (a reprint), forbidden on the first attempt.</summary>
    public string? ReprintReason { get; init; }

    /// <summary>AC04: set only once <see cref="Status"/> becomes <see cref="ProductionJobStatus.Rejected"/>.</summary>
    public string? RejectionReason { get; init; }

    public ProductionStepResult? Print { get; init; }
    public ProductionStepResult? Encode { get; init; }
    public ProductionStepResult? ReadBackQc { get; init; }

    /// <summary>AC03: <c>"connected"</c> (recorded live, step by step) or <c>"offline"</c> (all three steps submitted together via a signed production package once connectivity resumes).</summary>
    public required string Source { get; init; }

    /// <summary>Set only when <see cref="Source"/> is <c>"offline"</c> - the package that authorised this job.</summary>
    public string? PackageId { get; init; }

    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    /// <summary>
    /// BIO-CRD-008-AC04: "Production references exact published version." Purely additive
    /// (nullable, no <c>required</c>) - every existing caller/test that does not set these two
    /// properties is completely unaffected, the same "additive, not a redesign" shape this
    /// story's own <c>ListForTenant</c> addition to <c>IProductionJobRepository</c> already
    /// established. BIO-CRD-007 itself never validates that <see cref="TemplateVersion"/> is
    /// actually the currently-published one for <see cref="TemplateId"/> - that enforcement
    /// lives at its one sanctioned source, BIO-CRD-008's own <c>CardTemplateService.GetPublished</c>
    /// (see docs/features/bio-crd-008/README.md "AC04" for why enforcement is not duplicated
    /// into this already-accepted story's own DI graph).
    /// </summary>
    public string? TemplateId { get; init; }
    public int? TemplateVersion { get; init; }

    internal ProductionJob(string tenantId, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (string.IsNullOrWhiteSpace(cardClass))
        {
            throw new ArgumentException("cardClass must not be blank", nameof(cardClass));
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "a card issue's sequence number starts at 1");
        }

        if (jobSequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jobSequenceNumber), jobSequenceNumber, "a production job's own sequence number starts at 1");
        }

        TenantId = tenantId;
        PersonId = personId;
        CardClass = cardClass;
        SequenceNumber = sequenceNumber;
        JobSequenceNumber = jobSequenceNumber;
    }
}
