namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>A raw biometric capture, shaped locally rather than referencing BIO-BIO-001's own public <c>BiometricSample</c> type directly - BIO-BIO-001 is not a declared dependency of this story, only BIO-BIO-004/BIO-BIO-005 are; the module adapter converts this into whichever sibling type it actually needs.</summary>
public sealed record BiometricSampleInput
{
    public required byte[] Payload { get; init; }
    public required string ContentType { get; init; }
    public required string CapturedAtUtc { get; init; }
}

/// <summary>
/// Cross-story seam onto BIO-BIO-004's own already-registered <c>FingerprintVerificationService.VerifyAsync</c>.
/// Never throws for "no reference enrolled" - the adapter catches BIO-BIO-004's own
/// <c>FingerprintVerificationException</c> and reports <see cref="BiometricComparisonOutcome.NotEnrolled"/>
/// instead (see that outcome's own header), so this feature's orchestration never has to special-case it.
/// </summary>
public interface IFingerprintEvidenceGateway
{
    Task<BiometricEvidenceEntry> VerifyAsync(string personId, BiometricSampleInput sample, CancellationToken cancellationToken = default);
}
