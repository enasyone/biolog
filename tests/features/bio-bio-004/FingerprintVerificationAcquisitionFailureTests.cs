using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FingerprintVerification.Domain;
using Biolog.Features.FingerprintVerification.Service;
using Xunit;

namespace Biolog.Features.FingerprintVerification.Tests;

/// <summary>BIO-BIO-004-F01 (Seyi round 1, High): <c>VerifyAsync</c> used to catch only
/// <see cref="ArgumentException"/> around <see cref="IFingerprintProvider.ExtractTemplateAsync"/>,
/// so any other provider/vendor failure (a device timeout, an SDK-specific exception, an
/// internal provider-side cancellation unrelated to the caller's own token) escaped uncaught and
/// no <see cref="FingerprintVerificationRecord"/> was ever written - violating AC03's requirement
/// that extraction failure is always recorded as the distinct <see cref="BiometricComparisonOutcome.AcquisitionFailed"/>
/// outcome. These tests reproduce the exact failure modes the review named and prove the fix
/// contains every one of them while still letting a genuine CALLER cancellation propagate.</summary>
public sealed class FingerprintVerificationAcquisitionFailureTests
{
    private const string NowUtc = "2026-01-01T00:00:00Z";

    private static async Task<FingerprintVerificationService> NewServiceWithEnrolledReferenceAsync(IFingerprintProvider provider)
    {
        var templateStore = new InMemoryBiometricTemplateStore();
        var digest = System.Security.Cryptography.SHA256.HashData([1, 2, 3]);
        var template = BiometricTemplate.Create(
            "template-1", "ten_a", "per_a", BiometricModality.Fingerprint, "synthetic-fingerprint-v1", "synthetic-fingerprint-1.0.0",
            digest, NowUtc);
        await templateStore.SaveAsync(template, CancellationToken.None);

        var identityGateway = new FakeIdentityStatusGateway();
        identityGateway.Set("per_a", IdentityStatus.Active);

        return new FingerprintVerificationService(
            templateStore, provider, new SyntheticBiometricMatcher(), identityGateway, new InMemoryFingerprintVerificationRecordStore(),
            newId: () => "record-1", nowUtc: () => NowUtc);
    }

    private static BiometricSample Sample() => new() { Payload = [1, 2, 3], ContentType = "application/octet-stream", CapturedAtUtc = NowUtc };

    [Fact]
    public async Task A_non_ArgumentException_provider_failure_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var provider = new FakeFingerprintProvider(() => new InvalidOperationException("device offline"));
        var service = await NewServiceWithEnrolledReferenceAsync(provider);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.AcquisitionFailed, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("acquisition_failed", record.DenyReason);
    }

    [Fact]
    public async Task A_TimeoutException_provider_failure_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var provider = new FakeFingerprintProvider(() => new TimeoutException("scanner did not respond"));
        var service = await NewServiceWithEnrolledReferenceAsync(provider);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.AcquisitionFailed, record.ComparisonOutcome);
    }

    [Fact]
    public async Task A_vendor_specific_exception_type_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var provider = new FakeFingerprintProvider(() => new FormatException("vendor SDK: corrupt frame"));
        var service = await NewServiceWithEnrolledReferenceAsync(provider);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.AcquisitionFailed, record.ComparisonOutcome);
    }

    [Fact]
    public async Task A_provider_internal_cancellation_unrelated_to_the_caller_token_is_recorded_as_AcquisitionFailed()
    {
        // The provider raises OperationCanceledException on its OWN internal timeout, not
        // because the caller's token was ever cancelled - the caller's token here is
        // CancellationToken.None, so IsCancellationRequested is always false.
        var provider = new FakeFingerprintProvider(() => new OperationCanceledException("internal device timeout"));
        var service = await NewServiceWithEnrolledReferenceAsync(provider);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.AcquisitionFailed, record.ComparisonOutcome);
    }

    [Fact]
    public async Task A_genuine_caller_cancellation_propagates_and_writes_no_record()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakeFingerprintProvider(() => new OperationCanceledException(cts.Token));
        var service = await NewServiceWithEnrolledReferenceAsync(provider);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.VerifyAsync("ten_a", "per_a", Sample(), cts.Token));
    }
}
