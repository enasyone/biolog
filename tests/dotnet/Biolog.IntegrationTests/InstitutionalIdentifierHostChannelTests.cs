using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-IDN-003's
/// tenant boundary and BIO-IDN-002-QA-01-style existence check are enforced by the real,
/// shipped host channel - not merely an injected test double. Mirrors
/// <c>PersonProfileHostChannelTests.cs</c> (BIO-IDN-002) exactly: registers and verifies a real
/// domain through BIO-TEN-002's own endpoints, registers a real person through BIO-IDN-001's own
/// endpoint, then exercises BIO-IDN-003's endpoints through that verified hostname.
/// </summary>
public sealed class InstitutionalIdentifierHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    public InstitutionalIdentifierHostChannelTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null)
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

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons", host, new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Assigning_an_identifier_through_a_verified_hostname_resolves_the_real_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "enrol-identifier-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/identifiers", hostname, new { type = "MATRICULATION_NUMBER", value = "MAT/2026/00123" }), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantA, body.GetProperty("tenant_id").GetString());
    }

    [Fact]
    public async Task BIOIDN003_assigning_an_identifier_for_an_unknown_canonical_person_id_is_rejected()
    {
        // Applying BIO-IDN-002-QA-01 from the start: a real, verified tenant host, but a
        // person id that was never registered anywhere - through the ACTUAL packaged host's
        // real IPersonExistenceGateway adapter, not an injected fake.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "unknown-person-idn003-e2e.example.edu.ng", TenantA, ct);
        const string neverRegisteredPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{neverRegisteredPersonId}/identifiers", hostname, new { type = "MATRICULATION_NUMBER", value = "MAT/2026/00123" }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOIDN003_a_tenant_B_caller_cannot_attach_an_identifier_to_a_real_tenant_A_person()
    {
        // Canonical identifiers carry no authority (ADR-0001 §1.5) - a REAL person that
        // genuinely exists in tenant A must still be rejected, identically to an unknown one,
        // when referenced through tenant B's host.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "real-tenant-a-person-idn003-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "real-tenant-b-caller-idn003-e2e.example.edu.ng", TenantB, ct);
        var tenantAPersonId = await RegisterPersonAsync(client, hostA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{tenantAPersonId}/identifiers", hostB, new { type = "MATRICULATION_NUMBER", value = "MAT/2026/00123" }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString()); // identical to the wholly-unknown case
    }

    [Fact]
    public async Task Assigning_an_identifier_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d/identifiers",
            "nobody-registered-this-identifier-host.example.edu.ng", new { type = "MATRICULATION_NUMBER", value = "MAT/2026/00123" }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unrelated_endpoint_still_succeeds_under_an_arbitrary_Host_header()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live") { Headers = { Host = "whatever-identifier-e2e.example.edu.ng" } };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
