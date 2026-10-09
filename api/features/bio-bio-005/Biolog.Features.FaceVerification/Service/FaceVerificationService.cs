using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceVerification.Domain;

namespace Biolog.Features.FaceVerification.Service;

/// <summary>
/// BIO-BIO-005: face 1:1 verification between a live/current capture and the claimed person's
/// approved reference. Reuses BIO-BIO-001's own <see cref="IFaceProvider"/>/<see cref="IBiometricMatcher"/>
/// seams and BIO-BIO-003's own <see cref="IFaceQualityChecker"/> directly, rather than
/// re-deriving equivalent contracts, and layers AC01's persisted evidence, AC02's policy-based
/// threshold and AC03's evidence/decision split on top.
///
/// <b>AC01 - "similarity, threshold policy, quality and model version are recorded".</b> Every
/// call to <see cref="CompareAsync"/> writes exactly one <see cref="FaceVerificationEvidence"/>
/// via <see cref="IFaceVerificationEvidenceStore"/>, regardless of outcome - see that type's
/// remarks for exactly which fields are populated when.
///
/// <b>AC02 - "threshold is policy-based rather than arbitrary UI input".</b> This method's own
/// signature has no threshold parameter; the threshold used is always
/// <see cref="IFaceVerificationThresholdPolicy.GetThreshold"/>, resolved by tenant only.
///
/// <b>AC03 - "verification evidence is separate from authorization decision".</b>
/// <see cref="CompareAsync"/> produces and persists ONLY <see cref="FaceVerificationEvidence"/> -
/// it never computes or returns an allow/deny verdict. <see cref="FaceAuthorizationPolicy.Decide"/>
/// is the separate, pure step a caller invokes on that evidence to reach one.
///
/// <b>AC04 - "reference/photo lifecycle changes are handled deterministically".</b>
/// <see cref="IFaceReferenceGateway.GetLatestApprovedReferenceAsync"/> is resolved fresh on every
/// call (never cached across calls by this service), so a re-capture that supersedes the
/// person's approved reference is picked up automatically on the very next verification attempt.
/// An identity with no approved reference at all is a genuine precondition failure
/// (<see cref="FaceVerificationException.NoAuthorisedReference"/>), not a routine outcome.
///
/// <b>BIO-BIO-005-QA-01 (Chioma acceptance, High).</b> A resolved reference can still be STALE:
/// its own <see cref="FaceReferenceSnapshot.SourcePhotoMasterVersion"/> can lag behind the
/// claimed identity's CURRENT photo master version, because BIO-PHO-001 allows a new photo to be
/// appended without forcing a face re-capture. This service compares
/// <see cref="IFaceReferenceGateway.GetCurrentPhotoMasterVersionAsync"/>'s result against the
/// resolved reference's own version on every call, BEFORE attempting extraction/comparison -
/// a mismatch (or no current photo master at all) records
/// <see cref="FaceComparisonOutcome.ReferenceStale"/> and skips comparison entirely, since a
/// match against a superseded photo would be meaningless. This is the defined, deterministic
/// reject outcome an acceptance probe found missing from the original implementation.
///
/// <b>Provider/vendor extraction failures are contained (BIO-BIO-004-F01's lesson, applied from
/// the start).</b> <see cref="IFaceProvider"/> is an external adapter seam - a vendor SDK
/// implements it directly - so <see cref="IFaceProvider.ExtractTemplateAsync"/> can fail with
/// anything from <see cref="ArgumentException"/> to a device timeout or a vendor-specific
/// exception. This service catches all of it and records
/// <see cref="FaceComparisonOutcome.AcquisitionFailed"/>, except a genuine CALLER cancellation
/// (the passed <see cref="CancellationToken"/> itself was cancelled), which propagates instead of
/// being recorded, since nothing about that attempt was ever decided.
///
/// <b>Internal, like every prior BIO-BIO story's own service.</b> This service depends on the
/// internal <see cref="IFaceVerificationEvidenceStore"/>, so it is internal too - reachable only
/// from this assembly, <c>Biolog.Api</c> and this feature's own test project.
///
/// <b>Tenant context.</b> Like every prior BIO-BIO story's service, this takes a caller-resolved
/// tenant/person id rather than resolving one itself; this project defines no tenant-context seam
/// of its own.
/// </summary>
internal sealed class FaceVerificationService(
    IFaceReferenceGateway referenceGateway,
    IFaceProvider faceProvider,
    IBiometricMatcher matcher,
    IFaceQualityChecker qualityChecker,
    IFaceVerificationThresholdPolicy thresholdPolicy,
    IFaceVerificationEvidenceStore evidenceStore,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    public async Task<FaceVerificationEvidence> CompareAsync(
        string tenantId, string personId, BiometricSample sample, CancellationToken cancellationToken)
    {
        var reference = await referenceGateway.GetLatestApprovedReferenceAsync(tenantId, personId, cancellationToken).ConfigureAwait(false);
        if (reference is null)
        {
            throw FaceVerificationException.NoAuthorisedReference(personId);
        }

        var now = _nowUtc();
        var evidenceId = _newId();
        var threshold = thresholdPolicy.GetThreshold(tenantId);

        var assessment = await qualityChecker.AssessAsync(sample, cancellationToken).ConfigureAwait(false);

        var currentPhotoMasterVersion = await referenceGateway
            .GetCurrentPhotoMasterVersionAsync(tenantId, personId, cancellationToken).ConfigureAwait(false);

        if (currentPhotoMasterVersion != reference.SourcePhotoMasterVersion)
        {
            var stale = new FaceVerificationEvidence
            {
                Id = evidenceId,
                TenantId = tenantId,
                PersonId = personId,
                ReferenceId = reference.ReferenceId,
                SourcePhotoMasterVersion = reference.SourcePhotoMasterVersion,
                CurrentPhotoMasterVersion = currentPhotoMasterVersion,
                ComparisonOutcome = FaceComparisonOutcome.ReferenceStale,
                ThresholdUsed = threshold,
                ProviderId = reference.Template.ProviderId,
                ProviderVersion = reference.Template.ProviderVersion,
                QualityFaceCount = assessment.FaceCount,
                QualityApproved = assessment.IsApproved,
                OccurredAtUtc = now,
            };
            await evidenceStore.SaveAsync(stale, CancellationToken.None).ConfigureAwait(false);
            return stale;
        }

        ExtractedBiometricFeatures features;
        try
        {
            features = await faceProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation, not a provider/vendor failure - nothing about this
            // attempt was ever decided, so nothing is recorded; propagate exactly as any other
            // cancellable async call would.
            throw;
        }
        catch (Exception)
        {
            var failed = new FaceVerificationEvidence
            {
                Id = evidenceId,
                TenantId = tenantId,
                PersonId = personId,
                ReferenceId = reference.ReferenceId,
                SourcePhotoMasterVersion = reference.SourcePhotoMasterVersion,
                CurrentPhotoMasterVersion = currentPhotoMasterVersion,
                ComparisonOutcome = FaceComparisonOutcome.AcquisitionFailed,
                ThresholdUsed = threshold,
                ProviderId = reference.Template.ProviderId,
                ProviderVersion = reference.Template.ProviderVersion,
                QualityFaceCount = assessment.FaceCount,
                QualityApproved = assessment.IsApproved,
                OccurredAtUtc = now,
            };
            await evidenceStore.SaveAsync(failed, CancellationToken.None).ConfigureAwait(false);
            return failed;
        }

        var probe = BiometricTemplate.Create(
            _newId(), tenantId, personId, BiometricModality.Face, faceProvider.ProviderId, features.ProviderVersion, features.Payload, now);
        var matchResult = await matcher.CompareAsync(probe, reference.Template, cancellationToken).ConfigureAwait(false);

        var comparisonOutcome = matchResult.Score >= threshold ? FaceComparisonOutcome.Matched : FaceComparisonOutcome.NotMatched;

        var evidence = new FaceVerificationEvidence
        {
            Id = evidenceId,
            TenantId = tenantId,
            PersonId = personId,
            ReferenceId = reference.ReferenceId,
            SourcePhotoMasterVersion = reference.SourcePhotoMasterVersion,
            CurrentPhotoMasterVersion = currentPhotoMasterVersion,
            ComparisonOutcome = comparisonOutcome,
            Score = matchResult.Score,
            ThresholdUsed = threshold,
            MatcherId = matchResult.MatcherId,
            ProviderId = reference.Template.ProviderId,
            ProviderVersion = reference.Template.ProviderVersion,
            QualityFaceCount = assessment.FaceCount,
            QualityApproved = assessment.IsApproved,
            OccurredAtUtc = now,
        };

        await evidenceStore.SaveAsync(evidence, CancellationToken.None).ConfigureAwait(false);
        return evidence;
    }

    public Task<FaceVerificationEvidence?> GetEvidenceAsync(string tenantId, string evidenceId, CancellationToken cancellationToken) =>
        evidenceStore.GetAsync(tenantId, evidenceId, cancellationToken);

    public Task<IReadOnlyList<FaceVerificationEvidence>> GetHistoryAsync(
        string tenantId, string personId, CancellationToken cancellationToken) =>
        evidenceStore.GetForPersonAsync(tenantId, personId, cancellationToken);
}
