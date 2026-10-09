using Biolog.Features.FaceSearch.Domain;
using Biolog.Features.TenantFeatureConfig;
using Biolog.Features.TenantFeatureConfig.Api;
using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;
using Biolog.Features.TenantFeatureConfig.Identifiers;
using Biolog.Features.TenantFeatureConfig.Integration;
using Xunit;

namespace Biolog.Features.TenantFeatureConfig.Tests;

/// <summary>
/// BIO-TEN-003-AC02/AC04: proves <see cref="TenantFeatureFlagPolicyAdapter"/> - the real
/// implementation now wired into <c>BioBio007Module.RegisterServices</c> in place of
/// <c>SyntheticFaceSearchFeatureFlagPolicy</c> - is genuinely fail-closed by default and
/// correctly reflects this story's own real, persisted-in-process configuration once Face-1N
/// is explicitly enabled.
///
/// <b>Scope boundary (disclosed, see docs/features/bio-ten-003/README.md "AC02: the
/// BIO-BIO-007 swap").</b> This does not construct <c>FaceSearchService</c> itself and call
/// <c>SearchAsync</c> directly: that type (and its own collaborators - <c>IFaceGalleryStore</c>,
/// <c>IFaceGalleryApprovalGateway</c>, <c>IFaceSearchAuditSink</c>) is <c>internal</c> to
/// BIO-BIO-007's own assembly, with <c>InternalsVisibleTo</c> granted only to
/// <c>Biolog.Api</c> and <c>Biolog.Features.FaceSearch.Tests</c> - editing that grant is
/// BIO-BIO-007's own owned file (<c>api/features/bio-bio-007/**/*.csproj</c>), outside this
/// story's write scope and not among the edits pre-approved for this story (only the one-line
/// registration swap in <c>Modules/BioBio007Module.cs</c> was). BIO-BIO-007's own
/// <c>FaceSearchServiceTests.cs</c> (already-accepted, untouched by this story) already proves
/// <c>SearchAsync</c> honours whatever <see cref="IFaceSearchFeatureFlagPolicy"/> it is given.
/// This test proves the other half: the concrete policy <c>FaceSearchService</c> now actually
/// receives via that one changed registration line is backed by this story's own real
/// configuration, fail-closed by default, and flips correctly once Face-1N is explicitly
/// enabled through this story's real update path - never as a side effect of enabling the
/// parent Biometrics module.
/// </summary>
public sealed class TenantFeatureFlagPolicyAdapterTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");
    private static string NewTenantId() => $"ten_{Guid.CreateVersion7():D}";

    private static (TenantFeatureConfigurationService Service, IFaceSearchFeatureFlagPolicy Policy) NewAdapter()
    {
        var service = new TenantFeatureConfigurationService(new InMemoryTenantFeatureConfigurationStore(), new InMemoryTenantFeatureConfigurationAuditSink());
        IFaceSearchFeatureFlagPolicy policy = new TenantFeatureFlagPolicyAdapter(service);
        return (service, policy);
    }

    [Fact]
    public void Implements_the_real_BIO_BIO_007_extension_interface()
    {
        var (_, policy) = NewAdapter();
        Assert.IsAssignableFrom<IFaceSearchFeatureFlagPolicy>(policy);
    }

    [Fact]
    public void A_tenant_never_configured_is_disabled_by_default_AC04()
    {
        var (_, policy) = NewAdapter();
        Assert.False(policy.IsEnabledForTenant(NewTenantId()));
    }

    [Fact]
    public async Task Enabling_the_parent_biometrics_module_alone_does_not_enable_face_1n_AC02()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, policy) = NewAdapter();
        var tenantId = NewTenantId();

        await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Enabled = true } }, Actor, CanonicalId.NewEventId(), ct);

        Assert.False(policy.IsEnabledForTenant(tenantId));
    }

    [Fact]
    public async Task Explicitly_enabling_face_1n_and_its_parent_module_flips_the_policy_to_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, policy) = NewAdapter();
        var tenantId = NewTenantId();

        await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Enabled = true, Face1NIdentificationEnabled = true } }, Actor, CanonicalId.NewEventId(), ct);

        Assert.True(policy.IsEnabledForTenant(tenantId));
    }

    /// <summary>Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High): enabling only the child flag, with the parent module left disabled, must NOT enable the policy.</summary>
    [Fact]
    public async Task Enabling_face_1n_alone_with_the_parent_module_disabled_leaves_the_policy_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, policy) = NewAdapter();
        var tenantId = NewTenantId();

        await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Face1NIdentificationEnabled = true } }, Actor, CanonicalId.NewEventId(), ct);

        Assert.False(policy.IsEnabledForTenant(tenantId));
    }

    [Fact]
    public async Task Disabling_face_1n_again_flips_the_policy_back_to_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, policy) = NewAdapter();
        var tenantId = NewTenantId();

        var v1 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Enabled = true, Face1NIdentificationEnabled = true } }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(policy.IsEnabledForTenant(tenantId));

        await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = v1.Version, Biometrics = new BiometricsConfigRequest { Face1NIdentificationEnabled = false } }, Actor, CanonicalId.NewEventId(), ct);

        Assert.False(policy.IsEnabledForTenant(tenantId));
    }

    /// <summary>Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High, the exact reproduction both reviewers gave): disabling the PARENT module must fail closed for Face 1:N even though the stored child flag is left true.</summary>
    [Fact]
    public async Task Disabling_the_parent_biometrics_module_immediately_denies_face_1n_even_though_the_child_flag_remains_true()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, policy) = NewAdapter();
        var tenantId = NewTenantId();

        var v1 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Enabled = true, Face1NIdentificationEnabled = true } }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(policy.IsEnabledForTenant(tenantId));

        var v2 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = v1.Version, Biometrics = new BiometricsConfigRequest { Enabled = false } }, Actor, CanonicalId.NewEventId(), ct);

        // The stored child flag is untouched (AC02: independent fields) ...
        Assert.True(v2.Biometrics.Face1NIdentificationEnabled);
        // ... but the EFFECTIVE policy FaceSearchService actually consumes is false, parent-closed.
        Assert.False(policy.IsEnabledForTenant(tenantId));
    }

    [Fact]
    public async Task Enabling_face_1n_for_one_tenant_never_leaks_to_another_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, policy) = NewAdapter();
        var enabledTenant = NewTenantId();
        var otherTenant = NewTenantId();

        await service.UpdateAsync(enabledTenant, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Enabled = true, Face1NIdentificationEnabled = true } }, Actor, CanonicalId.NewEventId(), ct);

        Assert.True(policy.IsEnabledForTenant(enabledTenant));
        Assert.False(policy.IsEnabledForTenant(otherTenant));
    }
}
