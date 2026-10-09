using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Features.DeviceRegistry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-TRN-001's gateway
/// wiring reaches BOTH real sibling stores: BIO-DEV-001's device registry (AC03's vehicle/device
/// linkage - the one gateway this feature can actually prove positively through this host, since
/// registry create/read/update/retire is open to any asserted actor) and, for the one
/// authorization decision this feature makes (assignment create/end), the same disclosed
/// "rejection path only" limitation <c>AttendanceHostChannelTests</c>/<c>VisitorHostChannelTests</c>
/// already established (this project has no <c>InternalsVisibleTo</c> seam into BIO-IDN-006 to
/// grant a real permission - the positive path is proven at the unit level with a fake permission
/// gateway, see <c>PersonAssignmentServiceTests.cs</c>).
/// </summary>
public sealed class TransportHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-trn001";

    public TransportHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Transport:ActorProvenanceSecret"] = ProvenanceSecret,
            })));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private static HttpRequestMessage DomainAdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Actor-Id", "admin_alice");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, string actorId, object? body = null, bool withProvenance = false)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", actorId);
        if (withProvenance)
        {
            request.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(actorId));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

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

    private async Task<string> RegisterRealDeviceAsync(HttpClient client, string hostname, string serial, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices") { Headers = { Host = hostname } };
        request.Content = JsonContent.Create(new DeviceRegistrationRequest(
            serial, "Acme", "Reader-100", "1.0.0", "sdk-1.0", "Main Campus", new NetworkMetadata(null, null, null), CredentialReference: null));
        var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return created.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task TRN001_AC03_creating_a_vehicle_with_a_real_device_id_through_the_real_host_reaches_the_real_BIO_DEV_001_gateway_and_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn001-device-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var deviceId = await RegisterRealDeviceAsync(client, hostname, "SN-TRN001-1", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/vehicles", hostname, "fleet_admin", new
        {
            name = "Bus 01",
            device_id = deviceId,
        }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(deviceId, body.GetProperty("device_id").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TRN001_AC03_a_device_id_that_does_not_exist_in_the_resolved_tenant_is_rejected_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn001-unknown-device-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/vehicles", hostname, "fleet_admin", new
        {
            name = "Bus 01",
            device_id = "dev_0199e0c2-9999-7999-8999-999999999999",
        }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TRN001_route_stop_lifecycle_through_the_real_host_is_open_to_any_asserted_actor_and_versioned()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn001-route-lifecycle-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var stopResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/stops", hostname, "ops_admin", new { name = "Riverside Gate" }), ct);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        var stop = await stopResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var stopId = stop.GetProperty("id").GetString();

        var routeResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/routes", hostname, "ops_admin", new
        {
            name = "Route A",
            stops = new[] { new { stop_id = stopId, sequence_order = 1 } },
        }), ct);
        Assert.Equal(HttpStatusCode.OK, routeResponse.StatusCode);
        var route = await routeResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("draft", route.GetProperty("status").GetString());
        var routeId = route.GetProperty("id").GetString();
        var version = route.GetProperty("version").GetInt32();

        var activateResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/transport/routes/{routeId}/activate", hostname, "ops_admin", new { version }), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("active", activated.GetProperty("status").GetString());
        Assert.Equal(version + 1, activated.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task TRN001_AC02_creating_an_assignment_through_the_real_host_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn001-assign-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/transport/assignments", hostname, "unprivileged_attacker",
            new { person_id = "per_0199e0c2-1111-7222-8333-444455556666", route_id = "r", stop_id = "s", valid_from = "2026-01-01T00:00:00.000Z" },
            withProvenance: true), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TRN001_creating_an_assignment_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn001-assign-provenance-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/transport/assignments", hostname, "unprivileged_attacker",
            new { person_id = "per_0199e0c2-1111-7222-8333-444455556666", route_id = "r", stop_id = "s", valid_from = "2026-01-01T00:00:00.000Z" },
            withProvenance: false), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TRN001_AC05_the_two_UI_bridge_routes_answer_a_genuinely_headerless_request_through_the_real_host()
    {
        // web/features/transport/live.ts's fetch() sends no custom headers at all - proving these
        // two routes work with NO Host/X-Actor-Id/X-Actor-Provenance is the actual AC05 proof.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var assign = await client.PostAsync("/api/v1/transport/assign-route", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assignBody = await assign.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Transport:", assignBody.GetProperty("message").GetString());

        var primary = await client.PostAsync("/api/v1/transport/primary-action", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
        var primaryBody = await primary.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Transport:", primaryBody.GetProperty("message").GetString());
    }
}
