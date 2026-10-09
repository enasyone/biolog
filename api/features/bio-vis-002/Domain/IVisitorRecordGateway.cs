namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// This feature's own narrow seam onto BIO-VIS-001's own visitor record - implemented in
/// <c>Biolog.Api.Modules.BioVis002Module</c> by wrapping that story's own already-registered
/// (internal, <c>InternalsVisibleTo Biolog.Api</c>) <c>VisitorRecordService</c> directly, the same
/// cross-story gateway pattern every sibling story uses. <see cref="CheckOutAsync"/> is what makes
/// this story's own orchestrating checkout (<c>VisitorCredentialService.CheckOutAsync</c>) a real
/// call-through to BIO-VIS-001's own domain logic, not a reimplementation of it.
/// </summary>
public interface IVisitorRecordGateway
{
    Task<VisitorRecordView?> GetAsync(string visitorId, CancellationToken cancellationToken = default);

    /// <summary><paramref name="actorTypeWire"/> is this feature's own <see cref="Audit.ActorType"/> rendered via <c>ToWireString()</c> - the adapter parses it back into BIO-VIS-001's own identically-shaped actor-type enum (both mirror the same ADR-0003 wire vocabulary).</summary>
    Task<VisitorRecordView> CheckOutAsync(string visitorId, int? expectedVersion, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default);
}

/// <summary>A read-only projection of BIO-VIS-001's own <c>VisitorRecord</c> - this feature's own local type, never that story's own (cross-project types never cross a gateway interface boundary).</summary>
public sealed record VisitorRecordView
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public string? PersonId { get; init; }
    public required string FullName { get; init; }
    public required string HostApprovalStatusWire { get; init; }
    public required string VisitStatusWire { get; init; }
    public string? CheckedInAt { get; init; }
    public string? CheckedOutAt { get; init; }
    public required string ExpiresAt { get; init; }
    public required string CreatedAt { get; init; }
    public required int Version { get; init; }
}
