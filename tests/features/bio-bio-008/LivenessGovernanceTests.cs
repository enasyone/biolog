using System.Reflection;
using Biolog.Features.Liveness.Domain;
using Biolog.Features.Liveness.Service;
using Xunit;

namespace Biolog.Features.Liveness.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks, mirroring every prior BIO-BIO story's own,
/// plus BIO-BIO-008-AC02's own structural-distinctness proof.</summary>
public sealed class LivenessGovernanceTests
{
    [Fact]
    public void ILivenessProvider_and_SyntheticLivenessProvider_are_not_public()
    {
        Assert.False(typeof(ILivenessProvider).IsPublic);
        Assert.False(typeof(SyntheticLivenessProvider).IsPublic);
    }

    [Fact]
    public void ILivenessPolicy_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(ILivenessPolicy).IsPublic);
        Assert.False(typeof(InMemoryLivenessPolicy).IsPublic);
    }

    [Fact]
    public void LivenessEvaluationService_is_not_public()
    {
        Assert.False(typeof(LivenessEvaluationService).IsPublic);
    }

    [Fact]
    public void this_assembly_has_no_reference_to_BIO_BIO_005s_own_assembly()
    {
        var referenced = typeof(LivenessEvidence).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("Biolog.Features.FaceVerification", referenced);
    }

    /// <summary>
    /// BIO-BIO-008-AC02: "Failure is distinct from face mismatch." Structural proof: this
    /// feature's own liveness-outcome type carries no field whose name could be confused with a
    /// match/verification decision, and no member of <see cref="LivenessStatus"/> is named
    /// anything match-shaped.
    /// </summary>
    [Fact]
    public void LivenessEvidence_carries_no_match_or_mismatch_shaped_field()
    {
        var fieldNames = typeof(LivenessEvidence).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name.ToLowerInvariant())
            .ToArray();

        Assert.DoesNotContain(fieldNames, name => name.Contains("match"));
        Assert.DoesNotContain(fieldNames, name => name.Contains("mismatch"));
    }

    [Fact]
    public void LivenessStatus_members_are_liveness_shaped_not_match_shaped()
    {
        var memberNames = Enum.GetNames<LivenessStatus>().Select(n => n.ToLowerInvariant()).ToArray();

        Assert.DoesNotContain(memberNames, name => name.Contains("match"));
    }
}
