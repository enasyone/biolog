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
/// TD-WEB-PROVENANCE-BRIDGE-REVERIFY/BIO-ATT-003-F02 (Tunde, 2026-10-07): end-to-end proof,
/// through the REAL packaged <c>Biolog.Api</c> host, that a signed web-session identity reaches
/// a genuine authorized "granted" result for this story's own shift-management workflow - not
/// just the pre-bridge negative-only proof round 1's review correctly rejected.
///
/// Simpler than <see cref="BioTen003ReverifyEndpointsTests"/>'s own equivalent: this story
/// resolves tenant AMBIENTLY throughout (no explicit route-supplied tenant id, no separate
/// tenant-existence gateway), so round 5's <c>DemoSessionTenantResolver</c> confining ambient
/// resolution to the one synthetic tenant is sufficient on its own -
/// <c>Biolog.Api.Modules.BioAtt003Module.Configure</c> only needs to seed the real
/// <c>RolePermissionGrant</c> rows this story's own two permission keys need, never a tenant-
/// registry row too.
/// </summary>
public sealed class BioAtt003ReverifyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "test-host-e2e-att003-actor-provenance-secret-bridge";
    private const string DemoSessionSecret = "test-host-e2e-att003-web-session-bridge-secret-demo";
    private const string SyntheticTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2e9";

    private const string AdminPersonId = "per_01a113f6-6268-730c-a5ce-44d97f0aa7f5";
    private const string ViewerPersonId = "per_01a113f6-6268-757e-926c-4a1b5f98b45b";

    public BioAtt003ReverifyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkforceAttendance:ActorProvenanceSecret"] = ProvenanceSecret,
                ["WebSessionBridge:TokenSecret"] = DemoSessionSecret,
                ["WebSessionBridge:SyntheticTenantId"] = SyntheticTenantId,
            })));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    /// <summary>Signs a demo session token exactly the way web/lib/session-token.ts does - mirrored, not imported, the same cross-stack TS-signer/C#-verifier boundary every sibling reverify test uses.</summary>
    private static string SignDemoSessionToken(string secret)
    {
        var claims = new
        {
            sid = $"sid_{Guid.NewGuid():N}",
            roleId = "admin",
            tenantId = "alpha",
            issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expiresAtMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        };
        var json = JsonSerializer.Serialize(claims);
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadB64)));
        return $"{payloadB64}.{signature}";
    }

    private HttpClient ClientAs(string personId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor-Id", personId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(personId));
        // Ambient tenant resolution (round 5's DemoSessionTenantResolver) is this story's own
        // ONLY tenant channel - without this header, RequireTenant() throws
        // TenantContextMissing before ever reaching a permission check.
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret));
        return client;
    }

    [Fact]
    public async Task Admin_can_create_a_shift_a_real_granted_result_through_the_real_rbac_engine()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientAs(AdminPersonId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workforce-attendance/shifts",
            new { name = "Reverify Shift", start_time = "08:00", end_time = "17:00", grace_minutes = 10 },
            ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Reverify Shift", body.GetProperty("name").GetString());

        // Confirms the write genuinely persisted server-side, not merely a 201 with no backing
        // state - a real follow-up list call sees it.
        var listResponse = await client.GetAsync("/api/v1/workforce-attendance/shifts", ct);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains(listBody.GetProperty("data").EnumerateArray(), s => s.GetProperty("name").GetString() == "Reverify Shift");
    }

    [Fact]
    public async Task Viewer_with_no_shift_manage_grant_is_denied_creating_a_shift_a_real_403_not_a_blanket_rejection()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientAs(ViewerPersonId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workforce-attendance/shifts",
            new { name = "Should Not Be Created", start_time = "08:00", end_time = "17:00", grace_minutes = 10 },
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // No silent partial success: the shift was genuinely never created.
        var adminClient = ClientAs(AdminPersonId);
        var listResponse = await adminClient.GetAsync("/api/v1/workforce-attendance/shifts", ct);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.DoesNotContain(listBody.GetProperty("data").EnumerateArray(), s => s.GetProperty("name").GetString() == "Should Not Be Created");
    }

    [Fact]
    public async Task With_no_actor_provenance_at_all_shift_creation_is_still_rejected_401_unchanged_from_before_this_fix()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor-Id", AdminPersonId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workforce-attendance/shifts",
            new { name = "Unauthorized", start_time = "08:00", end_time = "17:00", grace_minutes = 10 },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
