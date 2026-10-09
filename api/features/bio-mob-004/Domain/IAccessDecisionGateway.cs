namespace Biolog.Features.MobileVerification.Domain;

/// <summary>What a mobile officer presents: a card/NFC/QR-read person id plus, optionally, a captured biometric sample. Mirrors BIO-ACC-002's own <c>DecideAccessRequest</c> shape at the field level (this feature's own copy, not a shared type - "each feature owns its own small primitives").</summary>
public sealed record VerificationDecisionQuery
{
    public required string PersonId { get; init; }
    public string? ProfileKey { get; init; }
    public required string CredentialClass { get; init; }
    public required string LocationId { get; init; }
    public string? OccurredAt { get; init; }
    public required string PresentedCardStatus { get; init; }
    public required string PolicyKey { get; init; }
    public BiometricSampleInput? FingerprintSample { get; init; }
    public BiometricSampleInput? FaceSample { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record BiometricSampleInput
{
    public required byte[] Payload { get; init; }
    public required string ContentType { get; init; }
    public required string CapturedAtUtc { get; init; }
}

/// <summary>
/// BIO-MOB-004-AC01, enforced by the SHAPE of this type, not merely by which fields a UI chooses
/// to render: deliberately carries no per-modality raw match score or comparison outcome (BIO-
/// ACC-002's own <c>AccessDecisionResponse.BiometricEvidence[].Score</c> is exactly the
/// "unnecessary biometric internal" this AC forbids exposing to a mobile officer). Only the
/// decision itself, its reason, and enough identity/audit context to display and correlate it.
/// </summary>
public sealed record VerificationDecisionResult
{
    public required string EventId { get; init; }
    public required string PersonId { get; init; }
    public required string Decision { get; init; }
    public required string ReasonCode { get; init; }
    public required string PresentedAssuranceLevel { get; init; }
    public required string OccurredAt { get; init; }
    public required string RecordedAt { get; init; }
    public required string CorrelationId { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// This feature's own narrow seam onto BIO-ACC-002's decision engine - mirrors every sibling
/// gateway's own shape ("each feature owns its own small primitives"). The real implementation
/// (an adapter in <c>Biolog.Api.Modules.BioMob004Module</c>) manually constructs BIO-ACC-002's
/// own internal <c>AccessDecisionService</c>, substituting a device-resolved tenant provider for
/// its own <c>ICurrentTenantProvider</c> (that service's container-level registration is wired
/// only to the host/principal channel, which a device-authenticated mobile request never has) -
/// the SAME manual-factory-lambda discipline (never the reflection-based DI activator) already
/// established for consuming <c>ICrossTenantDeviceLookup</c>. See <c>BioMob004Module.cs</c>'s own
/// header for the full reasoning. Deliberately NOT a <c>ProjectReference</c> to
/// <c>api/features/bio-acc-002</c>.
/// </summary>
public interface IAccessDecisionGateway
{
    Task<VerificationDecisionResult> DecideAsync(string tenantId, VerificationDecisionQuery query, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VerificationDecisionResult>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);
}
