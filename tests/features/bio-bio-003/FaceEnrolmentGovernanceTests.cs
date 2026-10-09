using Biolog.Features.FaceEnrolment.Domain;
using Biolog.Features.FaceEnrolment.Service;
using Xunit;

namespace Biolog.Features.FaceEnrolment.Tests;

/// <summary>BIO-IDN-001-QA-01/BIO-PHO-001-QA-01-shaped governance checks.</summary>
public sealed class FaceEnrolmentGovernanceTests
{
    [Fact]
    public void IFaceBiometricReferenceRepository_and_InMemoryFaceBiometricReferenceRepository_are_not_public()
    {
        Assert.False(typeof(IFaceBiometricReferenceRepository).IsPublic);
        Assert.False(typeof(InMemoryFaceBiometricReferenceRepository).IsPublic);
    }

    [Fact]
    public void FaceEnrolmentService_is_not_public()
    {
        Assert.False(typeof(FaceEnrolmentService).IsPublic);
    }

    [Fact]
    public void FaceBiometricReference_has_no_public_constructor()
    {
        Assert.DoesNotContain(typeof(FaceBiometricReference).GetConstructors(), c => c.IsPublic);
    }

    [Fact]
    public async Task GetAsync_cross_tenant_lookup_returns_null_not_an_exception()
    {
        var repository = new InMemoryFaceBiometricReferenceRepository();
        var template = Biolog.Features.Biometrics.Domain.BiometricTemplate.Create(
            "t1", "ten_a", "per_a", Biolog.Features.Biometrics.Domain.BiometricModality.Face, "prov", "1.0", [1], "2026-01-01T00:00:00Z");
        var liveness = new Biolog.Features.FaceEnrolment.Liveness.LivenessResult { IsLive = true, Score = 1.0, ProviderId = "fake" };
        var reference = new FaceBiometricReference("ref1", "ten_a", "per_a", 1, template, liveness, "2026-01-01T00:00:00Z");
        await repository.SaveAsync(reference, CancellationToken.None);

        var result = await repository.GetAsync("ten_b", "ref1", CancellationToken.None);

        Assert.Null(result);
    }
}
