namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>AC02's persistence seam: every <see cref="FingerprintVerificationRecord"/> this
/// feature produces is written here, never mutated afterwards. This story ships only
/// <see cref="InMemoryFingerprintVerificationRecordStore"/> (mirrors BIO-BIO-001-AC05's "no real
/// biometric data required locally" scope); a later story can implement this against a real
/// datastore without <see cref="Service.FingerprintVerificationService"/> changing shape.
/// Internal - reachable only from this assembly, <c>Biolog.Api</c> (DI registration) and this
/// feature's own test project (BIO-IDN-001-QA-01's governance shape).</summary>
internal interface IFingerprintVerificationRecordStore
{
    Task SaveAsync(FingerprintVerificationRecord record, CancellationToken cancellationToken);

    Task<FingerprintVerificationRecord?> GetAsync(string tenantId, string recordId, CancellationToken cancellationToken);

    /// <summary>Every record for one claimed identity, newest first. Empty (never null) when
    /// nothing has been recorded.</summary>
    Task<IReadOnlyList<FingerprintVerificationRecord>> GetForPersonAsync(
        string tenantId, string personId, CancellationToken cancellationToken);
}
