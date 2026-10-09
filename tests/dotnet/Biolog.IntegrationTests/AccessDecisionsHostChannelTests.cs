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
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-ACC-002's own
/// decision endpoint genuinely reaches BIO-ACC-001's real, ADR-0006-gated
/// <c>PolicyEvaluationService</c> - not merely an injected test double - and that its own
/// provenance gate holds. Mirrors <c>AccessPolicyHostChannelTests.cs</c>'s own disclosed
/// limitation: this project has no way to seed a real SUPER_ADMIN or a real
/// <c>access_policy.evaluate</c> grant through any public endpoint (BIO-IDN-006-QA-01), so this
/// file demonstrates the request reaching the real, gated service and being rejected on authority
/// grounds - never a decision SUCCEEDING here. The positive-path AC01-AC04 proofs, with a fake
/// privileged policy gateway, live at the unit level in <c>AccessDecisionServiceTests.cs</c>.
/// </summary>
public sealed class AccessDecisionsHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-acc002";

    public AccessDecisionsHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccessDecisions:ActorProvenanceSecret"] = ProvenanceSecret,
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, string actorId, object? body = null, bool includeProvenance = true)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", actorId);
        if (includeProvenance)
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

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", host, "admin_alice", new { first_name = "Chika", last_name = "Nwosu" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    private static object DecideBody(string personId) => new
    {
        person_id = personId,
        credential_class = "staff",
        location_id = "loc_main_gate",
        presented_card_status = "active",
        policy_key = "e2e-gate-policy",
    };

    [Fact]
    public async Task AC01_AC02_AC03_a_provenance_verified_but_unprivileged_actor_reaches_the_real_gated_policy_service_and_is_denied_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc002-decide-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(
            ActorRequest(HttpMethod.Post, "/api/v1/access-decisions/decisions", hostname, "unprivileged_gate_operator", DecideBody(personId)), ct);

        // The request reaches BIO-ACC-001's own real, already-registered PolicyEvaluationService
        // (through this story's own gateway) and is rejected there on authority grounds - proving
        // the real cross-story wiring, not a stub. See this file's own header for why a SUCCESSFUL
        // decision cannot be demonstrated through the real host in this test project.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task the_decision_route_requires_a_verified_provenance_proof_identical_to_BIO_ACC_001s_own_evaluate_route()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc002-no-provenance-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(
            ActorRequest(HttpMethod.Post, "/api/v1/access-decisions/decisions", hostname, "unprivileged_gate_operator", DecideBody(personId), includeProvenance: false), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC05_the_primary_action_bridge_route_answers_a_genuinely_headerless_request_with_a_real_published_demo_policy_and_a_real_computed_decision()
    {
        // web/features/access/live.ts's fetch() sends no custom headers at all.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/access/primary-action", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var message = body.GetProperty("message").GetString()!;
        Assert.Contains("demo policy", message);
        Assert.Contains("published", message);
        Assert.Matches("decision for per_.+ at demo_gate_main is (ALLOW|DENY)", message);
    }
}
