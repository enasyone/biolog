using Biolog.Features.Biometrics.Domain;
using Biolog.Features.FaceEnrolment.Liveness;

namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>
/// BIO-BIO-003-AC03: "Approved reference retains model/version and source-photo linkage." The
/// APPROVED output of a passing face capture - never constructed for a rejected one (see
/// <see cref="Service.FaceEnrolmentService.CaptureAsync"/>).
///
/// <b>Model/version.</b> Carried entirely by <see cref="Template"/> - BIO-BIO-001's own
/// <see cref="BiometricTemplate.ProviderId"/>/<see cref="BiometricTemplate.ProviderVersion"/> -
/// reused directly rather than re-derived, the same "build on BIO-BIO-001's canonical shape"
/// choice BIO-BIO-002 made.
///
/// <b>Source-photo linkage.</b> <see cref="SourcePhotoMasterVersion"/> is the specific
/// BIO-PHO-001 <c>PhotoMaster</c> version this reference was derived from - a plain int, the
/// same natural-key shape <c>PhotoMaster</c> itself uses (<c>(PersonId, Version)</c>, no
/// dedicated identifier - see that type's own remarks). <see cref="PersonId"/> is this
/// reference's own copy of the person, so the full source-photo key is
/// <c>(PersonId, SourcePhotoMasterVersion)</c>.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01/BIO-PHO-001-QA-01's lesson, applied from
/// the start): every property is <c>get</c>-only with no <c>init</c> accessor, and the
/// constructor is restricted to this assembly, <c>Biolog.Api</c> and this feature's own test
/// project via <c>InternalsVisibleTo</c>. No <c>with</c> expression against this type compiles
/// at all.
/// </summary>
public sealed class FaceBiometricReference
{
    public string Id { get; }
    public string TenantId { get; }
    public string PersonId { get; }
    public int SourcePhotoMasterVersion { get; }
    public BiometricTemplate Template { get; }
    public LivenessResult Liveness { get; }
    public string CreatedAtUtc { get; }

    internal FaceBiometricReference(
        string id, string tenantId, string personId, int sourcePhotoMasterVersion, BiometricTemplate template,
        LivenessResult liveness, string createdAtUtc)
    {
        RequireNonEmpty(id, nameof(id));
        RequireNonEmpty(tenantId, nameof(tenantId));
        RequireNonEmpty(personId, nameof(personId));
        RequireNonEmpty(createdAtUtc, nameof(createdAtUtc));
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(liveness);

        Id = id;
        TenantId = tenantId;
        PersonId = personId;
        SourcePhotoMasterVersion = sourcePhotoMasterVersion;
        Template = template;
        Liveness = liveness;
        CreatedAtUtc = createdAtUtc;
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("must be a non-empty string", paramName);
        }
    }
}
