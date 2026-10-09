using Biolog.Features.TenantRegistry;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;
using Biolog.Features.TenantRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.TenantRegistry.Tests;

/// <summary>BIO-TEN-001-AC01, AC03: tenant CRUD and status lifecycle, exercised directly against the service.</summary>
public sealed class TenantServiceTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");

    private static TenantService NewService(out InMemoryTenantAuditSink sink)
    {
        sink = new InMemoryTenantAuditSink();
        return new TenantService(new InMemoryTenantRegistry(), sink);
    }

    private static CreateTenantRequest ValidCreateRequest(string? name = "Acme University") => new()
    {
        Name = name,
        LicensePlan = "enterprise",
        LicenseSeats = 500,
        RoutingRegion = "eu-west",
    };

    [Fact]
    public async Task Create_mints_a_canonical_tenant_id_and_defaults_status_to_active()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var tenant = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        Assert.True(CanonicalId.IsTenantId(tenant.Id));
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(1, tenant.Version);
        Assert.Equal(tenant.CreatedAt, tenant.UpdatedAt);
    }

    [Fact]
    public async Task Create_persists_the_tenant_so_it_can_be_read_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var fetched = await service.GetAsync(created.Id, ct);

        Assert.NotNull(fetched);
        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task Create_rejects_a_missing_name_as_a_validation_error_not_a_500()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.CreateAsync(ValidCreateRequest(name: null), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(422, error.Status);
        Assert.Contains(error.Errors!, e => e.Path == "name" && e.Code == "required");
    }

    [Fact]
    public async Task Create_rejects_a_malformed_routing_region()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var request = ValidCreateRequest() with { RoutingRegion = "Not A Valid Region!" };

        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.CreateAsync(request, Actor, CanonicalId.NewEventId(), ct));

        Assert.Contains(error.Errors!, e => e.Path == "routing_region");
    }

    [Fact]
    public async Task Create_rejects_negative_license_seats()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var request = ValidCreateRequest() with { LicenseSeats = -1 };

        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.CreateAsync(request, Actor, CanonicalId.NewEventId(), ct));

        Assert.Contains(error.Errors!, e => e.Path == "license_seats" && e.Code == "out_of_range");
    }

    [Fact]
    public async Task Get_of_an_unknown_id_returns_null_not_a_throw()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var result = await service.GetAsync(CanonicalId.NewTenantId(), ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task Update_increments_version_and_changes_only_named_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateTenantRequest { Version = created.Version, Name = "Acme University (Renamed)" },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal("Acme University (Renamed)", updated.Name);
        Assert.Equal(created.LicensePlan, updated.LicensePlan); // untouched field survives
        Assert.Equal(created.Version + 1, updated.Version);
        // Not asserting updated_at != created_at: the wire format is millisecond-precision
        // (ADR-0001 §2.1) and this test's create+update can legitimately land in the same
        // millisecond, so equality here is not evidence of a bug.
    }

    [Fact]
    public async Task Update_with_a_stale_version_fails_closed_as_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<TenantRegistryError>(() => service.UpdateAsync(
            created.Id, new UpdateTenantRequest { Version = created.Version + 41, Name = "x" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Update_of_an_unknown_tenant_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var error = await Assert.ThrowsAsync<TenantRegistryError>(() => service.UpdateAsync(
            CanonicalId.NewTenantId(), new UpdateTenantRequest { Version = 1, Name = "x" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("tenant_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Disabled)]
    public async Task UpdateStatus_transitions_between_the_three_documented_states(TenantStatus target)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var updated = await service.UpdateStatusAsync(
            created.Id, new UpdateTenantStatusRequest { Version = created.Version, Status = target.ToWireString() },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(target, updated.Status);
        Assert.Equal(created.Version + 1, updated.Version);
    }

    [Fact]
    public async Task UpdateStatus_rejects_a_value_outside_the_three_documented_states()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<TenantRegistryError>(() => service.UpdateStatusAsync(
            created.Id, new UpdateTenantStatusRequest { Version = created.Version, Status = "pending_deletion" },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(error.Errors!, e => e.Path == "status");
    }

    [Fact]
    public async Task UpdateStatus_to_the_current_status_is_a_no_op_and_does_not_bump_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var result = await service.UpdateStatusAsync(
            created.Id, new UpdateTenantStatusRequest { Version = created.Version, Status = "active" }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(created.Version, result.Version);
    }

    [Fact]
    public async Task SoftDelete_disables_rather_than_removing_the_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var created = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var deleted = await service.SoftDeleteAsync(created.Id, created.Version, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(TenantStatus.Disabled, deleted.Status);
        var stillReadable = await service.GetAsync(created.Id, ct);
        Assert.NotNull(stillReadable); // never physically removed
        Assert.Equal(TenantStatus.Disabled, stillReadable!.Status);
    }

    [Fact]
    public async Task List_pages_through_more_tenants_than_one_page_holds()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        for (var i = 0; i < 5; i++)
        {
            await service.CreateAsync(ValidCreateRequest($"Tenant {i}"), Actor, CanonicalId.NewEventId(), ct);
        }

        var (firstPage, cursor) = await service.ListAsync(limit: 2, cursor: null, statusFilterRaw: null, ct);
        Assert.Equal(2, firstPage.Count);
        Assert.NotNull(cursor);

        var (secondPage, cursor2) = await service.ListAsync(limit: 2, cursor: cursor, statusFilterRaw: null, ct);
        Assert.Equal(2, secondPage.Count);
        Assert.NotNull(cursor2);
        Assert.DoesNotContain(secondPage, t => firstPage.Any(f => f.Id == t.Id)); // no overlap

        var (thirdPage, cursor3) = await service.ListAsync(limit: 2, cursor: cursor2, statusFilterRaw: null, ct);
        Assert.Single(thirdPage);
        Assert.Null(cursor3); // exhausted
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var active = await service.CreateAsync(ValidCreateRequest("Active Tenant"), Actor, CanonicalId.NewEventId(), ct);
        var toSuspend = await service.CreateAsync(ValidCreateRequest("Suspended Tenant"), Actor, CanonicalId.NewEventId(), ct);
        await service.UpdateStatusAsync(
            toSuspend.Id, new UpdateTenantStatusRequest { Version = toSuspend.Version, Status = "suspended" }, Actor, CanonicalId.NewEventId(), ct);

        var (activeOnly, _) = await service.ListAsync(limit: 50, cursor: null, statusFilterRaw: "active", ct);

        Assert.Contains(activeOnly, t => t.Id == active.Id);
        Assert.DoesNotContain(activeOnly, t => t.Id == toSuspend.Id);
    }

    [Fact]
    public async Task List_rejects_a_cursor_it_did_not_issue()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.ListAsync(limit: 10, cursor: "not-a-real-cursor", statusFilterRaw: null, ct));

        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(error.Errors!, e => e.Path == "cursor");
    }

    [Fact]
    public async Task List_rejects_a_limit_outside_the_documented_bounds()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(out _);
        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.ListAsync(limit: TenantService.MaxLimit + 1, cursor: null, statusFilterRaw: null, ct));

        Assert.Contains(error.Errors!, e => e.Path == "limit");
    }
}
