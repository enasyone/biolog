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
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, of TD-WEB-PROVENANCE-
/// BRIDGE round 6's fix: <c>demo-actor-{roleId}</c> was not a well-formed canonical BIOLOG
/// identifier, so <c>CanonicalId.IsPersonId</c> rejected it before any RBAC grant lookup ever
/// ran - no story could get a real "granted" result through this bridge. The web side now
/// derives one of three fixed <c>per_&lt;UUIDv7&gt;</c> literals per demo role
/// (<c>web/lib/demo-identity.ts</c>'s <c>DEMO_PERSON_ID_BY_ROLE</c>), and
/// <see cref="Biolog.Api.Tenancy.DemoSessionIdentitySeed"/> seeds the matching Person
/// (bio-idn-001) and PersonRoleAssignment (bio-idn-006) rows into the one synthetic tenant
/// round 5 already established.
///
/// Target endpoint: <c>GET /api/v1/persons/{personId}/roles</c> (BIO-IDN-006's own read
/// listing) - deliberately chosen because it requires no actor/provenance headers at all (no
/// <c>ActorResolver</c>/<c>ActorProvenanceGate</c> call in its handler), only a resolved tenant,
/// so it proves the seeded rows exist directly without the same "no test project has
/// InternalsVisibleTo access to seed a real permission grant" limitation
/// <see cref="DemoSessionTenantResolverEndpointsTests"/>'s own header discloses for its
/// mutating, permission-gated target endpoint.
/// </summary>
public sealed class DemoSessionIdentitySeedEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string DemoSessionSecret = "test-host-e2e-web-session-bridge-secret-demo-bridge";
    private const string SyntheticTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2e9";

    private const string AdminPersonId = "per_01a113f6-6268-730c-a5ce-44d97f0aa7f5";
    private const string StaffPersonId = "per_01a113f6-6268-7c44-a9e5-3aee6ff59416";
    private const string ViewerPersonId = "per_01a113f6-6268-757e-926c-4a1b5f98b45b";

    public DemoSessionIdentitySeedEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSessionBridge:TokenSecret"] = DemoSessionSecret,
                ["WebSessionBridge:SyntheticTenantId"] = SyntheticTenantId,
            })));
    }

    /// <summary>Mirrors web/lib/session-token.ts, same as every sibling TD-WEB-PROVENANCE-BRIDGE test.</summary>
    private static string SignDemoSessionToken(string secret, string roleId = "admin", string tenantId = "alpha") =>
        SignDemoSessionTokenRaw(secret, new
        {
            sid = $"sid_{Guid.NewGuid():N}",
            roleId,
            tenantId,
            issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expiresAtMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        });

    private static string SignDemoSessionTokenRaw(string secret, object claims)
    {
        var json = JsonSerializer.Serialize(claims);
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadB64)));
        return $"{payloadB64}.{signature}";
    }

    private HttpClient ClientWithDemoSession(string roleId) => WithSessionHeader(_factory.CreateClient(), roleId);

    private static HttpClient WithSessionHeader(HttpClient client, string roleId)
    {
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret, roleId));
        return client;
    }

    [Theory]
    [InlineData("admin", AdminPersonId, "TENANT_ADMIN")]
    [InlineData("staff", StaffPersonId, "ENROLMENT")]
    [InlineData("viewer", ViewerPersonId, "READ_ONLY")]
    public async Task The_seeded_person_and_role_assignment_for_each_demo_role_is_visible_through_the_real_host(
        string roleId, string personId, string expectedWireRole)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientWithDemoSession(roleId);

        var response = await client.GetAsync($"/api/v1/persons/{personId}/roles", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(data, entry =>
            entry.GetProperty("person_id").GetString() == personId &&
            entry.GetProperty("tenant_id").GetString() == SyntheticTenantId &&
            entry.GetProperty("role").GetString() == expectedWireRole &&
            entry.GetProperty("status").GetString() == "ACTIVE");
    }

    [Fact]
    public async Task The_seed_is_confined_to_the_one_synthetic_tenant_a_request_with_no_demo_session_token_sees_nothing()
    {
        // Same disclosed round-5 behaviour: with no genuine demo session token, tenant
        // resolution fails exactly as it did before this story, so the seeded rows (which live
        // only in the synthetic tenant) are never reachable through this unauthenticated path -
        // "quarantined to the synthetic tenant only" is proven here, not merely asserted.
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/persons/{AdminPersonId}/roles", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
