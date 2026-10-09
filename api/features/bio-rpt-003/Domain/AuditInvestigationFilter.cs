using Biolog.Features.AuditInvestigation.Audit;

namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>BIO-RPT-003-AC01: "Filters include actor/person/action/time/device/reason." Every field is optional; an absent field matches everything.</summary>
public sealed record AuditInvestigationFilter
{
    public ActorType? ActorType { get; init; }
    public string? ActorId { get; init; }
    public string? PersonId { get; init; }
    public string? Action { get; init; }
    public string? DeviceId { get; init; }
    public string? ReasonCode { get; init; }

    /// <summary>Inclusive lower bound on <c>occurred_at</c>, an RFC 3339 UTC instant string (ADR-0001 §2.4: safe to compare ordinally at fixed width).</summary>
    public string? OccurredFrom { get; init; }

    /// <summary>Inclusive upper bound on <c>occurred_at</c>.</summary>
    public string? OccurredTo { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
