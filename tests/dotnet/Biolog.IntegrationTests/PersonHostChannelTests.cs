using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host (Program.cs's actual
/// module list and middleware pipeline via <see cref="WebApplicationFactory{TEntryPoint}"/>),
/// that BIO-IDN-001's tenant boundary (AC02) is enforced by the REAL, shipped host channel -
/// not merely by an injected test double. ADR-0002 Decision 6: "the accommodation ends
/// per-channel, not all at once ... obligation on every W03+ tenant-plane story ... once the
/// channel it depends on ships, its positive-path evidence is upgraded to a real shipped-host
/// test." BIO-TEN-002's host channel shipped before this story started, so this is that
/// upgraded evidence, not an interim accommodation.
///
/// <c>Biolog.Features.PersonRegistry.Tests.PersonEndpointsIntegrationTests</c> covers the
/// complementary concern this file cannot cheaply cover: this feature's OWN logic (validation,
/// lifecycle transitions, cross-tenant isolation within the feature) against a fixed fake
/// tenant provider. Neither file alone proves both "the logic is correct" and "the logic is
/// what actually runs in the shipped host, driven by a real verified hostname" - together
/// they do, the same division <c>TenantContextHostChannelTests.cs</c> already established.
/// </summary>
public sealed class PersonHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    public PersonHostChannelTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage PersonActorRequest(HttpMethod method, string url, string host, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "registrar_ada");
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

    [Fact]
    public async Task Registering_a_person_through_a_verified_hostname_resolves_the_real_tenant_and_stores_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "enrol-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(PersonActorRequest(
            HttpMethod.Post, "/api/v1/persons", hostname, new { first_name = "Ngozi", last_name = "Okafor" }), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantA, body.GetProperty("tenant_id").GetString());
    }

    [Fact]
    public async Task Registering_a_person_through_an_unrecognised_host_fails_closed_as_tenant_context_missing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(PersonActorRequest(
            HttpMethod.Post, "/api/v1/persons", "nobody-registered-this-person-host.example.edu.ng",
            new { first_name = "Ngozi", last_name = "Okafor" }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // matches DeviceRegistryTests' established convention
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Registering_a_person_through_an_unverified_pending_host_also_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = "pending-person-e2e.example.edu.ng";
        var registerResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname, tenant_id = TenantA }), ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var response = await client.SendAsync(PersonActorRequest(
            HttpMethod.Post, "/api/v1/persons", hostname, new { first_name = "Ngozi", last_name = "Okafor" }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_person_registered_under_one_tenant_s_verified_host_is_not_reachable_through_another_tenant_s()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "tenant-a-person-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "tenant-b-person-e2e.example.edu.ng", TenantB, ct);

        var created = await client.SendAsync(PersonActorRequest(
            HttpMethod.Post, "/api/v1/persons", hostA, new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var personId = createdBody.GetProperty("id").GetString();

        var crossTenantGet = await client.SendAsync(PersonActorRequest(HttpMethod.Get, $"/api/v1/persons/{personId}", hostB), ct);

        Assert.Equal(HttpStatusCode.NotFound, crossTenantGet.StatusCode); // never forbidden - ADR-0001 §1.5
    }

    [Fact]
    public async Task An_unrelated_endpoint_still_succeeds_under_an_arbitrary_Host_header()
    {
        // TenantContextMiddleware runs on every request. If DI failed to resolve
        // ICurrentTenantProvider's adapter, or PersonService's registrations, for the
        // middleware or module wiring, this would surface as a 500 here - the regression
        // registering BioIdn001Module wrong, or not at all, would actually produce.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live") { Headers = { Host = "whatever-person-e2e.example.edu.ng" } };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
