using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-IDN-004's
/// write-capable gateway onto BIO-IDN-001's real <c>PersonService.UpdateStatusAsync</c> works
/// correctly - not merely against an injected fake. Mirrors every sibling story's own
/// <c>*HostChannelTests.cs</c>: registers and verifies a real domain through BIO-TEN-002's own
/// endpoints, registers a real person through BIO-IDN-001's own endpoint, then exercises
/// BIO-IDN-004's endpoints through that verified hostname.
/// </summary>
public sealed class PersonLifecycleHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    public PersonLifecycleHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null, string? permission = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "registrar_ada");
        if (permission is not null)
        {
            request.Headers.Add("X-Actor-Permission", permission);
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

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons", host, new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    private async Task AdvanceToActiveAsync(HttpClient client, string host, string personId, CancellationToken ct)
    {
        var verify = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", host, new { target_status = "VERIFIED", version = 1 }), ct);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var activate = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", host, new { target_status = "ACTIVE", version = 2 }), ct);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
    }

    [Fact]
    public async Task Emergency_suspension_through_a_verified_hostname_resolves_the_real_tenant_and_reaches_BIO_IDN_001_s_real_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "lifecycle-suspend-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        await AdvanceToActiveAsync(client, hostname, personId, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", hostname, new { target_status = "SUSPENDED", version = 3 }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("SUSPENDED", body.GetProperty("status").GetString());
        Assert.Equal(TenantA, body.GetProperty("tenant_id").GetString());
    }

    [Fact]
    public async Task Reactivation_through_the_real_host_requires_the_permission_header()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "lifecycle-reactivate-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        await AdvanceToActiveAsync(client, hostname, personId, ct);
        await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", hostname, new { target_status = "SUSPENDED", version = 3 }), ct);

        var withoutPermission = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", hostname, new { target_status = "ACTIVE", version = 4, reason = "cleared" }), ct);
        Assert.Equal(HttpStatusCode.Forbidden, withoutPermission.StatusCode);

        var withPermission = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", hostname, new { target_status = "ACTIVE", version = 4, reason = "cleared" },
            permission: "person.reactivate"), ct);
        Assert.Equal(HttpStatusCode.OK, withPermission.StatusCode);
    }

    [Fact]
    public async Task BIOIDN004_a_tenant_B_caller_cannot_suspend_a_real_tenant_A_person()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "lifecycle-real-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "lifecycle-real-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var personId = await RegisterPersonAsync(client, hostA, ct);
        await AdvanceToActiveAsync(client, hostA, personId, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/lifecycle/transitions", hostB, new { target_status = "SUSPENDED", version = 3 }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transitioning_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d/lifecycle/transitions",
            "nobody-registered-this-lifecycle-host.example.edu.ng", new { target_status = "SUSPENDED", version = 1 }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unrelated_endpoint_still_succeeds_under_an_arbitrary_Host_header()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live") { Headers = { Host = "whatever-lifecycle-e2e.example.edu.ng" } };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
