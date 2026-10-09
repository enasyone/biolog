using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Xunit;

namespace Biolog.Features.Integrations.Tests;

public sealed class ImportMappingServiceTests
{
    private const string TenantId = "ten_" + "00000000-0000-7000-8000-000000000001";
    private static readonly AuditActor Actor = new(ActorType.User, "user-1");
    private static readonly string CorrelationId = Identifiers.CanonicalId.NewEventId();

    private static ImportMappingService Build(out InMemoryImportAuditSink auditSink)
    {
        auditSink = new InMemoryImportAuditSink();
        return new ImportMappingService(
            new FakeCurrentTenantProvider(TenantId),
            new InMemoryImportSourceMappingRepository(),
            auditSink,
            new ImportLockRegistry(),
            new FakePermissionGateway());
    }

    private static IReadOnlyList<FieldMapping> DefaultFieldMappings() =>
        [new FieldMapping("ext_first_name", "first_name"), new FieldMapping("ext_last_name", "last_name")];

    // --- AC01: source mappings are versioned ---

    [Fact]
    public async Task CreateAsync_a_new_mapping_starts_at_version_1()
    {
        var service = Build(out _);
        var mapping = await service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS", DefaultFieldMappings()),
            Actor, CorrelationId);

        Assert.Equal(1, mapping.Version);
    }

    [Fact]
    public async Task UpdateAsync_a_successful_update_increments_the_version()
    {
        var service = Build(out _);
        var mapping = await service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS", DefaultFieldMappings()),
            Actor, CorrelationId);

        var updated = await service.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, SyncDirection.SourceIsMaster, "Main SIS v2", DefaultFieldMappings(), true),
            Actor, CorrelationId);

        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task UpdateAsync_a_stale_expected_version_is_rejected_with_concurrency_conflict()
    {
        var service = Build(out _);
        var mapping = await service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS", DefaultFieldMappings()),
            Actor, CorrelationId);

        await service.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, SyncDirection.SourceIsMaster, "Main SIS v2", DefaultFieldMappings(), true),
            Actor, CorrelationId);

        var error = await Assert.ThrowsAsync<ImportError>(() => service.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version /* stale: 1, current is now 2 */, SyncDirection.SourceIsMaster, "Main SIS v3", DefaultFieldMappings(), true),
            Actor, CorrelationId));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_field_mapping_targeting_an_unknown_person_field()
    {
        var service = Build(out _);
        var error = await Assert.ThrowsAsync<ImportError>(() => service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS",
                [new FieldMapping("ext_status", "status")]),
            Actor, CorrelationId));

        Assert.Equal("validation_failed", error.Code);
    }

    // --- AC02: BIOLOG can be master or downstream per integration ---

    [Fact]
    public async Task CreateAsync_persists_the_configured_sync_direction()
    {
        var service = Build(out _);
        var mapping = await service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Erp, SyncDirection.BiologIsMaster, "ERP (BIOLOG is master)", DefaultFieldMappings()),
            Actor, CorrelationId);

        Assert.Equal(SyncDirection.BiologIsMaster, mapping.SyncDirection);
    }

    // --- Permission gating (ADR-0006) ---

    [Fact]
    public async Task CreateAsync_without_the_manage_permission_is_rejected()
    {
        var auditSink = new InMemoryImportAuditSink();
        var service = new ImportMappingService(
            new FakeCurrentTenantProvider(TenantId), new InMemoryImportSourceMappingRepository(), auditSink,
            new ImportLockRegistry(), new FakePermissionGateway(new HashSet<(string, string)>()));

        var error = await Assert.ThrowsAsync<ImportError>(() => service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS", DefaultFieldMappings()),
            Actor, CorrelationId));

        Assert.Equal("permission_required", error.Code);
    }

    // --- Tenant isolation ---

    [Fact]
    public async Task GetAsync_a_mapping_from_a_different_tenant_returns_null()
    {
        var service = Build(out _);
        var mapping = await service.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS", DefaultFieldMappings()),
            Actor, CorrelationId);

        // Same repository instance, a different tenant context - ADR-0001 S1.5: a cross-tenant
        // lookup returns null, never a distinct "exists but forbidden" signal.
        var repository = new InMemoryImportSourceMappingRepository();
        await repository.CreateAsync(mapping);
        var crossTenantRead = await repository.GetAsync("ten_00000000-0000-7000-8000-000000000002", mapping.Id);
        Assert.Null(crossTenantRead);
    }
}
