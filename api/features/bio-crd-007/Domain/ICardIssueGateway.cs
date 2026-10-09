namespace Biolog.Features.CardProduction.Domain;

/// <summary>Decoupled mirror of BIO-CRD-005's own <c>CardIssueStatus</c> - this project never references that story's assembly types directly (see this project's csproj remarks).</summary>
public enum CardIssueLifecycleStatus
{
    Issued,
    Active,
    Deactivated,
}

/// <summary>A read-only projection of a BIO-CRD-005 <c>CardIssue</c> - only the fields this feature's own business rules need.</summary>
public sealed record CardIssueSnapshot
{
    public required string PersonId { get; init; }
    public required string CardClass { get; init; }
    public required int SequenceNumber { get; init; }
    public required CardIssueLifecycleStatus Status { get; init; }
    public required int Version { get; init; }
}

/// <summary>
/// This feature's own narrow seam onto BIO-CRD-005's real, already-shipped card-issue
/// transaction - deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-crd-005</c>
/// (this project's csproj remarks explain why). The real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioCrd007Module</c> wrapping BIO-CRD-005's own already-registered
/// <c>CardIssueService</c> directly, the same shape every prior story's own gateway already
/// established. Deliberately narrower than BIO-CRD-006's own <c>ICardIssueGateway</c> - this
/// story only ever needs to check a card exists and, once production has passed, activate it;
/// it neither lists siblings nor issues replacements nor deactivates anything.
/// </summary>
public interface ICardIssueGateway
{
    Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>BIO-CRD-007-AC05: delegates to BIO-CRD-005's own real, authoritative compare-and-set, which already enforces "at most one Active card" - this story adds a precondition in front of it (AC02), never a competing implementation of the invariant itself.</summary>
    Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default);
}
