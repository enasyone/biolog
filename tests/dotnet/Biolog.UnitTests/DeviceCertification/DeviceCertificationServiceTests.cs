using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceCertification;
using Biolog.Api.Features.DeviceCertification.Audit;
using Biolog.Api.Tenancy;
using Xunit;

namespace Biolog.UnitTests.DeviceCertification;

public class DeviceCertificationServiceTests
{
    private static DeviceCertificationRegistrationRequest SampleRequest(string actorId = "usr_ops") => new(
        Vendor: "Acme",
        Model: "ReaderX",
        Firmware: "1.2.3",
        Sdk: "sdk-2.0",
        Status: CertificationStatus.Certified,
        TestEvidenceReference: "https://evidence.internal/reports/acme-readerx-1.2.3",
        ActorType: "user",
        ActorId: actorId,
        CorrelationId: null);

    private static Biolog.Api.Features.DeviceCertification.DeviceCertificationService BuildService(
        TenantContext? tenant,
        out InMemoryDeviceCertificationRepository repository,
        out RecordingCertificationAuditSink auditSink)
    {
        repository = new InMemoryDeviceCertificationRepository();
        auditSink = new RecordingCertificationAuditSink();
        var accessor = new TenantContextAccessor(tenant);
        return new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            accessor, repository, auditSink, new DeviceCertificationLockRegistry());
    }

    // --- BIO-DEV-002-AC01: registration records the SDK/firmware version and test evidence ---

    [Fact]
    public async Task RegisterGlobalAsync_creates_an_entry_at_version_one_with_no_tenant_and_records_an_audit_event()
    {
        var service = BuildService(tenant: null, out _, out var auditSink);

        var entry = await service.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("Acme", entry.Vendor);
        Assert.Equal("ReaderX", entry.Model);
        Assert.Equal("1.2.3", entry.Firmware);
        Assert.Equal("sdk-2.0", entry.Sdk);
        Assert.Equal(CertificationStatus.Certified, entry.Status);
        Assert.Equal("https://evidence.internal/reports/acme-readerx-1.2.3", entry.TestEvidenceReference);
        Assert.Equal(1, entry.Version);
        Assert.Null(entry.TenantId);

        var recorded = Assert.Single(auditSink.Events);
        Assert.Equal("device_certification.registered", recorded.Action);
        Assert.Equal("device_certification", recorded.Target.Type);
        Assert.Null(recorded.TenantId);
        Assert.Equal(CertificationAuditOutcome.Allowed, recorded.Outcome);
        Assert.StartsWith("evt_", recorded.CorrelationId);
    }

    [Fact]
    public async Task RegisterGlobalAsync_requires_no_tenant_context()
    {
        // Control-plane, "As platform operations" per the user story - unlike
        // Features.DeviceRegistry.DeviceService, this must succeed with no resolved tenant.
        var service = BuildService(tenant: null, out _, out _);

        var entry = await service.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Null(entry.TenantId);
    }

    [Fact]
    public async Task RegisterGlobalAsync_throws_when_an_entry_already_exists_for_the_same_combination()
    {
        var service = BuildService(tenant: null, out _, out var auditSink);
        await service.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DeviceCertificationAlreadyExistsException>(
            () => service.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Single(auditSink.Events); // the rejected second attempt is not itself audited
    }

    [Fact]
    public async Task RegisterGlobalAsync_rejects_an_empty_test_evidence_reference()
    {
        var service = BuildService(tenant: null, out _, out _);
        var request = SampleRequest() with { TestEvidenceReference = "   " };

        await Assert.ThrowsAsync<InvalidTestEvidenceReferenceException>(
            () => service.RegisterGlobalAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterGlobalAsync_rejects_a_key_field_containing_a_reserved_separator()
    {
        var service = BuildService(tenant: null, out _, out _);
        var request = SampleRequest() with { Vendor = "Acme/Sub" };

        await Assert.ThrowsAsync<InvalidDeviceCertificationKeyException>(
            () => service.RegisterGlobalAsync(request, TestContext.Current.CancellationToken));
    }

    // --- BIO-DEV-002-AC03: a tenant's own override is independent of the global default ---

    [Fact]
    public async Task RegisterTenantOverrideAsync_throws_when_no_tenant_context_is_resolved()
    {
        var service = BuildService(tenant: null, out _, out _);

        await Assert.ThrowsAsync<TenantContextMissingException>(
            () => service.RegisterTenantOverrideAsync(SampleRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterTenantOverrideAsync_stores_a_separate_entry_from_the_global_default()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var auditSink = new RecordingCertificationAuditSink();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var globalService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, auditSink, lockRegistry);
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var tenantService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(tenant), repository, auditSink, lockRegistry);

        await globalService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);
        var overrideEntry = await tenantService.RegisterTenantOverrideAsync(
            SampleRequest() with { Status = CertificationStatus.Unsupported }, TestContext.Current.CancellationToken);

        Assert.Equal("ten_asu", overrideEntry.TenantId);
        Assert.Equal(CertificationStatus.Unsupported, overrideEntry.Status);

        var globalEntry = await globalService.GetGlobalAsync(overrideEntry.Key, TestContext.Current.CancellationToken);
        Assert.Equal(CertificationStatus.Certified, globalEntry!.Status); // untouched by the override
    }

    [Fact]
    public async Task GetEffectiveAsync_prefers_the_calling_tenants_override_over_the_global_default()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var auditSink = new RecordingCertificationAuditSink();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var globalService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, auditSink, lockRegistry);
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var tenantAccessor = new TenantContextAccessor(tenant);
        var tenantService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            tenantAccessor, repository, auditSink, lockRegistry);

        await globalService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);
        var overrideEntry = await tenantService.RegisterTenantOverrideAsync(
            SampleRequest() with { Status = CertificationStatus.Unsupported }, TestContext.Current.CancellationToken);

        var effectiveForTenant = await tenantService.GetEffectiveAsync(overrideEntry.Key, TestContext.Current.CancellationToken);
        var effectiveWithNoTenant = await globalService.GetEffectiveAsync(overrideEntry.Key, TestContext.Current.CancellationToken);

        Assert.Equal(CertificationStatus.Unsupported, effectiveForTenant!.Status); // the tenant's own override
        Assert.Equal(CertificationStatus.Certified, effectiveWithNoTenant!.Status); // falls back to the global default
    }

    [Fact]
    public async Task GetEffectiveAsync_falls_back_to_the_global_default_when_the_tenant_has_no_override()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var auditSink = new RecordingCertificationAuditSink();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var globalService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, auditSink, lockRegistry);
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var tenantService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(tenant), repository, auditSink, lockRegistry);
        var entry = await globalService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var effective = await tenantService.GetEffectiveAsync(entry.Key, TestContext.Current.CancellationToken);

        Assert.NotNull(effective);
        Assert.Equal(CertificationStatus.Certified, effective!.Status);
        Assert.Null(effective.TenantId); // this IS the global entry, not a copy
    }

    [Fact]
    public async Task ListTenantOverridesAsync_throws_when_no_tenant_context_is_resolved()
    {
        var service = BuildService(tenant: null, out _, out _);

        await Assert.ThrowsAsync<TenantContextMissingException>(
            () => service.ListTenantOverridesAsync(TestContext.Current.CancellationToken));
    }

    // --- BIO-DEV-002-AC02: the global matrix surfaces every status, including Unsupported ---

    [Fact]
    public async Task ListGlobalAsync_surfaces_an_unsupported_entry_alongside_every_other_status()
    {
        var service = BuildService(tenant: null, out _, out _);
        await service.RegisterGlobalAsync(SampleRequest() with { Model = "ReaderCertified", Status = CertificationStatus.Certified }, TestContext.Current.CancellationToken);
        await service.RegisterGlobalAsync(SampleRequest() with { Model = "ReaderUnsupported", Status = CertificationStatus.Unsupported }, TestContext.Current.CancellationToken);

        var all = await service.ListGlobalAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.Model == "ReaderUnsupported" && e.Status == CertificationStatus.Unsupported);
    }

    // --- BIO-DEV-002-AC04: status/evidence changes are audited ---

    [Fact]
    public async Task UpdateGlobalAsync_increments_version_updates_status_and_records_an_audit_event()
    {
        var service = BuildService(tenant: null, out _, out var auditSink);
        var entry = await service.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var updateRequest = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Deprecated, "https://evidence.internal/reports/rev-2",
            ExpectedVersion: 1, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);

        var updated = await service.UpdateGlobalAsync(updateRequest, TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(CertificationStatus.Deprecated, updated!.Status);
        Assert.Equal(2, updated.Version);

        var recorded = Assert.Single(auditSink.Events, e => e.Action == "device_certification.updated");
        Assert.Equal(entry.Key.Composite, recorded.Target.Id);
        Assert.Equal("usr_admin", recorded.Actor.Id);
    }

    [Fact]
    public async Task UpdateGlobalAsync_returns_null_for_an_unknown_combination()
    {
        var service = BuildService(tenant: null, out _, out var auditSink);
        var request = new DeviceCertificationStatusChangeRequest(
            "Nobody", "Unknown", "0", "0", CertificationStatus.Unsupported, "n/a", 1, "user", "usr_admin", null);

        var result = await service.UpdateGlobalAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(auditSink.Events); // an operation against an entry that never existed is not audit-worthy
    }

    [Fact]
    public async Task UpdateGlobalAsync_throws_on_a_stale_expected_version_and_records_no_audit_event()
    {
        var service = BuildService(tenant: null, out _, out var auditSink);
        var entry = await service.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);
        var staleRequest = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Deprecated, "n/a", ExpectedVersion: 999, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);

        var ex = await Assert.ThrowsAsync<DeviceCertificationConcurrencyConflictException>(
            () => service.UpdateGlobalAsync(staleRequest, TestContext.Current.CancellationToken));

        Assert.Equal(1, ex.CurrentVersion);
        Assert.Single(auditSink.Events); // only the original registration, not the rejected update
    }

    [Fact]
    public async Task UpdateTenantOverrideAsync_throws_when_no_tenant_context_is_resolved()
    {
        var service = BuildService(tenant: null, out _, out _);
        var request = new DeviceCertificationStatusChangeRequest(
            "Acme", "ReaderX", "1.2.3", "sdk-2.0", CertificationStatus.Unsupported, "n/a", 1, "user", "usr_admin", null);

        await Assert.ThrowsAsync<TenantContextMissingException>(
            () => service.UpdateTenantOverrideAsync(request, TestContext.Current.CancellationToken));
    }

    // NOTE on the tests below: DeviceCertificationService.RegisterAsync is ITSELF audited
    // (unlike Features.DeviceRegistry.DeviceService.RegisterAsync, which is not - only its
    // ChangeStatusAsync is). Constructing a service with a failing/blocking sink and using
    // THAT SAME service to also perform the setup registration would make the setup call
    // itself fail (or, for the gated sink, hang forever, since nothing calls Release() until
    // after registration would need to have already returned) - a real bug an earlier draft
    // of this file had, caught by actually running these tests, not by inspection. Each test
    // below registers through a separate "setup" service using a normal recording sink, then
    // builds the service under test with the special sink, sharing the same repository AND
    // lock registry (a real DI singleton would be shared the same way) so the update call
    // being tested is the only one that ever reaches the special sink.

    [Fact]
    public async Task UpdateGlobalAsync_propagates_when_the_audit_sink_is_unavailable_and_never_touches_the_repository()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var setupService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new RecordingCertificationAuditSink(), lockRegistry);
        var entry = await setupService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var service = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new ThrowingCertificationAuditSink(), lockRegistry);
        var request = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Deprecated, "n/a", ExpectedVersion: 1, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);

        await Assert.ThrowsAsync<AuditUnavailableException>(
            () => service.UpdateGlobalAsync(request, TestContext.Current.CancellationToken));

        var afterFailure = await setupService.GetGlobalAsync(entry.Key, TestContext.Current.CancellationToken);
        Assert.Equal(1, afterFailure!.Version); // repository never touched
        Assert.Equal(CertificationStatus.Certified, afterFailure.Status);
    }

    [Fact]
    public async Task UpdateGlobalAsync_treats_a_confirmed_present_event_as_success_despite_RecordAsync_throwing()
    {
        // ADR-0003 Decision 7, element 4.
        var repository = new InMemoryDeviceCertificationRepository();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var setupService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new RecordingCertificationAuditSink(), lockRegistry);
        var entry = await setupService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var service = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new AmbiguousThenPresentCertificationAuditSink(), lockRegistry);
        var request = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Deprecated, "n/a", ExpectedVersion: 1, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);

        var updated = await service.UpdateGlobalAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(CertificationStatus.Deprecated, updated!.Status);
    }

    [Fact]
    public async Task UpdateGlobalAsync_throws_AuditUnavailableException_when_verification_itself_is_a_total_outage()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var setupService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new RecordingCertificationAuditSink(), lockRegistry);
        var entry = await setupService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var service = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new TotalOutageCertificationAuditSink(), lockRegistry);
        var request = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Deprecated, "n/a", ExpectedVersion: 1, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);

        await Assert.ThrowsAsync<AuditUnavailableException>(
            () => service.UpdateGlobalAsync(request, TestContext.Current.CancellationToken));

        var afterFailure = await setupService.GetGlobalAsync(entry.Key, TestContext.Current.CancellationToken);
        Assert.Equal(1, afterFailure!.Version);
    }

    [Fact]
    public async Task UpdateGlobalAsync_genuinely_blocks_a_second_writer_until_the_first_releases_the_lock()
    {
        // Mirrors DeviceServiceTests' equivalent regression for BIO-DEV-001-QA-02: proves
        // real interleaving via a gated sink rather than timing-dependent ordering.
        var repository = new InMemoryDeviceCertificationRepository();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var setupService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new RecordingCertificationAuditSink(), lockRegistry);
        var entry = await setupService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var recordingSink = new RecordingCertificationAuditSink();
        var gatedSink = new GatedCertificationAuditSink(recordingSink);
        var service = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, gatedSink, lockRegistry);

        var firstRequest = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Supported, "n/a", ExpectedVersion: 1, ActorType: "user", ActorId: "usr_first", CorrelationId: null);
        var secondRequest = firstRequest with { Status = CertificationStatus.Deprecated, ActorId = "usr_second" };

        var first = service.UpdateGlobalAsync(firstRequest, TestContext.Current.CancellationToken);
        await gatedSink.Entered;

        var second = service.UpdateGlobalAsync(secondRequest, TestContext.Current.CancellationToken);
        var raced = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        Assert.NotSame(second, raced);

        gatedSink.Release();
        var firstResult = await first;
        Assert.NotNull(firstResult);
        Assert.Equal(CertificationStatus.Supported, firstResult!.Status);

        await Assert.ThrowsAsync<DeviceCertificationConcurrencyConflictException>(() => second);
        Assert.Single(recordingSink.Events, e => e.Action == "device_certification.updated"); // never two
    }

    [Fact]
    public async Task UpdateGlobalAsync_completes_the_repository_write_even_if_the_callers_token_is_cancelled_right_after_the_audit_write_succeeds()
    {
        // Regression mirroring BIO-DEV-001-QA-03.
        var repository = new InMemoryDeviceCertificationRepository();
        var lockRegistry = new DeviceCertificationLockRegistry();
        var setupService = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, new RecordingCertificationAuditSink(), lockRegistry);
        var entry = await setupService.RegisterGlobalAsync(SampleRequest(), TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        var auditSink = new CancelingCertificationAuditSink(cts);
        var service = new Biolog.Api.Features.DeviceCertification.DeviceCertificationService(
            new TenantContextAccessor(null), repository, auditSink, lockRegistry);
        var request = new DeviceCertificationStatusChangeRequest(
            entry.Vendor, entry.Model, entry.Firmware, entry.Sdk,
            CertificationStatus.Deprecated, "n/a", ExpectedVersion: 1, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);

        var updated = await service.UpdateGlobalAsync(request, cts.Token);

        Assert.NotNull(updated);
        Assert.Equal(CertificationStatus.Deprecated, updated!.Status); // the write landed despite the cancellation
        Assert.Equal(2, updated.Version);
    }
}
