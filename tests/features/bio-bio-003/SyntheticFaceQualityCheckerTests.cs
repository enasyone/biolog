using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Domain;
using Biolog.Features.FaceEnrolment.Quality;
using Xunit;

namespace Biolog.Features.FaceEnrolment.Tests;

public sealed class SyntheticFaceQualityCheckerTests
{
    private static BiometricSample Sample(params byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };

    [Fact]
    public async Task Empty_payload_detects_zero_faces()
    {
        var checker = new SyntheticFaceQualityChecker();
        var assessment = await checker.AssessAsync(Sample(), CancellationToken.None);
        Assert.Equal(0, assessment.FaceCount);
    }

    [Fact]
    public async Task First_byte_is_read_directly_as_face_count()
    {
        var checker = new SyntheticFaceQualityChecker();
        var assessment = await checker.AssessAsync(Sample(2, 0), CancellationToken.None);
        Assert.Equal(2, assessment.FaceCount);
    }

    [Fact]
    public async Task A_payload_shorter_than_two_bytes_passes_every_check()
    {
        var checker = new SyntheticFaceQualityChecker();
        var assessment = await checker.AssessAsync(Sample(1), CancellationToken.None);
        Assert.All(assessment.Checks, c => Assert.True(c.Passed));
    }

    [Fact]
    public async Task All_seven_checks_are_always_represented()
    {
        var checker = new SyntheticFaceQualityChecker();
        var assessment = await checker.AssessAsync(Sample(1, 0), CancellationToken.None);
        Assert.Equal(7, assessment.Checks.Count);
        Assert.Equal(Enum.GetValues<FaceQualityCheck>().OrderBy(c => c), assessment.Checks.Select(c => c.Check).OrderBy(c => c));
    }

    [Fact]
    public async Task Bit_zero_of_the_second_byte_fails_only_the_pose_check()
    {
        var checker = new SyntheticFaceQualityChecker();
        var assessment = await checker.AssessAsync(Sample(1, 0b0000001), CancellationToken.None);

        var pose = assessment.Checks.Single(c => c.Check == FaceQualityCheck.Pose);
        Assert.False(pose.Passed);
        Assert.NotNull(pose.Guidance);
        Assert.All(assessment.Checks.Where(c => c.Check != FaceQualityCheck.Pose), c => Assert.True(c.Passed));
    }
}
