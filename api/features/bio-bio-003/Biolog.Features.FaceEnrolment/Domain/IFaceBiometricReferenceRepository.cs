namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>Persistence seam for the approved, immutable <see cref="FaceBiometricReference"/>
/// (AC03). This story ships only <see cref="InMemoryFaceBiometricReferenceRepository"/>; a later
/// story can implement this against a real datastore without
/// <see cref="Service.FaceEnrolmentService"/> changing shape. Internal - reachable only from this
/// assembly, <c>Biolog.Api</c> and this feature's own test project (BIO-IDN-001-QA-01's
/// governance shape).</summary>
internal interface IFaceBiometricReferenceRepository
{
    Task SaveAsync(FaceBiometricReference reference, CancellationToken cancellationToken);

    Task<FaceBiometricReference?> GetAsync(string tenantId, string referenceId, CancellationToken cancellationToken);

    /// <summary>The most recently approved reference for this person, or null if none has ever
    /// been approved. Never returns a reference belonging to another tenant.</summary>
    Task<FaceBiometricReference?> GetLatestForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken);

    /// <summary>BIO-BIO-009-AC02/AC04: removes a reference outright. Returns <c>true</c> only
    /// when a reference genuinely existed for this tenant and was removed; <c>false</c> for "no
    /// such reference" (never an exception, same discipline as <see cref="GetAsync"/>). A plain
    /// persistence operation - no lock, no audit - matching every other method on this interface;
    /// the caller (BIO-BIO-009's own gateway) owns any lock/audit discipline it needs.</summary>
    Task<bool> DeleteAsync(string tenantId, string referenceId, CancellationToken cancellationToken);
}
