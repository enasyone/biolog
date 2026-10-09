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
/// BIO-TRN-003: end-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that this
/// story's rule/delivery endpoints are genuinely wired to bio-trn-002's own real data - not a
/// mocked read.
///
/// The synthetic tenant used here is bio-trn-002's own fixed, PUBLIC
/// <c>TransportEventDemoBridgeService.DemoTenantId</c> constant's literal VALUE (that class
/// itself is <c>internal</c>, so this project cannot reference the constant directly - the value
/// is copied, not the symbol, the same "project cannot reference a sibling feature project"
/// boundary every cross-story test in this codebase already respects). Configuring
/// <c>WebSessionBridge:SyntheticTenantId</c> to this SAME value means: (1) round 5's own
/// <c>DemoSessionTenantResolver</c> confines THIS test's own ambient tenant resolution to it, (2)
/// <c>DemoSessionIdentitySeedModule</c> seeds the admin/viewer demo identities into it, and (3)
/// <c>BioTrn003Module.Configure</c> seeds this story's own RBAC grants into it - so a genuine,
/// headerless <c>POST /api/v1/transport/primary-action</c> call (bio-trn-002's own demo bridge,
/// which captures a REAL board event against that same fixed tenant) and this story's own
/// RBAC-gated reads land in the exact same tenant, letting this test prove the full real chain:
/// a real captured board event -> a real matching rule -> a real, minimized, visible delivery.
/// </summary>
public sealed class BioTrn003EndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "test-host-e2e-trn003-actor-provenance-secret-bridge";
    private const string DemoSessionSecret = "test-host-e2e-trn003-web-session-bridge-secret-demo";

    /// <summary>Copied from bio-trn-002's own internal <c>TransportEventDemoBridgeService.DemoTenantId</c> - see this class's own header.</summary>
    private const string SyntheticTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    private const string AdminPersonId = "per_01a113f6-6268-730c-a5ce-44d97f0aa7f5";
    private const string ViewerPersonId = "per_01a113f6-6268-757e-926c-4a1b5f98b45b";

    public BioTrn003EndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TransportNotifications:ActorProvenanceSecret"] = ProvenanceSecret,
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
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret));
        return client;
    }

    [Fact]
    public async Task A_rule_selecting_board_delivers_a_real_board_event_captured_via_bio_trn_002_own_demo_bridge()
    {
        var ct = TestContext.Current.CancellationToken;

        // Headerless - bio-trn-002's own demo bridge, captures a real Board event against the
        // same fixed SyntheticTenantId this test configured above.
        var bridgeClient = _factory.CreateClient();
        var bridgeResponse = await bridgeClient.PostAsync("/api/v1/transport/primary-action", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, bridgeResponse.StatusCode);

        var admin = ClientAs(AdminPersonId);

        var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/transport-notifications/rules",
            new { name = "Board alerts", eventTypes = new[] { "board" } },
            ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var deliveriesResponse = await admin.GetAsync("/api/v1/transport-notifications/deliveries", ct);
        Assert.Equal(HttpStatusCode.OK, deliveriesResponse.StatusCode);
        var body = await deliveriesResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var deliveries = body.GetProperty("data").EnumerateArray().ToList();

        var boardDelivery = Assert.Single(deliveries, d => d.GetProperty("eventType").GetString() == "board");
        Assert.Equal("delivered", boardDelivery.GetProperty("outcome").GetString());

        // AC02 - the wire shape itself never carries the richer raw fields this story's own
        // redactor drops (see TransportNotificationPayloadRedactor's own header).
        Assert.False(boardDelivery.TryGetProperty("location", out _));
        Assert.False(boardDelivery.TryGetProperty("vehicleId", out _));
    }

    [Fact]
    public async Task Viewer_with_no_manage_grant_is_denied_creating_a_rule_a_real_403_not_a_blanket_rejection()
    {
        var ct = TestContext.Current.CancellationToken;
        var viewer = ClientAs(ViewerPersonId);

        var response = await viewer.PostAsJsonAsync(
            "/api/v1/transport-notifications/rules",
            new { name = "Should not be created", eventTypes = new[] { "board" } },
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var admin = ClientAs(AdminPersonId);
        var listResponse = await admin.GetAsync("/api/v1/transport-notifications/rules", ct);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.DoesNotContain(listBody.GetProperty("data").EnumerateArray(), r => r.GetProperty("name").GetString() == "Should not be created");
    }

    [Fact]
    public async Task With_no_actor_provenance_at_all_rule_creation_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor-Id", AdminPersonId);
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret));

        var response = await client.PostAsJsonAsync(
            "/api/v1/transport-notifications/rules",
            new { name = "Unauthorized", eventTypes = new[] { "board" } },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Toggling_a_rule_disabled_is_reflected_in_a_subsequent_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = ClientAs(AdminPersonId);

        var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/transport-notifications/rules",
            new { name = "Toggle test rule", eventTypes = new[] { "alight" } },
            ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var ruleId = created.GetProperty("id").GetString();
        var version = created.GetProperty("version").GetInt32();

        var toggleResponse = await admin.PutAsJsonAsync(
            $"/api/v1/transport-notifications/rules/{ruleId}/status",
            new { enabled = false, version },
            ct);
        Assert.Equal(HttpStatusCode.OK, toggleResponse.StatusCode);

        var listResponse = await admin.GetAsync("/api/v1/transport-notifications/rules", ct);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var rule = listBody.GetProperty("data").EnumerateArray().Single(r => r.GetProperty("id").GetString() == ruleId);
        Assert.False(rule.GetProperty("enabled").GetBoolean());
    }
}
