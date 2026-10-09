using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Xunit;

namespace Biolog.UnitTests.DeviceRegistry;

public class DeviceServiceTests
{
    private static DeviceRegistrationRequest SampleRequest() => new(
        Serial: "SN-0001",
        Vendor: "Acme",
        Model: "ReaderX",
        Firmware: "1.2.3",
        Sdk: "sdk-2.0",
        Site: "Main Campus",
        Network: new NetworkMetadata("10.0.0.5", "AA:BB:CC:DD:EE:FF", "reader-01"),
        CredentialReference: "kv://synthetic/reader-01");

    private static DeviceService BuildService(
        TenantContext? tenant, out InMemoryDeviceRepository repository, out RecordingAuditSink auditSink)
    {
        repository = new InMemoryDeviceRepository();
        auditSink = new RecordingAuditSink();
        var accessor = new TenantContextAccessor(tenant);
        return new DeviceService(accessor, repository, auditSink, new DeviceLockRegistry());
    }

    [Fact]
    public async Task RegisterAsync_throws_when_no_tenant_context_is_resolved()
    {
        // BIO-DEV-001-AC04 / ADR-0002 Decision 2: no default tenant, ever.
        var service = BuildService(tenant: null, out _, out _);

        await Assert.ThrowsAsync<TenantContextMissingException>(
            () => service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_creates_a_device_with_a_canonical_id_and_starting_version()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out _);

        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.StartsWith("dev_", device.Id);
        Assert.Equal("ten_asu", device.TenantId);
        Assert.Equal(DeviceStatus.PendingActivation, device.Status);
        Assert.Equal(1, device.Version);
        Assert.Null(device.LastSeenAt);
        Assert.Equal("SN-0001", device.Serial);
        Assert.Equal("kv://synthetic/reader-01", device.CredentialReference);
    }

    [Fact]
    public async Task GetAsync_throws_when_no_tenant_context_is_resolved()
    {
        var service = BuildService(tenant: null, out _, out _);

        await Assert.ThrowsAsync<TenantContextMissingException>(
            () => service.GetAsync("dev_does-not-matter", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_device()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out _);

        var result = await service.GetAsync("dev_00000000-0000-7000-8000-000000000000", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_returns_the_device_for_its_own_tenant()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out _);
        var created = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var fetched = await service.GetAsync(created.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_a_device_registered_under_a_different_tenant()
    {
        // BIO-DEV-001-AC04, the actual cross-tenant regression: a device that genuinely
        // exists, just not for this tenant, must answer exactly like one that never
        // existed at all (ADR-0002 §4.3 - not found, never forbidden).
        var repository = new InMemoryDeviceRepository();
        var auditSink = new RecordingAuditSink();
        var tenantA = new TenantContext("ten_asu", "binding-asu", []);
        var tenantB = new TenantContext("ten_grace", "binding-grace", []);
        var lockRegistry = new DeviceLockRegistry();
        var serviceForA = new DeviceService(new TenantContextAccessor(tenantA), repository, auditSink, lockRegistry);
        var serviceForB = new DeviceService(new TenantContextAccessor(tenantB), repository, auditSink, lockRegistry);

        var device = await serviceForA.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);
        var crossTenantRead = await serviceForB.GetAsync(device.Id, TestContext.Current.CancellationToken);
        var sameTenantRead = await serviceForA.GetAsync(device.Id, TestContext.Current.CancellationToken);

        Assert.Null(crossTenantRead);
        Assert.NotNull(sameTenantRead);
    }

    [Fact]
    public async Task ChangeStatusAsync_updates_status_increments_version_and_records_an_audit_event()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out var auditSink);
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var updated = await service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(DeviceStatus.Active, updated!.Status);
        Assert.Equal(2, updated.Version);

        var recorded = Assert.Single(auditSink.Events);
        Assert.Equal("device.status_changed", recorded.Action);
        Assert.Equal("device", recorded.Target.Type);
        Assert.Equal(device.Id, recorded.Target.Id);
        Assert.Equal("ten_asu", recorded.TenantId);
        Assert.Equal(AuditOutcome.Allowed, recorded.Outcome);
        Assert.Equal("user", recorded.Actor.Type);
        Assert.Equal("usr_admin", recorded.Actor.Id);
        Assert.StartsWith("evt_", recorded.CorrelationId);
    }

