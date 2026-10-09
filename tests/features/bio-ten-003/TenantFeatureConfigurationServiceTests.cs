using Biolog.Features.TenantFeatureConfig;
using Biolog.Features.TenantFeatureConfig.Api;
using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;
using Biolog.Features.TenantFeatureConfig.Identifiers;
using Xunit;

namespace Biolog.Features.TenantFeatureConfig.Tests;

/// <summary>BIO-TEN-003-AC01, AC02, AC03, AC04: core service behaviour.</summary>
public sealed class TenantFeatureConfigurationServiceTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");

    private static TenantFeatureConfigurationService NewService(out InMemoryTenantFeatureConfigurationAuditSink sink)
    {
        sink = new InMemoryTenantFeatureConfigurationAuditSink();
        return new TenantFeatureConfigurationService(new InMemoryTenantFeatureConfigurationStore(), sink);
    }

    private static string NewTenantId() => $"ten_{Guid.CreateVersion7():D}";

    // ---- AC04: fail-closed defaults ----

    [Fact]
    public async Task GetEffective_for_an_unknown_tenant_returns_every_module_disabled_not_a_throw()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var config = await service.GetEffectiveAsync(tenantId, ct);

        Assert.Equal(0, config.Version);
        Assert.False(config.Biometrics.Enabled);
        Assert.False(config.Biometrics.Face1NIdentificationEnabled);
        Assert.False(config.CardProductionEnabled);
        Assert.False(config.VisitorEnabled);
        Assert.False(config.AttendanceEnabled);
        Assert.False(config.TransportEnabled);
        Assert.False(config.DocumentVerify.Enabled);
        foreach (DocumentVerifySubfeature subfeature in Enum.GetValues<DocumentVerifySubfeature>())
        {
            Assert.False(config.DocumentVerify.IsEnabled(subfeature));
        }
    }

    [Fact]
    public async Task Gateway_methods_also_fail_closed_for_an_unknown_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        ITenantFeatureGateway gateway = NewService(out _);
        var tenantId = NewTenantId();

        foreach (TenantModule module in Enum.GetValues<TenantModule>())
        {
            Assert.False(await gateway.IsModuleEnabledAsync(tenantId, module, ct));
        }

        foreach (DocumentVerifySubfeature subfeature in Enum.GetValues<DocumentVerifySubfeature>())
        {
            Assert.False(await gateway.IsDocumentVerifySubfeatureEnabledAsync(tenantId, subfeature, ct));
        }

        Assert.False(await gateway.IsFace1NIdentificationEnabledAsync(tenantId, ct));
    }

    /// <summary>Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High): IsFace1NIdentificationEnabledAsync (the async gateway path) is also the parent-AND-child effective policy, not the raw child flag.</summary>
    [Fact]
    public async Task IsFace1NIdentificationEnabledAsync_requires_both_the_parent_module_and_the_child_flag()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        ITenantFeatureGateway gateway = service;
        var tenantId = NewTenantId();

        await service.UpdateAsync(
            tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Face1NIdentificationEnabled = true } },
            Actor, CanonicalId.NewEventId(), ct);
        Assert.False(await gateway.IsFace1NIdentificationEnabledAsync(tenantId, ct));

        var v2 = await service.UpdateAsync(
            tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 1, Biometrics = new BiometricsConfigRequest { Enabled = true } },
            Actor, CanonicalId.NewEventId(), ct);
        Assert.True(v2.Biometrics.Enabled && v2.Biometrics.Face1NIdentificationEnabled);
        Assert.True(await gateway.IsFace1NIdentificationEnabledAsync(tenantId, ct));
    }

    [Fact]
    public async Task Invalid_tenant_id_shape_is_a_validation_error_not_an_unhandled_exception()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var error = await Assert.ThrowsAsync<TenantFeatureConfigurationError>(() => service.GetEffectiveAsync("not-a-tenant-id", ct));
        Assert.Equal("invalid_tenant_id", error.Code);
    }

    // ---- AC01: all six modules, including Document Verify's eight subfeatures ----

    [Fact]
    public async Task Update_enables_each_of_the_six_top_level_modules_independently()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var r1 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, CardProductionEnabled = true }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(r1.CardProductionEnabled);
        Assert.False(r1.VisitorEnabled);

        var r2 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = r1.Version, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(r2.CardProductionEnabled); // untouched field survives
        Assert.True(r2.VisitorEnabled);

        var r3 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = r2.Version, AttendanceEnabled = true, TransportEnabled = true }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(r3.AttendanceEnabled);
        Assert.True(r3.TransportEnabled);

        var r4 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = r3.Version, DocumentVerify = new DocumentVerifyConfigRequest { Enabled = true } }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(r4.DocumentVerify.Enabled);

        var r5 = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = r4.Version, Biometrics = new BiometricsConfigRequest { Enabled = true } }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True(r5.Biometrics.Enabled);
        Assert.Equal(5, r5.Version);
    }

    [Fact]
    public async Task Update_enables_each_of_the_eight_document_verify_subfeatures_independently()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var updated = await service.UpdateAsync(
            tenantId,
            new UpdateTenantFeatureConfigurationRequest
            {
                Version = 0,
                DocumentVerify = new DocumentVerifyConfigRequest
                {
                    CaptureEnabled = true,
                    MrzEnabled = true,
                    BarcodeEnabled = true,
                    OcrEnabled = true,
                    AuthenticityEnabled = true,
                    DocumentIdentificationEnabled = true,
                    NfcEnabled = true,
                    SelfieComparisonEnabled = true,
                },
            },
            Actor, CanonicalId.NewEventId(), ct);

        foreach (DocumentVerifySubfeature subfeature in Enum.GetValues<DocumentVerifySubfeature>())
        {
            Assert.True(updated.DocumentVerify.IsEnabled(subfeature));
        }
        // The parent module switch is NOT implicitly flipped by enabling subfeatures.
        Assert.False(updated.DocumentVerify.Enabled);
    }

    // ---- AC02: Biometrics module vs. Face-1N are genuinely independent ----

    [Fact]
    public async Task Enabling_the_biometrics_module_never_implicitly_enables_face_1n_identification()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var updated = await service.UpdateAsync(
            tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Enabled = true } },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.True(updated.Biometrics.Enabled);
        Assert.False(updated.Biometrics.Face1NIdentificationEnabled);
    }

    [Fact]
    public async Task Face_1n_identification_can_be_enabled_independently_of_the_parent_module()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var updated = await service.UpdateAsync(
            tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, Biometrics = new BiometricsConfigRequest { Face1NIdentificationEnabled = true } },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.True(updated.Biometrics.Face1NIdentificationEnabled);
        Assert.False(updated.Biometrics.Enabled); // the parent switch was never touched
    }

    // ---- AC03: versioned (CAS) ----

    [Fact]
    public async Task First_write_for_a_tenant_targets_version_0_and_produces_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var updated = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(1, updated.Version);
    }

    [Fact]
    public async Task Stale_version_is_rejected_as_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();
        await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<TenantFeatureConfigurationError>(
            () => service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, AttendanceEnabled = true }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Missing_version_is_a_validation_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenantId = NewTenantId();

        var error = await Assert.ThrowsAsync<TenantFeatureConfigurationError>(
            () => service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task A_no_op_update_commits_and_audits_nothing_and_leaves_the_version_unmoved()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out var sink);
        var tenantId = NewTenantId();
        var created = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);
        var eventsBefore = sink.Events.Count;

        // Re-sending the exact same value it already has: nothing changes.
        var result = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = created.Version, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(created.Version, result.Version);
        Assert.Equal(eventsBefore, sink.Events.Count);
    }
}
