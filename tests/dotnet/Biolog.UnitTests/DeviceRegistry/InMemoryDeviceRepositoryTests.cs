using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Xunit;

namespace Biolog.UnitTests.DeviceRegistry;

public class InMemoryDeviceRepositoryTests
{
    private static DeviceRegistrationRequest SampleRequest() => new(
        "SN-0002", "Acme", "ReaderX", "1.0.0", "sdk-1", "Main Campus",
        new NetworkMetadata(null, null, null), CredentialReference: null);

    [Fact]
    public async Task UpdateStatusAsync_only_one_of_two_concurrent_writers_with_the_same_expected_version_wins()
    {
        // ADR-0001 §3.2's compare-and-set must be atomic against the STORE, not just a
        // version-number comparison read earlier - two callers racing on the same
        // expected_version must not both appear to succeed.
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var device = await repository.CreateAsync(tenant, SampleRequest(), TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(
            Attempt(repository, tenant, device.Id, DeviceStatus.Active),
            Attempt(repository, tenant, device.Id, DeviceStatus.Inactive));

        var succeeded = results.Count(r => r.Succeeded);
        var conflicted = results.Count(r => !r.Succeeded);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        var final = await repository.GetAsync(tenant, device.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, final!.Version);

        static async Task<(bool Succeeded, Exception? Error)> Attempt(
            InMemoryDeviceRepository repo, TenantContext tenant, string deviceId, DeviceStatus status)
        {
            try
            {
                await repo.UpdateStatusAsync(tenant, deviceId, status, expectedVersion: 1, TestContext.Current.CancellationToken);
                return (true, null);
            }
            catch (DeviceConcurrencyConflictException ex)
            {
                return (false, ex);
            }
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_returns_null_for_a_device_that_does_not_exist()
    {
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);

        var result = await repository.UpdateStatusAsync(
            tenant, "dev_00000000-0000-7000-8000-000000000000", DeviceStatus.Active, 1, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_plaintext_credential_reference_even_when_the_service_layer_is_bypassed()
    {
        // BIO-DEV-001-QA-04 (Chioma, acceptance): the acceptance probe called this
        // repository directly, bypassing DeviceService.RegisterAsync entirely, and the
        // plaintext credential still went straight into the stored record. The repository
        // itself must therefore validate too - this is the exact reproduction from
        // evidence/BIO-DEV-001/acceptance/QA-04.md, now expected to throw instead of storing.
        var repository = new InMemoryDeviceRepository();
        var tenant = new TenantContext("ten_asu", "binding-asu", []);
        var request = SampleRequest() with { CredentialReference = "synthetic-plaintext-password" };

        await Assert.ThrowsAsync<InvalidCredentialReferenceException>(
            () => repository.CreateAsync(tenant, request, TestContext.Current.CancellationToken));
    }
}