    [Fact]
    public async Task ChangeStatusAsync_returns_null_for_a_device_registered_under_a_different_tenant()
    {
        var repository = new InMemoryDeviceRepository();
        var auditSink = new RecordingAuditSink();
        var tenantA = new TenantContext("ten_asu", "binding-asu", []);
        var tenantB = new TenantContext("ten_grace", "binding-grace", []);
        var lockRegistry = new DeviceLockRegistry();
        var serviceForA = new DeviceService(new TenantContextAccessor(tenantA), repository, auditSink, lockRegistry);
        var serviceForB = new DeviceService(new TenantContextAccessor(tenantB), repository, auditSink, lockRegistry);
        var device = await serviceForA.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var result = await serviceForB.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(auditSink.Events); // no audit for an operation that never touched a real record
    }

    [Fact]
    public async Task ChangeStatusAsync_throws_on_a_stale_expected_version()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out var auditSink);
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<DeviceConcurrencyConflictException>(() => service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 999,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken));

        Assert.Equal(1, ex.CurrentVersion);
        Assert.Empty(auditSink.Events); // a rejected write is not an audited status change
    }

    [Fact]
    public async Task ChangeStatusAsync_propagates_when_the_audit_sink_is_unavailable()
    {
        // ADR-0002 Decision 5 / ADR-0003: the audit sink is a hard dependency, not an
        // optional side effect a caller can silently succeed past. ThrowingAuditSink both
        // throws on RecordAsync AND confirms the event genuinely absent via ExistsAsync
        // (ADR-0003 Decision 7 element 4), so this is a real, confirmed outage, surfaced as
        // AuditUnavailableException rather than whatever exception type the sink happened
        // to throw internally.
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = new DeviceService(new TenantContextAccessor(tenant), repository, new ThrowingAuditSink(), new DeviceLockRegistry());
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AuditUnavailableException>(() => service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangeStatusAsync_never_touches_the_repository_when_the_audit_sink_is_unavailable()
    {
        // Regression for BIO-DEV-001-QA-02 (corrected): audit runs BEFORE the repository
        // commit, not after it with a rollback on failure (Kemi's ADR-0003 Decision 7
        // correction - the original commit-then-rollback design breaks under concurrent
        // writers). So an audit failure here means the repository was never written to at
        // all - the record is found completely untouched, at its ORIGINAL version, not a
        // rolled-back one.
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = new DeviceService(new TenantContextAccessor(tenant), repository, new ThrowingAuditSink(), new DeviceLockRegistry());
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AuditUnavailableException>(() => service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken));

        var afterFailure = await service.GetAsync(device.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(afterFailure);
        Assert.Equal(DeviceStatus.PendingActivation, afterFailure!.Status);
        Assert.Equal(1, afterFailure.Version);
    }

    [Fact]
    public async Task ChangeStatusAsync_treats_a_confirmed_present_event_as_success_despite_RecordAsync_throwing()
    {
        // ADR-0003 Decision 7, element 4 (Kemi, closing BIO-TEN-001's identical finding): a
        // thrown RecordAsync does not by itself prove the event never happened - a real
        // store can commit and still fail to acknowledge. AmbiguousThenPresentAuditSink
        // simulates exactly that: RecordAsync throws, but ExistsAsync confirms the event IS
        // there, so the mutation must still proceed rather than be rejected.
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = new DeviceService(
            new TenantContextAccessor(tenant), repository, new AmbiguousThenPresentAuditSink(), new DeviceLockRegistry());
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var updated = await service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(DeviceStatus.Active, updated!.Status);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task ChangeStatusAsync_throws_AuditUnavailableException_when_verification_itself_is_a_total_outage()
    {
        // ADR-0003 Decision 7, element 4: if ExistsAsync (the verification call) itself
        // throws, that is a genuine, total audit-store outage - not merely one ambiguous
        // write - and must still fail closed with nothing committed.
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = new DeviceService(
            new TenantContextAccessor(tenant), repository, new TotalOutageAuditSink(), new DeviceLockRegistry());
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AuditUnavailableException>(() => service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, TestContext.Current.CancellationToken));

        var afterFailure = await service.GetAsync(device.Id, TestContext.Current.CancellationToken);
        Assert.Equal(1, afterFailure!.Version); // repository never touched
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_plaintext_credential_reference()
    {
        // BIO-DEV-001-QA-04 (Chioma, acceptance): the request boundary must reject
        // credential material masquerading as a reference, not merely trust the name.
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out _);
        var request = SampleRequest() with { CredentialReference = "synthetic-plaintext-password" };

        await Assert.ThrowsAsync<InvalidCredentialReferenceException>(
            () => service.RegisterAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_credential_bearing_url()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out _);
        var request = SampleRequest() with { CredentialReference = "vault://svc-account@vault.internal/reader-01" };

        await Assert.ThrowsAsync<InvalidCredentialReferenceException>(
            () => service.RegisterAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_accepts_a_device_with_no_credential_reference_yet()
    {
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = BuildService(tenant, out _, out _);
        var request = SampleRequest() with { CredentialReference = null };

        var device = await service.RegisterAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(device.CredentialReference);
    }

    [Fact]
    public async Task ChangeStatusAsync_genuinely_blocks_a_second_writer_until_the_first_releases_the_lock()
    {
        // Regression for a bug in an earlier version of this fix, caught by Kemi before it
        // ever reached review, PLUS a review-flagged evidence gap in the first regression
        // test written for it: auditing before the commit is not sufficient by itself,
        // because IDeviceRepository.UpdateStatusAsync re-checks the version independently
        // at its own call time. Without DeviceLockRegistry serializing the whole
        // pre-check-audit-commit sequence per device, a second REAL writer could slip in
        // between the first writer's pre-check and its commit, leaving the first writer's
        // audit event durably claiming Outcome: Allowed for a mutation that then gets
        // rejected - a false success record, not an honest attempt (the identical finding
        // Seyi made on BIO-TEN-001's equivalent fix).
        //
        // Seyi's independent re-review found the first version of this test insufficient:
        // both calls used a synchronously-completing in-memory audit sink, so the second
        // could simply run to completion after the first happened to finish first - the
        // test passed without ever proving the lock genuinely blocks concurrent entry. This
        // version uses a gated audit sink so the second attempt is provably still pending
        // (not merely completed-after) while the first holds the lock, then confirms the
        // second only proceeds - and correctly loses - once the first releases it.
        var repository = new InMemoryDeviceRepository();
        var recordingSink = new RecordingAuditSink();
        var gatedSink = new GatedAuditSink(recordingSink);
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = new DeviceService(new TenantContextAccessor(tenant), repository, gatedSink, new DeviceLockRegistry());
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var first = service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_first"), correlationId: null, TestContext.Current.CancellationToken);

        await gatedSink.Entered; // the first call is now genuinely inside its critical section, holding the lock

        var second = service.ChangeStatusAsync(
            device.Id, DeviceStatus.Inactive, expectedVersion: 1,
            new AuditActor("user", "usr_second"), correlationId: null, TestContext.Current.CancellationToken);

        var raced = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        Assert.NotSame(second, raced); // proves genuine blocking: the second call has not even reached its own audit write yet

        gatedSink.Release();
        var firstResult = await first;
        Assert.NotNull(firstResult);
        Assert.Equal(DeviceStatus.Active, firstResult!.Status);

        await Assert.ThrowsAsync<DeviceConcurrencyConflictException>(() => second);
        Assert.Single(recordingSink.Events); // never two - the loser's pre-check rejected it before auditing
    }

    [Fact]
    public async Task ChangeStatusAsync_completes_the_repository_write_even_if_the_callers_token_is_cancelled_right_after_the_audit_write_succeeds()
    {
        // Regression for BIO-DEV-001-QA-03: InMemoryDeviceRepository.UpdateStatusAsync
        // checks the caller's cancellation token before its own compare-and-set. If that
        // token is cancelled after the audit event already succeeded but before this check
        // runs, the repository would throw OperationCanceledException without ever
        // attempting the write - leaving the SAME false-success audit record the lock was
        // built to prevent, just via cancellation instead of a second writer. The fix
        // passes CancellationToken.None to the repository call once the audit event exists.
        var repository = new InMemoryDeviceRepository();
        using var cts = new CancellationTokenSource();
        var auditSink = new CancelingAuditSink(cts);
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var service = new DeviceService(new TenantContextAccessor(tenant), repository, auditSink, new DeviceLockRegistry());
        var device = await service.RegisterAsync(SampleRequest(), TestContext.Current.CancellationToken);

        var updated = await service.ChangeStatusAsync(
            device.Id, DeviceStatus.Active, expectedVersion: 1,
            new AuditActor("user", "usr_admin"), correlationId: null, cts.Token);

        Assert.NotNull(updated);
        Assert.Equal(DeviceStatus.Active, updated!.Status); // the write landed despite the cancellation
        Assert.Equal(2, updated.Version);
    }
}
