using Biolog.Features.FaceEnrolment.Domain;
using Xunit;

namespace Biolog.Features.FaceEnrolment.Tests;

/// <summary>AC01/AC02/AC04 domain-level tests for <see cref="FaceQualityAssessment"/>.</summary>
public sealed class FaceQualityAssessmentTests
{
    private static readonly IReadOnlyList<FaceQualityCheckResult> AllPass =
        Enum.GetValues<FaceQualityCheck>().Select(FaceQualityCheckResult.Pass).ToList();

    [Fact]
    public void AC01_zero_faces_is_not_approved_and_carries_guidance()
    {
        var assessment = new FaceQualityAssessment { FaceCount = 0, Checks = AllPass };

        Assert.False(assessment.FaceCountApproved);
        Assert.False(assessment.IsApproved);
        Assert.Contains(assessment.RecaptureGuidance, g => g.StartsWith("no_face_detected", StringComparison.Ordinal));
    }

    [Fact]
    public void AC01_multiple_faces_is_not_approved_and_carries_guidance()
    {
        var assessment = new FaceQualityAssessment { FaceCount = 3, Checks = AllPass };

        Assert.False(assessment.IsApproved);
        Assert.Contains(assessment.RecaptureGuidance, g => g.StartsWith("multiple_faces_detected", StringComparison.Ordinal));
    }

    [Fact]
    public void AC01_exactly_one_face_with_all_checks_passing_is_approved()
    {
        var assessment = new FaceQualityAssessment { FaceCount = 1, Checks = AllPass };

        Assert.True(assessment.IsApproved);
        Assert.Empty(assessment.RecaptureGuidance);
    }

    // AC02: each of the seven named checks independently blocks approval.
    [Theory]
    [InlineData(FaceQualityCheck.Pose)]
    [InlineData(FaceQualityCheck.Eyes)]
    [InlineData(FaceQualityCheck.Lighting)]
    [InlineData(FaceQualityCheck.Sharpness)]
    [InlineData(FaceQualityCheck.Resolution)]
    [InlineData(FaceQualityCheck.Occlusion)]
    [InlineData(FaceQualityCheck.Background)]
    public void AC02_a_single_failed_check_blocks_approval(FaceQualityCheck failing)
    {
        var checks = Enum.GetValues<FaceQualityCheck>()
            .Select(c => c == failing ? FaceQualityCheckResult.Fail(c, "fix it") : FaceQualityCheckResult.Pass(c))
            .ToList();

        var assessment = new FaceQualityAssessment { FaceCount = 1, Checks = checks };

        Assert.False(assessment.IsApproved);
        Assert.Contains("fix it", assessment.RecaptureGuidance);
    }

    [Fact]
    public void AC04_a_failed_check_without_guidance_cannot_be_constructed()
    {
        Assert.Throws<ArgumentException>(() => FaceQualityCheckResult.Fail(FaceQualityCheck.Pose, ""));
    }
}
