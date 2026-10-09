using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Features.OfflineSync.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-MOB-003's
/// central offline-intake endpoint really resolves a submitting device's tenant from BIO-DEV-001's
/// own already-registered <c>DeviceService</c> - not a fake gateway. Registers a real tenant
/// domain and a real device through their own endpoints, activates the device, then submits an
/// offline mutation and confirms it is accepted into the tenant the device is REALLY registered
/// to. SYNTHETIC fixtures only.
///
/// <b>Device credential seeding.</b> Mirrors <c>PhotoBoothHostChannelTests</c>' own pattern
/// exactly (see that file's header for the full rationale): BIO-DEV-001 mints its own
/// <c>dev_</c> id server-side, so a device's id cannot be known before registration, which
/// makes seeding its secret through <see cref="DeviceCredentialProvisioning.LoadFromConfiguration"/>
/// (fixed at host-build time) impossible within one HTTP-only flow. This test seeds the
/// credential directly against the shared <see cref="InMemoryDeviceCredentialStore"/> singleton
/// via DI once the real id is known - never through HTTP.
/// </summary>
public sealed class OfflineSyncHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string DeviceSecret = "offline-sync-host-channel-secret";

    public OfflineSyncHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static HttpRequestMessage DomainAdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Actor-Id", "admin_alice");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<string> RegisterAndVerifyDomainAsync(HttpClient client, string hostname, string tenantId, CancellationToken ct)
    {
        var registerResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname, tenant_id = tenantId }), ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = registered.GetProperty("version").GetInt32();

        var verifyResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Put, $"/api/v1/control-plane/tenant-domains/{hostname}/status", new { version, status = "VERIFIED" }), ct);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        return hostname;
    }

    private async Task<(string DeviceId, string TenantId)> RegisterAndActivateDeviceAsync(HttpClient client, string host, CancellationToken ct)
    {
        var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices") { Headers = { Host = host } };
        registerRequest.Content = JsonContent.Create(new DeviceRegistrationRequest(
            "SN-MOB-SYNC-1", "Acme", "MobileSyncTester", "1.0.0", "sdk-1", "Main Campus",
            new NetworkMetadata("10.0.0.9", null, "mob-sync-01"), CredentialReference: "kv://synthetic/mob-sync-01"));
        var registerResponse = await client.SendAsync(registerRequest, ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var device = await registerResponse.Content.ReadFromJsonAsync<Device>(ct);

        var activateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/devices/{device!.Id}/status") { Headers = { Host = host } };
        activateRequest.Content = JsonContent.Create(new DeviceStatusChangeRequest(DeviceStatus.Active, device.Version, "user", "admin_alice", CorrelationId: null));
        var activateResponse = await client.SendAsync(activateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        _factory.Services.GetRequiredService<InMemoryDeviceCredentialStore>().Register(device.Id, DeviceSecret);
        return (device.Id, device.TenantId);
    }

    private static string NewMutationId() => $"evt_{Guid.CreateVersion7():D}";

    [Fact]
    public async Task An_offline_mutation_from_a_really_registered_device_is_accepted_into_its_real_tenant()
    {
        var client = _factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var host = "mob-sync-host-channel.biolog.test";
        var tenantId = "ten_0199e0c1-7000-7000-8000-000000000001";

        await RegisterAndVerifyDomainAsync(client, host, tenantId, ct);
        var (deviceId, resolvedTenantId) = await RegisterAndActivateDeviceAsync(client, host, ct);
        Assert.Equal(tenantId, resolvedTenantId);

        var mutationId = NewMutationId();
        var aggregateId = "per_0199e0c1-7000-7000-8000-000000000002";
        var idempotencyKey = $"idem-{mutationId}";
        var submit = new HttpRequestMessage(HttpMethod.Post, "/api/v1/offline-mutations");
        submit.Headers.Add("X-Biolog-Device-Id", deviceId);
        submit.Headers.Add("X-Biolog-Device-Secret", DeviceSecret);
        submit.Headers.Add("Idempotency-Key", idempotencyKey);
        submit.Content = JsonContent.Create(new
        {
            mutation_id = mutationId,
            tenant_id = tenantId,
            device_id = deviceId,
            aggregate_id = aggregateId,
            base_version = 1,
            idempotency_key = idempotencyKey,
            occurred_at = "2026-09-23T00:00:00.000Z",
            sync_state = "UPLOADING",
            operation = "ADDITION",
            payload = new { field = "SYNTHETIC" },
        });

        var response = await client.SendAsync(submit, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(mutationId, ack.GetProperty("mutation_id").GetString());
        Assert.Equal(1, ack.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_payload_tenant_id_claim_that_disagrees_with_the_devices_real_registered_tenant_is_rejected()
    {
        var client = _factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var host = "mob-sync-mismatch-host-channel.biolog.test";
        var realTenantId = "ten_0199e0c1-7000-7000-8000-000000000003";
        var forgedTenantId = "ten_0199e0c1-7000-7000-8000-000000000004";

        await RegisterAndVerifyDomainAsync(client, host, realTenantId, ct);
        var (deviceId, _) = await RegisterAndActivateDeviceAsync(client, host, ct);

        var mutationId = NewMutationId();
        var idempotencyKey = $"idem-{mutationId}";
        var submit = new HttpRequestMessage(HttpMethod.Post, "/api/v1/offline-mutations");
        submit.Headers.Add("X-Biolog-Device-Id", deviceId);
        submit.Headers.Add("X-Biolog-Device-Secret", DeviceSecret);
        submit.Headers.Add("Idempotency-Key", idempotencyKey);
        submit.Content = JsonContent.Create(new
        {
            mutation_id = mutationId,
            tenant_id = forgedTenantId, // claims a DIFFERENT tenant than the device is really registered to
            device_id = deviceId,
            aggregate_id = "per_0199e0c1-7000-7000-8000-000000000005",
            base_version = 1,
            idempotency_key = idempotencyKey,
            occurred_at = "2026-09-23T00:00:00.000Z",
            sync_state = "UPLOADING",
            operation = "ADDITION",
            payload = new { field = "SYNTHETIC" },
        });

        var response = await client.SendAsync(submit, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_mismatch", problem.GetProperty("code").GetString());
    }
}
