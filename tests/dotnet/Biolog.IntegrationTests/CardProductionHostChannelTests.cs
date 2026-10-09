using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-CRD-007's real
/// cross-story dependency on BIO-CRD-005 (activation gated on a passed production job, then
/// delegated to a REAL, already-shipped <c>CardIssue</c> row) is enforced by the real, shipped
/// host channel - not merely an injected test double. Mirrors
/// <c>CardIssuanceHostChannelTests.cs</c>/<c>CardLifecycleHostChannelTests.cs</c>'s own
/// precedent: registers and verifies a real domain, registers a real person, issues a real card
/// through BIO-CRD-005's own endpoint, then produces and activates it through BIO-CRD-007's own
/// endpoints. SYNTHETIC fixtures only.
/// </summary>
public sealed class CardProductionHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c88";

    public CardProductionHostChannelTests(WebApplicationFactory<Program> factory)
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

    private async Task<string> IssueCardAsync(HttpClient client, string host, CancellationToken ct)
    {
        var personId = await RegisterPersonAsync(client, host, ct);
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff", host, new
            {
                template_ref = "template-e2e",
                photo_ref = "photo-e2e",
                printer_ref = "printer-e2e",
            }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return personId;
    }

    private static object ProductionRequest() => new { printer_ref = "printer-e2e", workstation_ref = "workstation-e2e" };

    private async Task<int> RecordStepAsync(HttpClient client, string host, string personId, string step, int expectedVersion, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/jobs/1/steps/{step}", host,
            new { passed = true, expected_version = expectedVersion }), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("version").GetInt32();
    }

    [Fact]
    public async Task AC01_AC02_AC05_a_passed_production_job_through_the_real_host_gates_and_then_activates_a_real_card()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd007-pass-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueCardAsync(client, hostname, ct);

        var createResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/jobs", hostname, ProductionRequest()), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        var afterPrint = await RecordStepAsync(client, hostname, personId, "print", created.GetProperty("version").GetInt32(), ct);
        var afterEncode = await RecordStepAsync(client, hostname, personId, "encode", afterPrint, ct);
        await RecordStepAsync(client, hostname, personId, "readback-qc", afterEncode, ct);

        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/activate", hostname,
            new { expected_version = 1 }, idempotencyKey: "e2e-activate-1"), ct);

        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("active", activated.GetProperty("status").GetString());

        // The real BIO-CRD-005 card issue is genuinely Active there too, not merely this
        // story's own gate.
        var realCardResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/persons/{personId}/card-issues/staff/1", hostname), ct);
        var realCard = await realCardResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("active", realCard.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC02_activation_through_the_real_host_is_refused_when_no_production_job_has_passed_and_the_real_card_stays_Issued()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd007-nopass-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueCardAsync(client, hostname, ct);

        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/activate", hostname,
            new { expected_version = 1 }, idempotencyKey: "e2e-activate-nopass"), ct);

        Assert.Equal(HttpStatusCode.Conflict, activateResponse.StatusCode);
        var problem = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("production_not_passed", problem.GetProperty("code").GetString());

        var realCardResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/persons/{personId}/card-issues/staff/1", hostname), ct);
        var realCard = await realCardResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("issued", realCard.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC03_a_signed_package_issued_and_completed_through_the_real_host_produces_and_activates_a_real_card()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd007-offline-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueCardAsync(client, hostname, ct);

        var issueResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/packages", hostname, ProductionRequest()), ct);
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        var package = await issueResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = package.GetProperty("token").GetString();

        var completeResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/card-production/offline-complete", hostname,
            new { token, print_passed = true, encode_passed = true, readback_qc_passed = true }, idempotencyKey: "e2e-offline-1"), ct);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var job = await completeResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("passed", job.GetProperty("status").GetString());

        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/activate", hostname,
            new { expected_version = 1 }, idempotencyKey: "e2e-offline-activate-1"), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
    }

    [Fact]
    public async Task A_tenant_B_caller_cannot_create_a_production_job_for_a_real_tenant_A_persons_card()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "crd007-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "crd007-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var personId = await IssueCardAsync(client, hostA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/production/jobs", hostB, ProductionRequest()), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // BIO-CRD-005's own tenant-scoped repository never finds tenant A's row under tenant B
    }

    [Fact]
    public async Task Creating_a_production_job_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b3f/card-issues/staff/1/production/jobs",
            "nobody-registered-this-crd007-host.example.edu.ng", ProductionRequest()), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
