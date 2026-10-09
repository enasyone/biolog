using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-TRN-002's real
/// cross-story dependency on BIO-TRN-001 (route/vehicle/stop existence and active-status,
/// through <c>Biolog.Api.Modules.BioTrn002Module</c>'s own <c>TransportRegistryGatewayAdapter</c>)
/// and on BIO-IDN-001 (person existence) are enforced by the real, shipped host channel - not
/// merely injected test doubles. Registers a real domain, a real person, a real active
/// route/vehicle/stop through BIO-TRN-001's own endpoints, then exercises BIO-TRN-002's own
/// endpoints against that real data. SYNTHETIC fixtures only.
/// </summary>
public sealed class TransportEventsHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c88";

    public TransportEventsHostChannelTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "ops_admin");
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

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", host, new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    private async Task<(string RouteId, string StopId, string VehicleId)> RegisterActiveRouteStopVehicleAsync(HttpClient client, string host, CancellationToken ct)
    {
        var stopResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/stops", host, new { name = "Riverside Gate" }), ct);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        var stopId = (await stopResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var routeResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/routes", host, new
        {
            name = "Route A",
            stops = new[] { new { stop_id = stopId, sequence_order = 1 } },
        }), ct);
        Assert.Equal(HttpStatusCode.OK, routeResponse.StatusCode);
        var route = await routeResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var routeId = route.GetProperty("id").GetString()!;
        var routeVersion = route.GetProperty("version").GetInt32();

        var activateResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/transport/routes/{routeId}/activate", host, new { version = routeVersion }), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var vehicleResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport/vehicles", host, new { name = "Bus 01" }), ct);
        Assert.Equal(HttpStatusCode.OK, vehicleResponse.StatusCode);
        var vehicleId = (await vehicleResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        return (routeId, stopId, vehicleId);
    }

    private static object BoardBody(string personId, string routeId, string vehicleId, string stopId, string idempotencyKey, string? occurredAt = null) => new
    {
        person_id = personId,
        route_id = routeId,
        vehicle_id = vehicleId,
        stop_id = stopId,
        mode = "board",
        evidence = new[] { new { method = "uhf", verified = true, confidence = 0.9 } },
        occurred_at = occurredAt ?? "2026-01-01T08:00:00.000Z",
        idempotency_key = idempotencyKey,
    };

    [Fact]
    public async Task AC01_AC02_capturing_a_real_board_event_against_a_real_route_vehicle_stop_and_person_succeeds_and_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn002-capture-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        var (routeId, stopId, vehicleId) = await RegisterActiveRouteStopVehicleAsync(client, hostname, ct);

        var body = BoardBody(personId, routeId, vehicleId, stopId, "e2e-idem-1");
        var firstResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport-events/events", hostname, body), ct);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(routeId, first.GetProperty("route_id").GetString());

        // AC02 - identical redelivery answers idempotently with the SAME event id.
        var secondResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport-events/events", hostname, body), ct);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(first.GetProperty("event_id").GetString(), second.GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task AC01_capturing_against_a_route_from_a_DIFFERENT_tenant_fails_closed_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "trn002-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "trn002-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var (routeIdA, stopIdA, vehicleIdA) = await RegisterActiveRouteStopVehicleAsync(client, hostA, ct);
        // The person is registered in TENANT B (the tenant the capture request actually resolves
        // to) precisely so the person-existence check passes and the route gateway's OWN
        // tenant-scoping is what's actually being proven here, not merely that some earlier check
        // in the pipeline also happens to fail closed.
        var personIdB = await RegisterPersonAsync(client, hostB, ct);

        var body = BoardBody(personIdB, routeIdA, vehicleIdA, stopIdA, "e2e-idem-cross-tenant");
        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport-events/events", hostB, body), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // BIO-TRN-001's own tenant-scoped repository never finds tenant A's route under tenant B
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("route_not_found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC03_AC04_a_genuinely_backdated_board_with_no_alight_is_a_real_exception_and_triggers_a_readable_back_notification()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "trn002-exception-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        var (routeId, stopId, vehicleId) = await RegisterActiveRouteStopVehicleAsync(client, hostname, ct);

        var setConfigResponse = await client.SendAsync(ActorRequest(HttpMethod.Put, "/api/v1/transport-events/notification-config", hostname, new { notify_on_missing_alight_exception = true }), ct);
        Assert.Equal(HttpStatusCode.OK, setConfigResponse.StatusCode);

        var backdated = DateTime.UtcNow.AddHours(-20).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var body = BoardBody(personId, routeId, vehicleId, stopId, "e2e-idem-exception", backdated);
        var captureResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/transport-events/events", hostname, body), ct);
        Assert.Equal(HttpStatusCode.OK, captureResponse.StatusCode);

        var date = backdated[..10];
        var statusResponse = await client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/transport-events/presence-status?person_id={personId}&date={date}", hostname), ct);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("missing_alight_exception", status.GetProperty("status").GetString());

        var notificationsResponse = await client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/transport-events/triggered-notifications", hostname), ct);
        var notifications = await notificationsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = notifications.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(data, n => n.GetProperty("person_id").GetString() == personId && n.GetProperty("rule_id").GetString() == "missing_alight_exception");
    }

    [Fact]
    public async Task AC05_the_boarding_alighting_log_bridge_route_answers_a_genuinely_headerless_request_with_real_domain_data()
    {
        // web/features/transport/live.ts's fetch() sends no custom headers at all.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/transport/primary-action", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var message = body.GetProperty("message").GetString()!;
        Assert.Contains("Transport: boarding/alighting log loaded", message);
        Assert.Matches("is (no_activity|on_board|alighted|missing_alight_exception) for", message);
    }
}
