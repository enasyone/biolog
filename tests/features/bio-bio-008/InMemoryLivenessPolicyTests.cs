using Biolog.Features.Liveness.Domain;
using Xunit;

namespace Biolog.Features.Liveness.Tests;

public sealed class InMemoryLivenessPolicyTests
{
    [Fact]
    public async Task AC03_an_unconfigured_tenant_workflow_pair_defaults_to_not_required()
    {
        var policy = new InMemoryLivenessPolicy();

        var required = await policy.IsRequiredAsync("ten_a", LivenessWorkflows.FaceVerification1to1, CancellationToken.None);

        Assert.False(required);
    }

    [Fact]
    public async Task AC03_setting_a_workflow_required_for_one_tenant_makes_it_report_required()
    {
        var policy = new InMemoryLivenessPolicy();
        policy.SetRequired("ten_a", LivenessWorkflows.FaceVerification1to1);

        var required = await policy.IsRequiredAsync("ten_a", LivenessWorkflows.FaceVerification1to1, CancellationToken.None);

        Assert.True(required);
    }

    [Fact]
    public async Task AC03_requiring_liveness_for_one_workflow_does_not_affect_a_different_workflow_for_the_same_tenant()
    {
        var policy = new InMemoryLivenessPolicy();
        policy.SetRequired("ten_a", LivenessWorkflows.FaceVerification1to1);

        var enrolmentRequired = await policy.IsRequiredAsync("ten_a", LivenessWorkflows.FaceEnrolment, CancellationToken.None);

        Assert.False(enrolmentRequired);
    }

    [Fact]
    public async Task AC03_requiring_liveness_for_one_tenant_does_not_affect_another_tenant()
    {
        var policy = new InMemoryLivenessPolicy();
        policy.SetRequired("ten_a", LivenessWorkflows.FaceIdentification1toN);

        var forTenantB = await policy.IsRequiredAsync("ten_b", LivenessWorkflows.FaceIdentification1toN, CancellationToken.None);

        Assert.False(forTenantB);
    }

    [Fact]
    public async Task AC03_a_previously_required_workflow_can_be_explicitly_un_required()
    {
        var policy = new InMemoryLivenessPolicy();
        policy.SetRequired("ten_a", LivenessWorkflows.FaceEnrolment);
        policy.SetNotRequired("ten_a", LivenessWorkflows.FaceEnrolment);

        var required = await policy.IsRequiredAsync("ten_a", LivenessWorkflows.FaceEnrolment, CancellationToken.None);

        Assert.False(required);
    }
}
