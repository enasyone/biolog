using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FingerprintVerification.Domain;

namespace Biolog.Features.FingerprintVerification.Service;

/// <summary>
/// BIO-BIO-004: 1:1 fingerprint verification against a claimed identity. Reuses BIO-BIO-001's
/// own <see cref="IBiometricTemplateStore"/>/<see cref="IFingerprintProvider"/>/
/// <see cref="IBiometricMatcher"/> seams directly - the same seams
/// <c>Biolog.Features.Biometrics.Service.BiometricService.VerifyAsync</c> already established
/// for 1:1 verification (BIO-BIO-001-AC03) - rather than re-deriving an equivalent comparison
/// pipeline, and layers AC02's persisted decision record, AC03's acquisition-failure/no-match
/// split and AC04's identity-status gate on top.
///
/// <b>AC01 - "claimed identity resolves one authorised reference set".</b>
/// <see cref="IBiometricTemplateStore.GetTemplatesAsync"/> returns every fingerprint template
/// ever enrolled for the claimed (tenantId, personId), newest first; this service always
/// compares against exactly the newest one - the SAME "one authorised reference" resolution
/// <c>BiometricService.VerifyAsync</c> already uses (never a 1:N search across every enrolled
/// template). An empty result is a genuine precondition failure
/// (<see cref="FingerprintVerificationException.NoAuthorisedReference"/>), not a routine
/// verification outcome - see that exception's remarks for why this differs from AC03's two
/// outcomes.
///
/// <b>AC02 - "provider score/decision/model version are recorded".</b> Every call writes exactly
/// one <see cref="FingerprintVerificationRecord"/> via <see cref="IFingerprintVerificationRecordStore"/>,
/// regardless of outcome - see that type's remarks for exactly which fields are populated when.
///
/// <b>AC03 - "no-match and acquisition failure are distinct outcomes".</b>
/// <see cref="IFingerprintProvider"/> is an external adapter seam - a vendor SDK implements it
/// directly - so <see cref="IFingerprintProvider.ExtractTemplateAsync"/> can fail with anything
/// from <see cref="ArgumentException"/> (an empty/unusable live sample, see
/// <c>SyntheticFingerprintProvider</c>'s own remarks) to a device timeout, a vendor-specific
/// exception, or an internal provider-side cancellation unrelated to this call's own
/// <see cref="CancellationToken"/>. This service contains ALL of it (BIO-BIO-004-F01, Seyi round
/// 1: an earlier version caught only <see cref="ArgumentException"/>, letting any other provider
/// failure escape uncaught with no record written) and records
/// <see cref="BiometricComparisonOutcome.AcquisitionFailed"/> rather than letting it propagate -
/// except a genuine CALLER cancellation (the passed <see cref="CancellationToken"/> itself was
/// cancelled), which propagates exactly as any other cancellable async call would, since nothing
/// about that attempt was ever decided. A live sample that DOES extract but does not match the
/// reference records <see cref="BiometricComparisonOutcome.NotMatched"/> instead. Both
/// AcquisitionFailed and NotMatched are routine, expected outcomes an operator handles - never a
/// caller/integration error the way AC01's missing-reference precondition is (the same
/// typed-outcome-vs-exception split BIO-BIO-003 established for its own quality/liveness gate).
///
/// <b>AC04 - "suspended/revoked identities cannot be converted into access allow by match
/// alone".</b> <see cref="IIdentityStatusGateway"/> is consulted ONLY when the biometric
/// comparison itself already matched (an identity that never matched can never reach
/// <see cref="AccessDecision.Allow"/> regardless of its status, so there is nothing to gate); a
/// <see cref="IdentityStatus.Suspended"/>/<see cref="IdentityStatus.Revoked"/> status downgrades
/// what would otherwise be an allow into a deny with an explicit reason, while the underlying
/// <see cref="BiometricComparisonOutcome.Matched"/> fact is still recorded (AC02) - a security
/// reviewer can see "this suspended identity's fingerprint matched" without that fact ever having
/// been capable of granting access.
///
/// <b>Internal, like BIO-BIO-002/003's own services.</b> This service depends on the internal
/// <see cref="IFingerprintVerificationRecordStore"/>, so it is internal too - reachable only from
/// this assembly, <c>Biolog.Api</c> and this feature's own test project.
///
/// <b>Tenant context.</b> Like every prior BIO-BIO story's service, this takes a caller-resolved
/// tenant/person id rather than resolving one itself; this project defines no tenant-context seam
/// of its own.
/// </summary>
internal sealed class FingerprintVerificationService(
    IBiometricTemplateStore templateStore,
    IFingerprintProvider fingerprintProvider,
    IBiometricMatcher matcher,
    IIdentityStatusGateway identityStatusGateway,
    IFingerprintVerificationRecordStore recordStore,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    public async Task<FingerprintVerificationRecord> VerifyAsync(
        string tenantId, string personId, BiometricSample sample, CancellationToken cancellationToken)
    {
        var enrolled = await templateStore.GetTemplatesAsync(tenantId, personId, BiometricModality.Fingerprint, cancellationToken)
            .ConfigureAwait(false);
        if (enrolled.Count == 0)
        {
            throw FingerprintVerificationException.NoAuthorisedReference(personId);
        }

        var reference = enrolled[0];
        var now = _nowUtc();
        var recordId = _newId();

        ExtractedBiometricFeatures features;
        try
        {
            features = await fingerprintProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation, not a provider/vendor failure - AC03's
            // AcquisitionFailed outcome is for a live sample the provider could not turn into a
            // template, not for the caller abandoning the request. Nothing is recorded, because
            // nothing about this attempt was ever decided; propagate exactly as any other
            // cancellable async call would.
            throw;
        }
        catch (Exception)
        {
            // BIO-BIO-004-F01 (Seyi round 1, High): this used to catch only ArgumentException,
            // so a provider/vendor failure that was not an ArgumentException (a device timeout,
            // an SDK-specific exception, an internal provider-side cancellation unrelated to this
            // call's own token) escaped uncaught and no FingerprintVerificationRecord was ever
            // written - AC03 requires extraction failure to be recorded as AcquisitionFailed
            // regardless of which exception type the provider adapter happens to raise. The
            // provider is an external adapter seam (IFingerprintProvider - a vendor SDK
            // implements this directly); this service must contain whatever it throws, the same
            // "provider failure is always a recorded outcome, never an unhandled exception"
            // discipline the class-level remarks describe.
            var failed = new FingerprintVerificationRecord
            {
                Id = recordId,
                TenantId = tenantId,
                PersonId = personId,
                ComparisonOutcome = BiometricComparisonOutcome.AcquisitionFailed,
                AccessDecision = AccessDecision.Deny,
                DenyReason = "acquisition_failed",
                ProviderId = reference.ProviderId,
                ProviderVersion = reference.ProviderVersion,
                OccurredAtUtc = now,
            };
            await recordStore.SaveAsync(failed, CancellationToken.None).ConfigureAwait(false);
            return failed;
        }

        var probe = BiometricTemplate.Create(
            _newId(), tenantId, personId, BiometricModality.Fingerprint, fingerprintProvider.ProviderId, features.ProviderVersion,
            features.Payload, now);
        var matchResult = await matcher.CompareAsync(probe, reference, cancellationToken).ConfigureAwait(false);

        var comparisonOutcome = matchResult.IsMatch ? BiometricComparisonOutcome.Matched : BiometricComparisonOutcome.NotMatched;
        var accessDecision = AccessDecision.Deny;
        string? denyReason = comparisonOutcome == BiometricComparisonOutcome.Matched ? null : "no_match";

        if (comparisonOutcome == BiometricComparisonOutcome.Matched)
        {
            var status = await identityStatusGateway.GetStatusAsync(personId, cancellationToken).ConfigureAwait(false);
            denyReason = status switch
            {
                IdentityStatus.Suspended => "identity_suspended",
                IdentityStatus.Revoked => "identity_revoked",
                _ => null,
            };
            accessDecision = denyReason is null ? AccessDecision.Allow : AccessDecision.Deny;
        }

        var record = new FingerprintVerificationRecord
        {
            Id = recordId,
            TenantId = tenantId,
            PersonId = personId,
            ComparisonOutcome = comparisonOutcome,
            AccessDecision = accessDecision,
            DenyReason = denyReason,
            Score = matchResult.Score,
            MatcherId = matchResult.MatcherId,
            MatchedTemplateId = matchResult.MatchedTemplateId,
            ProviderId = reference.ProviderId,
            ProviderVersion = reference.ProviderVersion,
            OccurredAtUtc = now,
        };

        await recordStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public Task<FingerprintVerificationRecord?> GetRecordAsync(string tenantId, string recordId, CancellationToken cancellationToken) =>
        recordStore.GetAsync(tenantId, recordId, cancellationToken);

    public Task<IReadOnlyList<FingerprintVerificationRecord>> GetHistoryAsync(
        string tenantId, string personId, CancellationToken cancellationToken) =>
        recordStore.GetForPersonAsync(tenantId, personId, cancellationToken);
}
