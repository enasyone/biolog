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
/// BIO-VIS-002: end-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that this
/// feature's own gateway wiring reaches BIO-VIS-001's own real <c>VisitorRecordService</c> (not a
/// stub) and that a sibling feature's own domain error is correctly translated into this
/// feature's own problem-response shape, rather than surfacing as an unhandled 500.
///
/// <b>Same disclosed limitation as every sibling story's own <c>*HostChannelTests.cs</c>
/// (see <c>VisitorHostChannelTests.cs</c>'s own header):</b> this project has no
/// <c>InternalsVisibleTo</c> seam to seed a real BIO-IDN-006 permission grant, and BIO-VIS-001's
/// own <c>ApproveAsync</c> is itself unreachable through this real host for the identical reason -
/// so a visitor can never genuinely reach <c>checked-in</c> through this real, tenant-scoped host
/// in ANY integration test (VIS-001's own host tests never reach it either). What this file CAN
/// and DOES prove for real: the grant/revoke permission gate is reached and enforced, a
/// provenance proof is required before authority is even checked, the orchestrating checkout
/// endpoint genuinely calls through to BIO-VIS-001's own real service and correctly maps its
/// rejection (rather than throwing unhandled), and the history read reaches the real visitor
/// record. The GRANT/REVOKE/CHECKOUT-REVOKE SUCCESS paths are proven at the unit level
/// (<c>VisitorCredentialServiceTests.cs</c>) with fakes standing in for both gateways.
/// </summary>
public sealed class VisitorCredentialsHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-viscred002";

    public VisitorCredentialsHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VisitorCredentials:ActorProvenanceSecret"] = ProvenanceSecret,
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

    private async Task<(string VisitorId, int Version)> PreregisterVisitorAsync(HttpClient client, string hostname, string hostPersonId, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/visitors", hostname, "frontdesk_1", new
        {
            full_name = "Synthetic Visitor",
            host_person_id = hostPersonId,
        }), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (body.GetProperty("id").GetString()!, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task VISCRED001_the_real_checkout_endpoint_calls_through_to_BIO_VIS_001s_own_real_service_and_maps_its_rejection_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "viscred-checkout-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);
        var (visitorId, version) = await PreregisterVisitorAsync(client, hostname, hostPersonId, ct);

        // Never approved, so BIO-VIS-001's own real CheckOutAsync rejects it as "not checked-in" -
        // this story's own adapter must map that rejection, not let it surface as an unhandled 500.
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/visitors/{visitorId}/check-out", hostname, "security_guard_1",
            new { version }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("not_checked_in", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VISCRED002_granting_a_credential_through_the_real_host_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "viscred-grant-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a98", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);
        var (visitorId, _) = await PreregisterVisitorAsync(client, hostname, hostPersonId, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/visitors/{visitorId}/credential", hostname, "unprivileged_attacker",
            new { scope = "general", expires_at = "2099-01-01T00:00:00.000Z" }, withProvenance: true), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VISCRED003_granting_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "viscred-grant-provenance-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a97", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);
        var (visitorId, _) = await PreregisterVisitorAsync(client, hostname, hostPersonId, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/visitors/{visitorId}/credential", hostname, "unprivileged_attacker",
            new { scope = "general", expires_at = "2099-01-01T00:00:00.000Z" }, withProvenance: false), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VISCRED004_the_real_history_endpoint_reaches_the_real_BIO_VIS_001_visitor_record()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "viscred-history-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a96", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);
        var (visitorId, _) = await PreregisterVisitorAsync(client, hostname, hostPersonId, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/visitors/{visitorId}/credential-history", hostname, "frontdesk_1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Synthetic Visitor", body.GetProperty("full_name").GetString());
        Assert.Equal("preregistered", body.GetProperty("visit_status").GetString());
        Assert.Empty(body.GetProperty("credentials").EnumerateArray());
    }

    [Fact]
    public async Task VISCRED005_the_real_reconcile_endpoint_is_mapped_and_gated_identically_to_grant()
    {
        // BIO-VIS-002-F06 (Seyi round-5 FAIL): proves the new operator-recovery endpoint is
        // genuinely mapped and reaches the same permission+provenance gate as GrantAsync/
        // RevokeAsync - not a dead route. The success path (an actual stranded card actually
        // getting reconciled) is proven at the unit level
        // (VisitorCredentialServiceTests.ReconcileStrandedCardsForPersonAsync_...) with fakes
        // standing in for BIO-CRD-005, the same split every other capability in this file uses.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "viscred-reconcile-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a95", ct);
        var hostPersonId = await RegisterRealPersonAsync(client, hostname, "Demo Host", ct);

        var noProvenance = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/visitor-credentials/reconcile", hostname, "unprivileged_attacker",
            new { person_id = hostPersonId }, withProvenance: false), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noProvenance.StatusCode);
        var noProvenanceBody = await noProvenance.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", noProvenanceBody.GetProperty("code").GetString());

        var noPermission = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/visitor-credentials/reconcile", hostname, "unprivileged_attacker",
            new { person_id = hostPersonId }, withProvenance: true), ct);
        Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);
        var noPermissionBody = await noPermission.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", noPermissionBody.GetProperty("code").GetString());
    }
}
