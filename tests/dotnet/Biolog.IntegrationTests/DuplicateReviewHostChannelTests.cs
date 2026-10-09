using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-IDN-005's
/// candidate-raising reads real BIO-IDN-001 name/date-of-birth data and real BIO-IDN-003
/// institutional identifier values through the real adapters
/// <c>BioIdn005Module.PersonAttributeGatewayAdapter</c>/<c>IdentifierAttributeGatewayAdapter</c>
/// wire up - not merely an injected test double - and that AC04's cross-tenant prohibition
/// holds at the real, shipped host channel. Mirrors <c>InstitutionalIdentifierHostChannelTests.cs</c>
/// (BIO-IDN-003) and <c>PersonProfileHostChannelTests.cs</c> (BIO-IDN-002) exactly: registers and
/// verifies a real domain through BIO-TEN-002's own endpoints, registers real persons through
/// BIO-IDN-001's own endpoint, then exercises BIO-IDN-005's endpoints through that verified
/// hostname.
/// </summary>
public sealed class DuplicateReviewHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    public DuplicateReviewHostChannelTests(WebApplicationFactory<Program> factory)
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

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, string firstName, string lastName, string bornOn, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons", host, new { first_name = firstName, last_name = lastName, born_on = bornOn }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Scanning_through_a_verified_hostname_raises_a_candidate_from_two_real_persons_sharing_a_name_and_date_of_birth()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "dup-review-name-dob-e2e.example.edu.ng", TenantA, ct);
        var personA = await RegisterPersonAsync(client, hostname, "Chiamaka", "Nwosu", "2006-11-02", ct);
        await RegisterPersonAsync(client, hostname, "Chiamaka", "Nwosu", "2006-11-02", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personA}/duplicate-candidates/scan", hostname), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Contains("NAME_AND_DATE_OF_BIRTH", data[0].GetProperty("signal_sources").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Scanning_through_a_verified_hostname_raises_a_candidate_from_two_real_persons_sharing_a_real_institutional_identifier_value()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "dup-review-identifier-e2e.example.edu.ng", TenantA, ct);
        var personA = await RegisterPersonAsync(client, hostname, "Tunde", "Bakare", "2003-05-19", ct);
        var personB = await RegisterPersonAsync(client, hostname, "Someone", "Else", "1990-01-01", ct);

        // Disable enforced uniqueness for this exact (type, custom_label) scope so BOTH
        // identifier assignments can succeed - this test is proving BIO-IDN-005's own
        // detection, not BIO-IDN-003's uniqueness enforcement. The rule endpoint is
        // tenant-scoped too, so it needs the resolved hostname, not just an actor.
        var disableRule = await client.SendAsync(
            ActorRequest(HttpMethod.Put, "/api/v1/identifier-types/TENANT_DEFINED/uniqueness-rule?custom_label=library_card", hostname, new { enforced = false }), ct);
        Assert.Equal(HttpStatusCode.OK, disableRule.StatusCode);
        var assignA = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personA}/identifiers", hostname, new { type = "TENANT_DEFINED", custom_label = "library_card", value = "LIB-0099" }), ct);
        Assert.Equal(HttpStatusCode.Created, assignA.StatusCode);
        var assignB = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personB}/identifiers", hostname, new { type = "TENANT_DEFINED", custom_label = "library_card", value = "LIB-0099" }), ct);
        Assert.Equal(HttpStatusCode.Created, assignB.StatusCode);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personA}/duplicate-candidates/scan", hostname), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Contains("INSTITUTIONAL_IDENTIFIER_VALUE", data[0].GetProperty("signal_sources").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task BIOIDN005_QA01_scanning_through_a_verified_hostname_raises_a_candidate_from_two_real_persons_sharing_a_real_contact_data_value()
    {
        // BIO-IDN-005-QA-01 (Seyi round 1, High): proves the fix through the REAL packaged host
        // - the PersonContactPointService/endpoint this round added, not an injected fake.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "dup-review-qa01-contact-e2e.example.edu.ng", TenantA, ct);
        var personA = await RegisterPersonAsync(client, hostname, "Kelechi", "Nnamdi", "2002-04-11", ct);
        var personB = await RegisterPersonAsync(client, hostname, "Someone", "Else", "1985-01-01", ct);

        var setA = await client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{personA}/contact-point", hostname, new { email = "kelechi.n@example.edu.ng" }), ct);
        Assert.Equal(HttpStatusCode.OK, setA.StatusCode);
        var setB = await client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{personB}/contact-point", hostname, new { email = "KELECHI.N@EXAMPLE.EDU.NG" }), ct);
        Assert.Equal(HttpStatusCode.OK, setB.StatusCode);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personA}/duplicate-candidates/scan", hostname), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Contains("CONTACT_DATA", data[0].GetProperty("signal_sources").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task BIOIDN005_QA01_a_tenant_B_person_sharing_the_same_contact_data_never_surfaces_as_a_candidate_for_a_tenant_A_subject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "dup-review-qa01-contact-cross-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "dup-review-qa01-contact-cross-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var tenantAPerson = await RegisterPersonAsync(client, hostA, "Femi", "Adeoye", "1997-06-06", ct);
        var tenantBPerson = await RegisterPersonAsync(client, hostB, "Someone", "Unrelated", "1970-01-01", ct);
        await client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{tenantAPerson}/contact-point", hostA, new { email = "femi.a@example.edu.ng" }), ct);
        // A real person in a DIFFERENT tenant, sharing the exact same email on file.
        await client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{tenantBPerson}/contact-point", hostB, new { email = "femi.a@example.edu.ng" }), ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{tenantAPerson}/duplicate-candidates/scan", hostA), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, body.GetProperty("data").GetArrayLength()); // the tenant B person's contact data is invisible to this scan, by construction
    }

    [Fact]
    public async Task BIOIDN005_AC04_a_tenant_B_person_sharing_the_same_name_and_date_of_birth_never_surfaces_as_a_candidate_for_a_tenant_A_subject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "dup-review-cross-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "dup-review-cross-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var tenantAPerson = await RegisterPersonAsync(client, hostA, "Blessing", "Adeyemi", "2001-08-08", ct);
        await RegisterPersonAsync(client, hostB, "Blessing", "Adeyemi", "2001-08-08", ct); // a real, matching person - but in a DIFFERENT tenant

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{tenantAPerson}/duplicate-candidates/scan", hostA), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, body.GetProperty("data").GetArrayLength()); // the tenant B person is invisible to this scan, by construction
    }

    [Fact]
    public async Task Scanning_a_never_registered_canonical_person_id_through_a_verified_hostname_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "dup-review-unknown-person-e2e.example.edu.ng", TenantA, ct);
        const string neverRegisteredPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{neverRegisteredPersonId}/duplicate-candidates/scan", hostname), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Scanning_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d/duplicate-candidates/scan",
            "nobody-registered-this-host-dup-review.example.edu.ng"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
