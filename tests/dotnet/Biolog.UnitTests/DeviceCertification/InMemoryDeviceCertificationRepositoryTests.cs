using Biolog.Api.Features.DeviceCertification;
using Xunit;

namespace Biolog.UnitTests.DeviceCertification;

public class InMemoryDeviceCertificationRepositoryTests
{
    private static DeviceCertificationRegistrationRequest SampleRequest(string? actorId = "usr_ops") => new(
        Vendor: "Acme",
        Model: "ReaderX",
        Firmware: "1.2.3",
        Sdk: "sdk-2.0",
        Status: CertificationStatus.Certified,
        TestEvidenceReference: "https://evidence.internal/reports/acme-readerx-1.2.3",
        ActorType: "user",
        ActorId: actorId!,
        CorrelationId: null);

    [Fact]
    public async Task CreateAsync_stores_the_entry_starting_at_version_one()
    {
        var repository = new InMemoryDeviceCertificationRepository();

        var entry = await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(1, entry.Version);
        Assert.Null(entry.TenantId);
        Assert.Equal(CertificationStatus.Certified, entry.Status);
    }

    [Fact]
    public async Task CreateAsync_throws_when_an_entry_already_exists_for_the_same_key_and_scope()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DeviceCertificationAlreadyExistsException>(
            () => repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_allows_the_same_key_independently_for_the_global_scope_and_a_tenant_override()
    {
        // BIO-DEV-002-AC03: a tenant's own override and the platform-wide default for the
        // identical vendor+model+firmware+sdk combination are different aggregates.
        var repository = new InMemoryDeviceCertificationRepository();
        await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);

        var overrideEntry = await repository.CreateAsync("ten_asu", SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("ten_asu", overrideEntry.TenantId);
        Assert.Equal(1, overrideEntry.Version);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_key_containing_a_reserved_separator_even_when_the_service_layer_is_bypassed()
    {
        // Mirrors BIO-DEV-001-QA-04's lesson: a caller reaching the repository directly,
        // bypassing DeviceCertificationService entirely, must still be rejected.
        var repository = new InMemoryDeviceCertificationRepository();
        var request = SampleRequest() with { Vendor = "Acme/Sub" };

        await Assert.ThrowsAsync<InvalidDeviceCertificationKeyException>(
            () => repository.CreateAsync(null, request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_key()
    {
        var repository = new InMemoryDeviceCertificationRepository();

        var result = await repository.GetAsync(null, new DeviceCertificationKey("Nobody", "Unknown", "0", "0"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_returns_null_for_an_entry_that_does_not_exist()
    {
        var repository = new InMemoryDeviceCertificationRepository();

        var result = await repository.UpdateAsync(
            null, new DeviceCertificationKey("Nobody", "Unknown", "0", "0"),
            CertificationStatus.Unsupported, "n/a", expectedVersion: 1, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_increments_version_and_applies_the_new_status_and_evidence()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var created = await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);

        var updated = await repository.UpdateAsync(
            null, created.Key, CertificationStatus.Deprecated, "https://evidence.internal/reports/rev-2",
            expectedVersion: 1, TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Version);
        Assert.Equal(CertificationStatus.Deprecated, updated.Status);
        Assert.Equal("https://evidence.internal/reports/rev-2", updated.TestEvidenceReference);
    }

    [Fact]
    public async Task UpdateAsync_throws_on_a_stale_expected_version()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var created = await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<DeviceCertificationConcurrencyConflictException>(() => repository.UpdateAsync(
            null, created.Key, CertificationStatus.Deprecated, "n/a", expectedVersion: 999, TestContext.Current.CancellationToken));

        Assert.Equal(1, ex.CurrentVersion);
    }

    [Fact]
    public async Task UpdateAsync_to_the_global_entry_does_not_affect_a_tenants_own_override()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        var globalEntry = await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);
        var overrideEntry = await repository.CreateAsync(
            "ten_asu", SampleRequest() with { Status = CertificationStatus.Experimental }, TestContext.Current.CancellationToken);

        await repository.UpdateAsync(
            null, globalEntry.Key, CertificationStatus.Unsupported, "n/a", expectedVersion: 1, TestContext.Current.CancellationToken);

        var overrideAfter = await repository.GetAsync("ten_asu", overrideEntry.Key, TestContext.Current.CancellationToken);
        Assert.Equal(CertificationStatus.Experimental, overrideAfter!.Status);
        Assert.Equal(1, overrideAfter.Version);
    }

    [Fact]
    public async Task ListAsync_only_one_of_two_concurrent_writers_with_the_same_expected_version_wins()
    {
        // Mirrors InMemoryDeviceRepositoryTests' equivalent regression: the compare-and-set
        // must be atomic against the store itself, not a version read earlier.
        var repository = new InMemoryDeviceCertificationRepository();
        var created = await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(
            Attempt(repository, created.Key, CertificationStatus.Supported),
            Attempt(repository, created.Key, CertificationStatus.Deprecated));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.Equal(1, results.Count(r => !r.Succeeded));

        var final = await repository.GetAsync(null, created.Key, TestContext.Current.CancellationToken);
        Assert.Equal(2, final!.Version);

        static async Task<(bool Succeeded, Exception? Error)> Attempt(
            InMemoryDeviceCertificationRepository repo, DeviceCertificationKey key, CertificationStatus status)
        {
            try
            {
                await repo.UpdateAsync(null, key, status, "n/a", expectedVersion: 1, TestContext.Current.CancellationToken);
                return (true, null);
            }
            catch (DeviceCertificationConcurrencyConflictException ex)
            {
                return (false, ex);
            }
        }
    }

    [Fact]
    public async Task ListAsync_returns_only_entries_in_the_requested_scope()
    {
        var repository = new InMemoryDeviceCertificationRepository();
        await repository.CreateAsync(null, SampleRequest(), TestContext.Current.CancellationToken);
        await repository.CreateAsync("ten_asu", SampleRequest() with { Model = "ReaderY" }, TestContext.Current.CancellationToken);

        var globalList = await repository.ListAsync(null, TestContext.Current.CancellationToken);
        var tenantList = await repository.ListAsync("ten_asu", TestContext.Current.CancellationToken);

        Assert.Single(globalList);
        Assert.Single(tenantList);
        Assert.All(globalList, e => Assert.Null(e.TenantId));
        Assert.All(tenantList, e => Assert.Equal("ten_asu", e.TenantId));
    }
}
