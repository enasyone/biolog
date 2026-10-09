// Developer tests for BIO-RPT-003. SYNTHETIC fixtures only.
using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Xunit;

namespace Biolog.Features.AuditInvestigation.Tests;

public class AuditInvestigationServiceTests
{
    private static (AuditInvestigationService Service, FakeAuditExportPermissionGateway Gateway, InMemoryAuditInvestigationAuditSink AuditSink) Build(
        params IAuditEventSource[] sources)
    {
        var gateway = new FakeAuditExportPermissionGateway();
        var auditSink = new InMemoryAuditInvestigationAuditSink();
        var tenant = new FakeCurrentTenantProvider(TestData.TenantA);
        var service = new AuditInvestigationService(sources, gateway, tenant, auditSink);
        return (service, gateway, auditSink);
    }

    [Fact]
    public async Task AC01_filters_by_actor_type_and_id()
    {
        var source = new FakeAuditEventSource("rbac",
        [
            TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal", actorType: ActorType.User, actorId: "alice"),
            TestData.Record("rbac", "evt_2", "rbac.role_assign", "role_assigned", "2026-09-02T00:00:00.000Z", "internal", actorType: ActorType.ServiceAccount, actorId: "sync-worker"),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter { ActorType = ActorType.ServiceAccount }, "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_2", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task AC01_filters_by_person_id()
    {
        var source = new FakeAuditEventSource("card_production",
        [
            TestData.Record("card_production", "evt_1", "production.create_job", "production_job_created", "2026-09-01T00:00:00.000Z", "internal", personId: "per_alice"),
            TestData.Record("card_production", "evt_2", "production.create_job", "production_job_created", "2026-09-01T00:00:01.000Z", "internal", personId: "per_bob"),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter { PersonId = "per_bob" }, "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_2", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task AC01_filters_by_action()
    {
        var source = new FakeAuditEventSource("rbac",
        [
            TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal"),
            TestData.Record("rbac", "evt_2", "rbac.permission_grant_set", "permission_granted", "2026-09-01T00:00:01.000Z", "internal"),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter { Action = "rbac.permission_grant_set" }, "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_2", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task AC01_filters_by_device_id_against_a_synthetic_fixture()
    {
        // No source wired in production today ever populates a non-null device (verified: every
        // existing *AuditEventBuilder in this codebase sets Device = null) - this proves the
        // filter mechanism itself is correct against a fixture that DOES populate one, so it will
        // start matching real data automatically the moment any source ever does, with no code
        // change here (see docs/features/bio-rpt-003/README.md).
        var source = new FakeAuditEventSource("card_production",
        [
            TestData.Record("card_production", "evt_1", "production.print", "print_passed", "2026-09-01T00:00:00.000Z", "internal", device: new AuditDeviceRef("dev_printer_1", "station-a")),
            TestData.Record("card_production", "evt_2", "production.print", "print_passed", "2026-09-01T00:00:01.000Z", "internal", device: new AuditDeviceRef("dev_printer_2", "station-b")),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter { DeviceId = "dev_printer_2" }, "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_2", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task AC01_filters_by_reason_code()
    {
        var source = new FakeAuditEventSource("card_templates",
        [
            TestData.Record("card_templates", "evt_1", "card_template.reject", "card_template_rejected", "2026-09-01T00:00:00.000Z", "internal"),
            TestData.Record("card_templates", "evt_2", "card_template.publish", "card_template_published", "2026-09-01T00:00:01.000Z", "internal"),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter { ReasonCode = "card_template_published" }, "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_2", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task AC01_filters_by_inclusive_time_range()
    {
        var source = new FakeAuditEventSource("rbac",
        [
            TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal"),
            TestData.Record("rbac", "evt_2", "rbac.role_assign", "role_assigned", "2026-09-05T00:00:00.000Z", "internal"),
            TestData.Record("rbac", "evt_3", "rbac.role_assign", "role_assigned", "2026-09-10T00:00:00.000Z", "internal"),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(
            new AuditInvestigationFilter { OccurredFrom = "2026-09-02T00:00:00.000Z", OccurredTo = "2026-09-05T00:00:00.000Z" }, "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_2", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task Search_orders_newest_first_and_paginates()
    {
        var source = new FakeAuditEventSource("rbac",
        [
            TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal"),
            TestData.Record("rbac", "evt_2", "rbac.role_assign", "role_assigned", "2026-09-02T00:00:00.000Z", "internal"),
            TestData.Record("rbac", "evt_3", "rbac.role_assign", "role_assigned", "2026-09-03T00:00:00.000Z", "internal"),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter { Page = 1, PageSize = 2 }, "viewer-1");

        Assert.Equal(3, total);
        Assert.Equal(["evt_3", "evt_2"], records.Select(r => r.EventId));
    }

    [Fact]
    public async Task Tenant_isolation_a_record_for_a_different_tenant_never_matches()
    {
        var source = new FakeAuditEventSource("rbac",
        [
            TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal", tenantId: TestData.TenantA),
            TestData.Record("rbac", "evt_2", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:01.000Z", "internal", tenantId: TestData.TenantB),
        ]);
        var (service, _, _) = Build(source);

        var (records, total) = await service.SearchAsync(new AuditInvestigationFilter(), "viewer-1");

        Assert.Equal(1, total);
        Assert.Equal("evt_1", Assert.Single(records).EventId);
    }

    [Fact]
    public async Task AC04_search_hides_sensitive_detail_without_permission_but_keeps_classification()
    {
        var source = new FakeAuditEventSource("card_production",
        [
            TestData.Record("card_production", "evt_1", "production.create_job", "production_job_created", "2026-09-01T00:00:00.000Z", "personal",
                detail: new Dictionary<string, object?> { ["person_id"] = "per_alice" }),
        ]);
        var (service, _, _) = Build(source);

        var (records, _) = await service.SearchAsync(new AuditInvestigationFilter(), "viewer-without-permission");

        var record = Assert.Single(records);
        Assert.Empty(record.Detail);
        Assert.Equal("personal", record.Classification);
    }

    [Fact]
    public async Task AC04_search_reveals_sensitive_detail_when_the_viewer_holds_the_permission()
    {
        var source = new FakeAuditEventSource("card_production",
        [
            TestData.Record("card_production", "evt_1", "production.create_job", "production_job_created", "2026-09-01T00:00:00.000Z", "personal",
                detail: new Dictionary<string, object?> { ["person_id"] = "per_alice" }),
        ]);
        var (service, gateway, _) = Build(source);
        gateway.Grant("viewer-with-permission", AuditPermissionKeys.ViewSensitiveDetail);

        var (records, _) = await service.SearchAsync(new AuditInvestigationFilter(), "viewer-with-permission");

        Assert.Single(Assert.Single(records).Detail);
    }

    [Fact]
    public async Task GetById_returns_null_for_an_unknown_event_id()
    {
        var (service, _, _) = Build(new FakeAuditEventSource("rbac", []));

        var record = await service.GetByIdAsync("evt_does_not_exist", "viewer-1");

        Assert.Null(record);
    }

    [Fact]
    public async Task GetById_applies_the_same_redaction_gate_as_search()
    {
        var source = new FakeAuditEventSource("card_production",
        [
            TestData.Record("card_production", "evt_1", "production.create_job", "production_job_created", "2026-09-01T00:00:00.000Z", "sensitive",
                detail: new Dictionary<string, object?> { ["person_id"] = "per_alice" }),
        ]);
        var (service, _, _) = Build(source);

        var record = await service.GetByIdAsync("evt_1", "viewer-without-permission");

        Assert.NotNull(record);
        Assert.Empty(record.Detail);
    }

    [Fact]
    public async Task AC03_export_throws_forbidden_without_the_export_permission()
    {
        var (service, _, _) = Build(new FakeAuditEventSource("rbac", []));

        var error = await Assert.ThrowsAsync<AuditInvestigationError>(
            () => service.ExportCsvAsync(new AuditInvestigationFilter(), new AuditActor(ActorType.User, "viewer-1"), "evt_00000000-0000-7000-8000-000000000001"));

        Assert.Equal("audit_export_forbidden", error.Code);
    }

    [Fact]
    public async Task AC03_export_returns_csv_and_writes_its_own_audit_event_before_returning()
    {
        var source = new FakeAuditEventSource("rbac",
        [
            TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal"),
        ]);
        var (service, gateway, auditSink) = Build(source);
        gateway.Grant("exporter-1", AuditPermissionKeys.Export);
        var actor = new AuditActor(ActorType.User, "exporter-1");

        var (bytes, contentType, fileName) = await service.ExportCsvAsync(new AuditInvestigationFilter(), actor, "evt_00000000-0000-7000-8000-000000000001");

        Assert.Equal("text/csv", contentType);
        Assert.EndsWith(".csv", fileName);
        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("evt_1", csv);
        Assert.Contains("\r\n", csv);

        var auditEvent = Assert.Single(auditSink.EventsForTenant(TestData.TenantA));
        Assert.Equal("audit_investigation.export", auditEvent.Action);
        Assert.Equal(1, auditEvent.Detail["row_count"]);
    }

    [Fact]
    public async Task AC03_export_applies_the_filter_and_the_redaction_gate_identically_to_search()
    {
        var source = new FakeAuditEventSource("card_production",
        [
            TestData.Record("card_production", "evt_1", "production.create_job", "production_job_created", "2026-09-01T00:00:00.000Z", "sensitive",
                personId: "per_alice", detail: new Dictionary<string, object?> { ["full_name"] = "Alice Smith" }),
            TestData.Record("card_production", "evt_2", "production.create_job", "production_job_created", "2026-09-01T00:00:01.000Z", "internal",
                personId: "per_bob"),
        ]);
        var (service, gateway, _) = Build(source);
        gateway.Grant("exporter-1", AuditPermissionKeys.Export);
        var actor = new AuditActor(ActorType.User, "exporter-1");

        var (bytes, _, _) = await service.ExportCsvAsync(new AuditInvestigationFilter { PersonId = "per_alice" }, actor, "evt_00000000-0000-7000-8000-000000000001");

        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("evt_1", csv);
        Assert.DoesNotContain("evt_2", csv);
        // per_alice itself is a safe, correlatable canonical identifier (ADR-0003: "it carries the
        // canonical identifier of the record instead") and is expected to remain visible even when
        // the SENSITIVE detail value (a person's actual name) is withheld - exporter never granted
        // view_sensitive_detail.
        Assert.Contains("per_alice", csv);
        Assert.DoesNotContain("Alice Smith", csv);
    }

    [Fact]
    public async Task Tenant_context_missing_throws_before_any_source_is_consulted()
    {
        var gateway = new FakeAuditExportPermissionGateway();
        var auditSink = new InMemoryAuditInvestigationAuditSink();
        var tenant = new FakeCurrentTenantProvider(tenantId: null);
        var service = new AuditInvestigationService([new FakeAuditEventSource("rbac", [])], gateway, tenant, auditSink);

        var error = await Assert.ThrowsAsync<AuditInvestigationError>(() => service.SearchAsync(new AuditInvestigationFilter(), "viewer-1"));

        Assert.Equal("tenant_context_missing", error.Code);
    }
}
