namespace Biolog.Features.FaceVerification.Domain;

/// <summary>AC01's persistence seam: every <see cref="FaceVerificationEvidence"/> this feature
/// produces is written here, never mutated afterwards - mirrors BIO-BIO-004's
/// <c>IFingerprintVerificationRecordStore</c>. This story ships only
/// <see cref="InMemoryFaceVerificationEvidenceStore"/>; a later story can implement this against
/// a real datastore without <see cref="Service.FaceVerificationService"/> changing shape.
/// Internal - reachable only from this assembly, <c>Biolog.Api</c> (DI registration) and this
/// feature's own test project (BIO-IDN-001-QA-01's governance shape).</summary>
internal interface IFaceVerificationEvidenceStore
{
    Task SaveAsync(FaceVerificationEvidence evidence, CancellationToken cancellationToken);

    Task<FaceVerificationEvidence?> GetAsync(string tenantId, string evidenceId, CancellationToken cancellationToken);

    /// <summary>Every evidence record for one claimed identity, newest first. Empty (never null)
    /// when nothing has been recorded.</summary>
    Task<IReadOnlyList<FaceVerificationEvidence>> GetForPersonAsync(
        string tenantId, string personId, CancellationToken cancellationToken);
}
