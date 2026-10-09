using Biolog.Features.Transport.Identifiers;

namespace Biolog.Features.Transport.Domain;

/// <summary>
/// AC02 - a person's assignment to a route/stop, with a validity window
/// (<see cref="ValidFrom"/>/<see cref="ValidUntil"/>, nullable = open-ended). A mutable,
/// CAS-versioned aggregate, same shape as every other aggregate in this feature.
///
/// <b>The overlap invariant (AC02) lives in <see cref="PersonAssignmentService"/>, not here</b> -
/// it is a tenant/person-scoped invariant across MULTIPLE records, not a single record's own
/// state machine, so it cannot be checked by this type alone (the same reason BIO-IDN-003's
/// identifier-uniqueness rule lived in its service, not its aggregate).
///
/// <b>Effective status is never stored</b> - see <see cref="TransportStatusEvaluator"/> and
/// <see cref="EffectiveAssignmentStatus"/>'s own header (AC02, the same "refuse auto-flip, compute
/// in effect at read time" discipline this whole codebase applies to time-based status).
///
/// <b>Only one real mutation after creation: ending the assignment early</b> (setting
/// <see cref="ValidUntil"/> on an open-ended or future-dated window) - this feature does not
/// support editing an assignment's route/stop/person in place; ending one and creating a new one
/// is the sanctioned path (keeps the audit trail unambiguous about WHICH window applied when).
/// </summary>
public sealed record PersonAssignment
{
    public string Id { get; }
    public string TenantId { get; }

    public required string PersonId { get; init; }
    public required string RouteId { get; init; }
    public required string StopId { get; init; }
    public required string ValidFrom { get; init; }
    public string? ValidUntil { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string CreatedBy { get; init; }

    internal PersonAssignment(string id, string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }

    internal static PersonAssignment Create(
        string tenantId, string personId, string routeId, string stopId, string validFrom, string? validUntil, string actorId, string now) =>
        new(CanonicalId.NewLocalId(), tenantId)
        {
            PersonId = personId,
            RouteId = routeId,
            StopId = stopId,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            Version = 1,
            CreatedAt = now,
            CreatedBy = actorId,
        };

    /// <summary>Ends an open-ended (or future-dated) assignment as of <paramref name="endAt"/> - the one authorization-gated mutation this aggregate supports (see <c>PersonAssignmentService.EndAsync</c>).</summary>
    internal PersonAssignment End(string endAt) => this with { ValidUntil = endAt, Version = Version + 1 };
}
