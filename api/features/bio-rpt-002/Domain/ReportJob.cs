using Biolog.Features.ReportBuilder.Audit;

namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>
/// One report request and its lifecycle (BIO-RPT-002-AC04). Mutable and CAS-versioned
/// (ADR-0001 §3.2), the same shape BIO-CRD-007's own <c>ProductionJob</c> establishes for a
/// mutable, evolving aggregate. Unlike a production job, a report job has no pre-existing
/// natural key to collide against (it is minted fresh per request, never looked up by caller-
/// supplied composite fields), so construction stays a plain object initializer inside
/// <see cref="ReportJobRunner"/>/<see cref="IReportJobRepository"/> rather than a validating
/// factory method.
/// </summary>
public sealed record ReportJob
{
    public required string JobId { get; init; }
    public required string TenantId { get; init; }
    public required ReportDefinition Definition { get; init; }
    public required ReportJobStatus Status { get; init; }
    public required int Version { get; init; }
    public required AuditActor RequestedBy { get; init; }

    public ReportResult? Result { get; init; }
    public string? ErrorCode { get; init; }

    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public string? StartedAt { get; init; }
    public string? CompletedAt { get; init; }
}
