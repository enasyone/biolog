using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Domain;
using Biolog.Features.FaceEnrolment.Liveness;
using Biolog.Features.FaceEnrolment.Quality;

namespace Biolog.Features.FaceEnrolment.Service;

/// <summary>
/// BIO-BIO-003: orchestrates a quality-gated face capture. Verifies AC03's source-photo linkage
/// up front (<see cref="IPhotoMasterExistenceGateway"/>), runs AC01/AC02's face-count and
/// seven-check quality assessment plus AC05's liveness hook, and only ever derives a template
/// (through BIO-BIO-001's <see cref="IFaceProvider"/>) and constructs a
/// <see cref="FaceBiometricReference"/> when BOTH gates pass - an unapproved capture never
/// reaches BIO-BIO-001's provider at all, and never produces a reference (AC04's guidance is
/// returned instead, see <see cref="FaceCaptureOutcome"/>).
///
/// <b>Internal, like BIO-BIO-002's <c>FingerprintEnrolmentService</c>.</b> This service depends
/// on the internal <see cref="IFaceBiometricReferenceRepository"/>, so it is internal too -
/// reachable only from this assembly, <c>Biolog.Api</c> and this feature's own test project.
///
/// <b>Tenant context.</b> Like BIO-BIO-001/002's services, this takes a caller-resolved tenant/
/// person id rather than resolving one itself; this project defines no tenant-context seam of
/// its own.
///
/// <b>Scope boundary: no audit trail.</b> Unlike BIO-BIO-002 (whose AC05 explicitly requires
/// "Enrolment is audited"), none of this story's five ACs mention auditing, so this service adds
/// none - see docs/features/bio-bio-003/README.md "Design decisions and scope boundaries" for the
/// explicit reasoning. A later story adding one does not need this service's shape to change.
/// </summary>
internal sealed class FaceEnrolmentService(
    IFaceBiometricReferenceRepository repository,
    IPhotoMasterExistenceGateway photoMasterGateway,
    IFaceProvider faceProvider,
    IFaceQualityChecker qualityChecker,
    ILivenessDetector livenessDetector,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    public async Task<FaceCaptureOutcome> CaptureAsync(
        string tenantId, string personId, int sourcePhotoMasterVersion, BiometricSample sample, CancellationToken cancellationToken)
    {
        if (!await photoMasterGateway.ExistsAsync(personId, sourcePhotoMasterVersion, cancellationToken).ConfigureAwait(false))
        {
            throw FaceEnrolmentException.SourcePhotoNotFound(personId, sourcePhotoMasterVersion);
        }

        var assessment = await qualityChecker.AssessAsync(sample, cancellationToken).ConfigureAwait(false);
        var liveness = await livenessDetector.DetectAsync(sample, cancellationToken).ConfigureAwait(false);

        if (!assessment.IsApproved || !liveness.IsLive)
        {
            var guidance = assessment.RecaptureGuidance.ToList();
            if (assessment.IsApproved && !liveness.IsLive)
            {
                guidance.Add("liveness_check_failed: recapture using a live, direct camera feed rather than a photo or screen.");
            }
            return FaceCaptureOutcome.Reject(guidance);
        }

        var now = _nowUtc();
        var features = await faceProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        var template = BiometricTemplate.Create(
            _newId(), tenantId, personId, BiometricModality.Face, faceProvider.ProviderId, features.ProviderVersion, features.Payload, now);

        var reference = new FaceBiometricReference(_newId(), tenantId, personId, sourcePhotoMasterVersion, template, liveness, now);
        await repository.SaveAsync(reference, cancellationToken).ConfigureAwait(false);

        return FaceCaptureOutcome.Approve(reference);
    }

    public Task<FaceBiometricReference?> GetLatestReferenceAsync(string tenantId, string personId, CancellationToken cancellationToken) =>
        repository.GetLatestForPersonAsync(tenantId, personId, cancellationToken);

    /// <summary>BIO-BIO-009-AC02/AC04: a plain persistence wrapper, matching
    /// <see cref="GetLatestReferenceAsync"/>'s own directness - no lock, no audit (this service
    /// has none of either today, a deliberate original scope boundary; the caller, BIO-BIO-009's
    /// own gateway, owns the full ADR-0003 Decision 7 discipline for this operation). Returns
    /// <c>null</c> when nothing was deleted; otherwise the deleted reference's own
    /// <see cref="FaceBiometricReference.PersonId"/> - resolved from the record itself (an extra
    /// read before the delete), so a caller can never substitute a different identity for
    /// cache-eviction purposes (BIO-BIO-009-AC04, ZA-BIO009-01).</summary>
    public async Task<string?> DeleteReferenceAsync(string tenantId, string referenceId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(tenantId, referenceId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var deleted = await repository.DeleteAsync(tenantId, referenceId, cancellationToken);
        return deleted ? existing.PersonId : null;
    }

    /// <summary>BIO-BIO-009-AC04/ZB-BIO009-06: resolves a reference's own PersonId WITHOUT
    /// deleting it, so BIO-BIO-009's own deletion service can durably record a gallery-cleanup
    /// obligation BEFORE calling <see cref="DeleteReferenceAsync"/> - a throw or a failed gallery
    /// eviction after the delete can then never lose the person id needed to retry. Plain, like
    /// <see cref="GetLatestReferenceAsync"/>'s own directness.</summary>
    public async Task<string?> ResolveOwnerAsync(string tenantId, string referenceId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(tenantId, referenceId, cancellationToken);
        return existing?.PersonId;
    }
}
