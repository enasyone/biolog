namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// This feature's own narrow seam onto BIO-CRD-005's own physical card issue transaction -
/// implemented in <c>Biolog.Api.Modules.BioVis002Module</c> by wrapping that story's own
/// already-registered (internal) <c>CardIssueService</c> directly. <see cref="IssueAsync"/>
/// REQUIRES a real, existing <c>personId</c> (BIO-CRD-005's own rule, not relaxed here) - see
/// <c>VisitorCredentialService.GrantAsync</c>'s own header for the disclosed consequence this
/// has for a visitor with no linked PERSON.
/// </summary>
public interface ICardIssuanceGateway
{
    Task<CardIssueView?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// BIO-VIS-002-F01's own remediation: wraps BIO-CRD-005's own already-existing
    /// <c>CardIssueService.ListAsync</c> (no new method needed on that side) - this feature's
    /// pre-issue duplicate check consults BIO-CRD-005's OWN state for this (person, card class)
    /// before calling <see cref="IssueAsync"/>, instead of relying solely on this feature's own
    /// local credential row (exactly the row that goes missing in the failure this remediates).
    /// Ordered by sequence number ascending; empty if none exist.
    /// </summary>
    Task<IReadOnlyList<CardIssueView>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default);

    /// <summary><paramref name="actorTypeWire"/> is this feature's own <see cref="Audit.ActorType"/> rendered via <c>ToWireString()</c> - the adapter parses it back into BIO-CRD-005's own identically-shaped actor-type enum.</summary>
    Task<CardIssueView> IssueAsync(string personId, string cardClass, string templateRef, string printerRef, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default);

    Task<CardIssueView> ActivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, string idempotencyKey, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default);

    Task<CardIssueView> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default);
}

/// <summary>A read-only projection of BIO-CRD-005's own <c>CardIssue</c> - this feature's own local type.</summary>
public sealed record CardIssueView
{
    public required string PersonId { get; init; }
    public required string CardClass { get; init; }
    public required int SequenceNumber { get; init; }
    public required string StatusWire { get; init; }
    public required int Version { get; init; }
}
