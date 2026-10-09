using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-CRD-005's
/// tenant boundary and BIO-IDN-001 existence-gateway wiring are enforced by the real, shipped
/// host channel and person registry - not merely an injected test double. Mirrors
/// <c>PhotographyHostChannelTests.cs</c>/<c>PhotoBoothHostChannelTests.cs</c> exactly: registers
/// and verifies a real domain through BIO-TEN-002's own endpoints, registers a real person
/// through BIO-IDN-001's own endpoint, then exercises BIO-CRD-005's endpoints through that
/// verified hostname. SYNTHETIC fixtures only.
/// </summary>
public sealed class CardIssuanceHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    public CardIssuanceHostChannelTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "operator_ada");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
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

    private static object IssueBody() => new
    {
        template_ref = "template-e2e",
        photo_ref = "photo-e2e",
        logo_ref = "logo-e2e",
        signature_ref = "signature-e2e",
        printer_ref = "printer-e2e",
    };

    [Fact]
    public async Task AC01_AC05_issuing_a_card_for_a_real_person_through_a_verified_hostname_resolves_the_real_tenant_and_records_provenance()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "card-issue-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff", hostname, IssueBody()), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("sequence_number").GetInt32());
        Assert.Equal("template-e2e", body.GetProperty("provenance").GetProperty("template_ref").GetString());
        Assert.Equal("operator_ada", body.GetProperty("provenance").GetProperty("operator_actor_id").GetString());
    }

    [Fact]
    public async Task AC04_activating_a_real_card_issue_through_the_real_host_requires_and_honours_the_idempotency_key()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "card-activate-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff", hostname, IssueBody()), ct);

        var withoutKey = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/activate", hostname, new { expected_version = 1 }), ct);
        Assert.Equal((HttpStatusCode)428, withoutKey.StatusCode);

        var activated = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/activate", hostname, new { expected_version = 1 }, idempotencyKey: "e2e-activate-1"), ct);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var body = await activated.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task BIOCRD005_issuing_a_card_for_an_unknown_canonical_person_id_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "card-unknown-person-e2e.example.edu.ng", TenantA, ct);
        const string neverRegisteredPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{neverRegisteredPersonId}/card-issues/staff", hostname, IssueBody()), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOCRD005_a_tenant_B_caller_cannot_issue_a_card_for_a_real_tenant_A_person()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "card-real-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "card-real-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var tenantAPersonId = await RegisterPersonAsync(client, hostA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{tenantAPersonId}/card-issues/staff", hostB, IssueBody()), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString()); // identical to the wholly-unknown case (ADR-0001 §1.5)
    }

    [Fact]
    public async Task Issuing_a_card_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d/card-issues/staff",
            "nobody-registered-this-card-issue-host.example.edu.ng", IssueBody()), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
