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
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-ATT-002's gateway
/// wiring reaches the real BIO-IDN-001 person store and BIO-ATT-001's own real event store (via
/// <c>BioAtt002Module</c>'s own <c>AttendanceEventQueryGatewayAdapter</c>/
/// <c>AttendanceDemoSeedGatewayAdapter</c>), and that the two AC05 UI-bridge routes this story
/// took over from BIO-ATT-001 answer a genuinely headerless request. This feature's two
/// authorization decisions (session policy, adjustments) are proven the identical "rejected on
/// authority grounds, same disclosed limitation as every sibling story's own host-channel tests"
/// way <c>AttendanceHostChannelTests.cs</c> already established for BIO-ATT-001's own policy
/// endpoint - the positive path for both is proven at the unit level with a fake permission
/// gateway.
/// </summary>
public sealed class AttendanceSessionsHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-att002";

    public AttendanceSessionsHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AttendanceSessions:ActorProvenanceSecret"] = ProvenanceSecret,
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

    private async Task<string> RegisterRealPersonAsync(HttpClient client, string hostname, CancellationToken ct)
    {
        var request = ActorRequest(HttpMethod.Post, "/api/v1/persons", hostname, "hr_admin",
            new { first_name = "Synthetic", last_name = "Demo Person", born_on = "2005-03-14" });
        var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return created.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task ATT002_creating_a_session_through_the_real_host_reaches_the_real_BIO_IDN_001_person_gateway_and_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att002-create-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c22", ct);
        var personId = await RegisterRealPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance-sessions/sessions", hostname, "teacher_1", new
        {
            session_type = "lecture",
            title = "Intro to Biology",
            scheduled_date = "2026-10-01",
            scheduled_start_time = "09:00",
            roster_person_ids = new[] { personId },
        }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(personId, body.GetProperty("roster_person_ids")[0].GetString());
    }

    [Fact]
    public async Task ATT002_creating_a_session_with_a_roster_person_that_does_not_exist_is_rejected_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att002-unknown-person-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c22", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance-sessions/sessions", hostname, "teacher_1", new
        {
            session_type = "class",
            title = "Roll call",
            scheduled_date = "2026-10-01",
            scheduled_start_time = "09:00",
            roster_person_ids = new[] { "per_0199e0c2-9999-7999-8999-999999999999" },
        }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATT002_AC01_AC03_a_roster_read_through_the_real_host_reflects_a_real_captured_BIO_ATT_001_event()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att002-roster-read-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c22", ct);
        var personId = await RegisterRealPersonAsync(client, hostname, ct);

        var createResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance-sessions/sessions", hostname, "teacher_1", new
        {
            session_type = "lecture",
            title = "Intro to Biology",
            scheduled_date = "2026-10-05",
            scheduled_start_time = "09:00",
            roster_person_ids = new[] { personId },
        }), ct);
        var session = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sessionId = session.GetProperty("session_id").GetString();

        // Capture a REAL BIO-ATT-001 event for this person through that feature's own endpoint.
        var captureResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance/events", hostname, "kiosk_device_1", new
        {
            person_id = personId,
            source = "device",
            device_id = "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03",
            location = "Lecture Hall B",
            mode = "check_in",
            evidence = new { method = "biometric", verified = true, confidence = 0.95 },
            occurred_at = "2026-10-05T09:05:00.000Z",
            idempotency_key = "host-e2e-att002-roster-1",
        }), ct);
        Assert.Equal(HttpStatusCode.OK, captureResponse.StatusCode);

        var rosterResponse = await client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/attendance-sessions/sessions/{sessionId}/roster", hostname, "teacher_1"), ct);
        Assert.Equal(HttpStatusCode.OK, rosterResponse.StatusCode);
        var roster = await rosterResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var member = roster.GetProperty("roster")[0];

        Assert.Equal("present", member.GetProperty("computed_status").GetString());
        var evidence = member.GetProperty("grounding_evidence")[0];
        Assert.Equal("biometric", evidence.GetProperty("evidence_method").GetString());
        Assert.True(evidence.GetProperty("evidence_verified").GetBoolean());
    }

    [Fact]
    public async Task ATT002_setting_the_session_policy_through_the_real_host_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att002-policy-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c22", ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/attendance-sessions/policy?session_type=exam", hostname, "unprivileged_attacker",
            new { late_grace_minutes = 5, absent_after_minutes = 10 }, withProvenance: true), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATT002_creating_an_adjustment_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att002-adjustment-provenance-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5c22", ct);
        var personId = await RegisterRealPersonAsync(client, hostname, ct);

        var createResponse = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance-sessions/sessions", hostname, "teacher_1", new
        {
            session_type = "lecture",
            title = "Intro to Biology",
            scheduled_date = "2026-10-06",
            scheduled_start_time = "09:00",
            roster_person_ids = new[] { personId },
        }), ct);
        var session = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sessionId = session.GetProperty("session_id").GetString();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/attendance-sessions/sessions/{sessionId}/adjustments", hostname, "unprivileged_attacker",
            new { person_id = personId, adjusted_status = "present", reason = "attempted forgery" }, withProvenance: false), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATT002_AC05_the_two_UI_bridge_routes_answer_a_genuinely_headerless_request_through_the_real_host()
    {
        // web/features/attendance/live.ts's fetch() sends no custom headers at all - proving these
        // two routes work with NO Host/X-Actor-Id/X-Actor-Provenance is the actual AC05 proof.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var primary = await client.PostAsync("/api/v1/attendance/primary-action", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
        var primaryBody = await primary.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Attendance:", primaryBody.GetProperty("message").GetString());

        var flag = await client.PostAsync("/api/v1/attendance/flag-exception", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, flag.StatusCode);
        var flagBody = await flag.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("Attendance:", flagBody.GetProperty("message").GetString());
    }
}
