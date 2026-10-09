namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>Decoupled mirror of BIO-CRD-005's own <c>CardIssueStatus</c> - this project never references that story's assembly types directly (see this project's csproj remarks).</summary>
public enum CardIssueLifecycleStatus
{
    Issued,
    Active,
    Deactivated,
}

/// <summary>A read-only projection of a BIO-CRD-005 <c>CardIssue</c> - only the fields this feature's own business rules need, never that story's own domain type.</summary>
public sealed record CardIssueSnapshot
{
    public required string PersonId { get; init; }
    public required string CardClass { get; init; }
    public required int SequenceNumber { get; init; }
    public required CardIssueLifecycleStatus Status { get; init; }
    public required int Version { get; init; }
}

/// <summary>Request to issue a replacement physical card - the same provenance inputs BIO-CRD-005-AC05 requires, forwarded through unchanged.</summary>
public sealed record ReplacementCardRequest
{
    public required string TemplateRef { get; init; }
    public string? PhotoRef { get; init; }
    public string? LogoRef { get; init; }
    public string? SignatureRef { get; init; }
    public required string PrinterRef { get; init; }
}

/// <summary>
/// This feature's own narrow seam onto BIO-CRD-005's real, already-shipped card-issue
/// transaction - deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-crd-005</c>
/// (this project's csproj remarks explain why: no feature project references another). The
/// real implementation is an adapter in <c>Biolog.Api.Modules.BioCrd006Module</c> wrapping
/// BIO-CRD-005's own already-registered <c>CardIssueService</c> directly - the same shape
/// BIO-CRD-005's own <c>IPersonExistenceGateway</c> already established for an identical
/// dependency on BIO-IDN-001.
/// </summary>
public interface ICardIssueGateway
{
    Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>Ordered by sequence number ascending. Empty if this (person, card class) has no card issues.</summary>
    Task<IReadOnlyList<CardIssueSnapshot>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default);

    /// <summary>BIO-CRD-006-AC03: wraps BIO-CRD-005's own <c>IssueAsync</c>, whose own repository already guarantees the next sequence number - nothing extra to enforce here.</summary>
    Task<CardIssueSnapshot> IssueReplacementAsync(string personId, string cardClass, ReplacementCardRequest request, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default);

    /// <summary>BIO-CRD-005's own <c>ActivateAsync</c> enforces the expected_version precondition itself (428/409) - <paramref name="expectedVersion"/> is forwarded through unchanged, nullable, rather than duplicating that check on this side.</summary>
    Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default);

    /// <summary>Best-effort sync of BIO-CRD-005's own record during AC04's permanent-revocation sweep - see <see cref="CardLifecycle.CardLifecycleService"/>'s activate-replacement method for why a failure here does not block this story's own, authoritative <see cref="CardLockState.PermanentlyRevoked"/> transition.</summary>
    Task<CardIssueSnapshot> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default);
}
