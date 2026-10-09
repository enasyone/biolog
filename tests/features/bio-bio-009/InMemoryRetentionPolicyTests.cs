using Biolog.Features.BiometricRetention.Domain;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

public sealed class InMemoryRetentionPolicyTests
{
    [Fact]
    public async Task AC01_an_unconfigured_tenant_category_pair_reports_the_documented_default()
    {
        var policy = new InMemoryRetentionPolicy();

        var setting = await policy.GetAsync("ten_a", RetentionCategory.FaceTemplate, CancellationToken.None);

        Assert.Equal(RetentionDefaults.FaceTemplateDays, setting.RetentionDays);
        Assert.False(setting.IsExplicitlyConfigured);
    }

    [Fact]
    public async Task AC01_setting_a_category_for_one_tenant_makes_it_report_the_configured_value()
    {
        var policy = new InMemoryRetentionPolicy();
        await policy.SetAsync("ten_a", RetentionCategory.FingerprintTemplate, 180, CancellationToken.None);

        var setting = await policy.GetAsync("ten_a", RetentionCategory.FingerprintTemplate, CancellationToken.None);

        Assert.Equal(180, setting.RetentionDays);
        Assert.True(setting.IsExplicitlyConfigured);
    }

    [Fact]
    public async Task AC01_configuring_one_category_does_not_affect_a_different_category_for_the_same_tenant()
    {
        var policy = new InMemoryRetentionPolicy();
        await policy.SetAsync("ten_a", RetentionCategory.FingerprintTemplate, 180, CancellationToken.None);

        var faceSetting = await policy.GetAsync("ten_a", RetentionCategory.FaceTemplate, CancellationToken.None);

        Assert.False(faceSetting.IsExplicitlyConfigured);
    }

    [Fact]
    public async Task AC01_configuring_one_tenant_does_not_affect_another_tenant()
    {
        var policy = new InMemoryRetentionPolicy();
        await policy.SetAsync("ten_a", RetentionCategory.RawImage, 7, CancellationToken.None);

        var forTenantB = await policy.GetAsync("ten_b", RetentionCategory.RawImage, CancellationToken.None);

        Assert.False(forTenantB.IsExplicitlyConfigured);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AC01_a_non_positive_retention_day_count_is_rejected_before_anything_is_stored(int invalidDays)
    {
        var policy = new InMemoryRetentionPolicy();

        await Assert.ThrowsAsync<BiometricRetentionException>(
            () => policy.SetAsync("ten_a", RetentionCategory.FaceTemplate, invalidDays, CancellationToken.None));

        var setting = await policy.GetAsync("ten_a", RetentionCategory.FaceTemplate, CancellationToken.None);
        Assert.False(setting.IsExplicitlyConfigured);
    }
}
