using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FaceEnrolment.Domain;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceEnrolment.Service;
using Xunit;

namespace Biolog.Features.FaceEnrolment.Tests;

/// <summary>End-to-end tests for <see cref="FaceEnrolmentService"/>: AC03 (model/version +
/// source-photo linkage), AC04 (recapture guidance, no exception), AC05 (liveness gate).</summary>
public sealed class FaceEnrolmentServiceTests
{
    private const string TenantA = "ten_a";
    private const string TenantB = "ten_b";
    private const string PersonA = "per_a";
    private const int PhotoVersion = 1;

    private static FaceEnrolmentService NewService(
        out InMemoryFaceBiometricReferenceRepository repository, bool photoExists = true, bool isLive = true)
    {
        repository = new InMemoryFaceBiometricReferenceRepository();
        var gateway = new FakePhotoMasterExistenceGateway();
        if (photoExists)
        {
            gateway.Add(PersonA, PhotoVersion);
        }
        return new FaceEnrolmentService(
            repository, gateway, new SyntheticFaceProvider(), new SyntheticFaceQualityChecker(), new FakeLivenessDetector(isLive));
    }

    private static BiometricSample Sample(params byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };

    // 1 face, second byte 0 -> every AC02 check passes.
    private static readonly byte[] GoodCapture = [1, 0];

    [Fact]
    public async Task AC03_an_approved_capture_retains_model_version_and_source_photo_linkage()
    {
        var service = NewService(out _);

        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        Assert.True(outcome.Approved);
        Assert.NotNull(outcome.Reference);
        Assert.Equal(PhotoVersion, outcome.Reference!.SourcePhotoMasterVersion);
        Assert.Equal(PersonA, outcome.Reference.PersonId);
        Assert.Equal(BiometricModality.Face, outcome.Reference.Template.Modality);
        Assert.NotEmpty(outcome.Reference.Template.ProviderId);
        Assert.NotEmpty(outcome.Reference.Template.ProviderVersion);
    }

    [Fact]
    public async Task Approved_capture_is_persisted_and_retrievable_as_the_latest_reference()
    {
        var service = NewService(out _);
        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        var latest = await service.GetLatestReferenceAsync(TenantA, PersonA, CancellationToken.None);

        Assert.Equal(outcome.Reference!.Id, latest!.Id);
    }

    [Fact]
    public async Task CaptureAsync_throws_when_the_source_photo_does_not_exist()
    {
        var service = NewService(out _, photoExists: false);

        await Assert.ThrowsAsync<FaceEnrolmentException>(
            () => service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None));
    }

    // AC04: a poor capture is a normal outcome, not an exception.
    [Fact]
    public async Task AC04_zero_faces_yields_guidance_instead_of_throwing()
    {
        var service = NewService(out var repository);

        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(0, 0), CancellationToken.None);

        Assert.False(outcome.Approved);
        Assert.Null(outcome.Reference);
        Assert.NotEmpty(outcome.RecaptureGuidance);
        Assert.Null(await repository.GetLatestForPersonAsync(TenantA, PersonA, CancellationToken.None));
    }

    [Fact]
    public async Task AC04_a_failed_quality_check_yields_actionable_guidance_naming_the_check()
    {
        var service = NewService(out _);

        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(1, 0b0000010), CancellationToken.None); // Eyes bit

        Assert.False(outcome.Approved);
        Assert.Contains(outcome.RecaptureGuidance, g => g.Contains("eyes", StringComparison.OrdinalIgnoreCase));
    }

    // AC05: liveness is a real gate, not merely an interface that exists unused.
    [Fact]
    public async Task AC05_a_failed_liveness_check_blocks_approval_even_when_quality_passes()
    {
        var service = NewService(out var repository, isLive: false);

        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        Assert.False(outcome.Approved);
        Assert.Contains(outcome.RecaptureGuidance, g => g.Contains("liveness", StringComparison.OrdinalIgnoreCase));
        Assert.Null(await repository.GetLatestForPersonAsync(TenantA, PersonA, CancellationToken.None));
    }

    [Fact]
    public async Task AC05_an_approved_reference_records_the_liveness_result()
    {
        var service = NewService(out _, isLive: true);

        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        Assert.True(outcome.Reference!.Liveness.IsLive);
    }

    [Fact]
    public async Task ADR0002_S4_3_a_reference_approved_in_one_tenant_is_not_visible_from_another_tenant()
    {
        var service = NewService(out _);
        await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        var fromOtherTenant = await service.GetLatestReferenceAsync(TenantB, PersonA, CancellationToken.None);

        Assert.Null(fromOtherTenant);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: approved, additive extension (Kemi, 2026-10-06) - a plain
    /// persistence wrapper, matching <see cref="FaceEnrolmentService.GetLatestReferenceAsync"/>'s
    /// own directness. No lock, no audit - this service has neither today.</summary>
    [Fact]
    public async Task DeleteReferenceAsync_removes_the_reference()
    {
        var service = NewService(out var repository);
        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        var deletedPersonId = await service.DeleteReferenceAsync(TenantA, outcome.Reference!.Id, CancellationToken.None);

        Assert.Equal(PersonA, deletedPersonId);
        Assert.Null(await repository.GetAsync(TenantA, outcome.Reference.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteReferenceAsync_returns_null_for_a_reference_that_does_not_exist()
    {
        var service = NewService(out _);

        var deletedPersonId = await service.DeleteReferenceAsync(TenantA, "no-such-reference", CancellationToken.None);

        Assert.Null(deletedPersonId);
    }

    [Fact]
    public async Task DeleteReferenceAsync_cannot_delete_another_tenants_reference()
    {
        var service = NewService(out var repository);
        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        var deletedPersonId = await service.DeleteReferenceAsync(TenantB, outcome.Reference!.Id, CancellationToken.None);

        Assert.Null(deletedPersonId);
        Assert.NotNull(await repository.GetAsync(TenantA, outcome.Reference.Id, CancellationToken.None));
    }

    /// <summary>BIO-BIO-009-AC04/ZB-BIO009-06 (round 3): a read-only peek, added so a durable
    /// gallery-cleanup obligation can be recorded BEFORE the reference is deleted - must never
    /// delete anything itself.</summary>
    [Fact]
    public async Task ResolveOwnerAsync_returns_the_owning_person_without_deleting_the_reference()
    {
        var service = NewService(out var repository);
        var outcome = await service.CaptureAsync(TenantA, PersonA, PhotoVersion, Sample(GoodCapture), CancellationToken.None);

        var resolvedPersonId = await service.ResolveOwnerAsync(TenantA, outcome.Reference!.Id, CancellationToken.None);

        Assert.Equal(PersonA, resolvedPersonId);
        Assert.NotNull(await repository.GetAsync(TenantA, outcome.Reference.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveOwnerAsync_returns_null_for_a_reference_that_does_not_exist()
    {
        var service = NewService(out _);

        var resolvedPersonId = await service.ResolveOwnerAsync(TenantA, "no-such-reference", CancellationToken.None);

        Assert.Null(resolvedPersonId);
    }
}
