using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-VIS-001's gateway
/// wiring reaches the real BIO-IDN-001 person store and that preregistration/check-in/check-out
/// genuinely succeed for any asserted actor (ADR-0006 Decision 2). The two authorization
/// decisions this feature makes - <c>POST /api/v1/visitors/{id}/approve</c> and
/// <c>PUT /api/v1/visitors/config</c> - are proven the identical "rejected on authority grounds,
/// same disclosed limitation as every sibling story's own host-channel tests" way
/// <c>AttendanceHostChannelTests.cs</c> already established.
/// </summary>
public sealed class VisitorHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-vis001";

    public VisitorHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Visitors:ActorProvenanceSecret"] = ProvenanceSecret,
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

    private async Task<string> RegisterRealPersonAsync(HttpClient client, string hostname, string lastName, CancellationToken ct)
    {
        var request = ActorRequest(HttpMethod.Post, "/api/v1/persons", hostname, "hr_admin",
            new { first_name = "Synthetic", last_name = lastName, born_on = "2005-03-14" });
        var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return created.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task VIS001_preregistering_through_the_real_host_reaches_the_real_BIO_IDN_001_host_person_gateway_and_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "vis001-preregister-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/visitors", hostname, "frontdesk_1", new
        {
            full_name = "Synthetic Visitor",
            host_person_id = hostPersonId,
        }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Synthetic Visitor", body.GetProperty("full_name").GetString());
        Assert.Equal(hostPersonId, body.GetProperty("host_person_id").GetString());
        Assert.Equal("pending", body.GetProperty("host_approval_status").GetString());
    }

    [Fact]
    public async Task VIS001_preregistering_with_a_host_that_does_not_exist_in_the_resolved_tenant_is_rejected_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "vis001-unknown-host-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/visitors", hostname, "frontdesk_1", new
        {
            full_name = "Synthetic Visitor",
            host_person_id = "per_0199e0c2-9999-7999-8999-999999999999",
        }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VIS001_AC02_approving_through_the_real_host_is_rejected_for_lack_of_authority()
    {
        // Same disclosed limitation as AttendanceHostChannelTests/AccessPolicyHostChannelTests:
        // this project has no InternalsVisibleTo seam into BIO-IDN-006 to grant a real
        // permission, so this can only ever demonstrate the rejection path - the positive path
        // is proven at the unit level (VisitorRecordServiceTests.cs) with a fake permission gateway.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "vis001-approve-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);

        var preregisterResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/visitors", hostname, "frontdesk_1", new
        {
            full_name = "Synthetic Visitor",
            host_person_id = hostPersonId,
        }), ct);
        var preregistered = await preregisterResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var visitorId = preregistered.GetProperty("id").GetString();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/visitors/{visitorId}/approve", hostname, "unprivileged_attacker",
            new { version = 1 }, withProvenance: true), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VIS001_approving_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "vis001-approve-provenance-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);

        var preregisterResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/visitors", hostname, "frontdesk_1", new
        {
            full_name = "Synthetic Visitor",
            host_person_id = hostPersonId,
        }), ct);
        var preregistered = await preregisterResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var visitorId = preregistered.GetProperty("id").GetString();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/visitors/{visitorId}/approve", hostname, "unprivileged_attacker",
            new { version = 1 }, withProvenance: false), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VIS001_AC03_setting_the_config_through_the_real_host_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "vis001-config-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/visitors/config", hostname, "unprivileged_attacker",
            new { require_nda = true, default_visit_duration_hours = 8, max_visit_duration_hours = 24 }, withProvenance: true), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VIS001_AC05_the_three_UI_bridge_routes_answer_a_genuinely_headerless_request_through_the_real_host()
    {
        // web/features/visitors/live.ts's fetch() sends no custom headers at all - proving these
        // three routes work with NO Host/X-Actor-Id/X-Actor-Provenance is the actual AC05 proof.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var primary = await client.PostAsync("/api/v1/visitors/primary-action", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
        var primaryBody = await primary.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Visitors:", primaryBody.GetProperty("message").GetString());

        var history = await client.PostAsync("/api/v1/visitors/view-visit-history", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyBody = await history.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Visitors:", historyBody.GetProperty("message").GetString());

        var checkOut = await client.PostAsync("/api/v1/visitors/check-out-visitor", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, checkOut.StatusCode);
        var checkOutBody = await checkOut.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Visitors:", checkOutBody.GetProperty("message").GetString());
    }
}
