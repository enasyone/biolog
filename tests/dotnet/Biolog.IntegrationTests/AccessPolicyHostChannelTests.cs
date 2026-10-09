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
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-ACC-001's
/// gateway wiring reaches the real BIO-IDN-001/BIO-IDN-006 stores and that its own RBAC/provenance
/// gates hold through the actual shipped endpoints - not merely an injected test double. Mirrors
/// BIO-IDN-006's own <c>RbacHostChannelTests.cs</c>: registers and verifies a real domain through
/// BIO-TEN-002's own endpoints, registers a real person through BIO-IDN-001's own endpoint, then
/// exercises BIO-ACC-001's endpoints through that verified hostname.
///
/// <b>Same disclosed limitation as <c>RbacHostChannelTests.cs</c>.</b> This project has no
/// <c>InternalsVisibleTo</c> access to seed a real SUPER_ADMIN or any real permission grant
/// directly into BIO-IDN-006's internals - a tenant's very first SUPER_ADMIN cannot be created
/// through any public endpoint at all (BIO-IDN-006-QA-01's own disclosed consequence). So this
/// file can only demonstrate a policy-authoring/evaluation request reaching
/// <see cref="AccessPolicyService"/>/<see cref="PolicyEvaluationService"/> through the real host
/// and being rejected on authority grounds - never a mutation SUCCEEDING here. The underlying
/// AC01/AC02/AC03 positive-path proofs, with a privileged actor, live at the unit level in
/// <c>AccessPolicyServiceTests.cs</c> and <c>PolicyEvaluationServiceTests.cs</c>.
/// </summary>
public sealed class AccessPolicyHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-acc001";

    public AccessPolicyHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rbac:ActorProvenanceSecret"] = ProvenanceSecret,
                ["AccessPolicy:ActorProvenanceSecret"] = ProvenanceSecret,
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, string actorId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", actorId);
        request.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(actorId));
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

    private static object FullRule() => new
    {
        effect = "ALLOW",
        subject = new { kind = "ANY_PERSON" },
        credential = new { kind = "ANY" },
        location = new { kind = "ANY" },
        schedule = new { kind = "ALWAYS" },
        identity_status = new { any_status = true },
        card_status = new { any_status = true },
        required_assurance_level = "BASIC",
    };

    [Fact]
    public async Task Creating_a_draft_through_a_verified_hostname_reaches_the_real_service_and_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc001-create-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/access-policies", hostname, "unprivileged_attacker", new { policy_key = "main-campus", rules = new[] { FullRule() } }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Evaluating_through_a_verified_hostname_reaches_the_real_service_and_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc001-evaluate-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/access-policies/main-campus/evaluate", hostname, "unprivileged_attacker", new
            {
                person_id = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99", credential_class = "staff-card", location_id = "main-gate",
                presented_card_status = "active", presented_assurance_level = "BASIC",
            }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ACC001_PRIVILEGE_ESCALATION_a_caller_without_a_valid_provenance_proof_is_rejected_before_authority_is_ever_checked_through_the_real_packaged_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc001-provenance-e2e.example.edu.ng", TenantA, ct);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/access-policies") { Headers = { Host = hostname }, Content = JsonContent.Create(new { policy_key = "main-campus", rules = new[] { FullRule() } }) };
        request.Headers.Add("X-Actor-Id", "unprivileged_attacker"); // deliberately no X-Actor-Provenance at all

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString()); // not permission_required
    }

    [Fact]
    public async Task Creating_a_draft_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/access-policies", "nobody-registered-this-acc001-host.example.edu.ng", "admin_alice",
            new { policy_key = "main-campus", rules = new[] { FullRule() } }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOACC001_QA01_an_unprivileged_actor_cannot_list_policies_through_the_real_packaged_host()
    {
        // BIO-ACC-001-QA-01 (Chioma's acceptance audit, High): a policy's own content is the
        // sensitive asset, so listing now requires access_policy.read exactly like every
        // mutating endpoint - proven here the same way every other rejection in this file is:
        // this project has no seeding backdoor into a real RBAC grant, so an unprivileged actor
        // is rejected on authority grounds, never shown an (empty or otherwise) catalogue.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc001-list-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/access-policies", hostname, "unprivileged_attacker"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOACC001_QA01_listing_policies_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "acc001-list-provenance-e2e.example.edu.ng", TenantA, ct);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/access-policies") { Headers = { Host = hostname } };
        request.Headers.Add("X-Actor-Id", "unprivileged_attacker"); // deliberately no X-Actor-Provenance

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }
}
