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
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-ATT-001's gateway
/// wiring reaches the real BIO-IDN-001 person store and that capture/read genuinely succeed for
/// any asserted actor (ADR-0006 Decision 2 - unlike BIO-ACC-001's own host-channel tests, which
/// can only ever show REJECTION since every one of its endpoints is an authorization decision
/// with no RBAC-seeding backdoor available to this test project). The one authorization decision
/// this feature makes - <c>PUT /api/v1/attendance/policy</c> - is proven the identical
/// "rejected on authority grounds, same disclosed limitation as every sibling story's own
/// host-channel tests" way <c>AccessPolicyHostChannelTests.cs</c> already established.
/// </summary>
public sealed class AttendanceHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-att001";

    public AttendanceHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Attendance:ActorProvenanceSecret"] = ProvenanceSecret,
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
    public async Task ATT001_capturing_a_real_event_through_the_real_host_reaches_the_real_BIO_IDN_001_person_gateway_and_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att001-capture-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var personId = await RegisterRealPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance/events", hostname, "kiosk_device_1", new
        {
            person_id = personId,
            source = "device",
            device_id = "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03",
            location = "Main Gate",
            mode = "check_in",
            evidence = new { method = "biometric", verified = true, confidence = 0.97 },
            occurred_at = "2026-09-24T08:00:00.000Z",
            idempotency_key = "host-e2e-capture-1",
        }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(personId, body.GetProperty("person_id").GetString());
        Assert.Equal("check_in", body.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task ATT001_capturing_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att001-unknown-person-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance/events", hostname, "kiosk_device_1", new
        {
            person_id = "per_0199e0c2-9999-7999-8999-999999999999",
            source = "manual",
            mode = "check_in",
            evidence = new { method = "manual", verified = false },
            occurred_at = "2026-09-24T08:00:00.000Z",
            idempotency_key = "host-e2e-unknown-person",
        }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATT001_AC03_identical_redelivery_through_the_real_host_returns_the_same_event_id()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att001-replay-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var personId = await RegisterRealPersonAsync(client, hostname, ct);

        object CapturePayload() => new
        {
            person_id = personId,
            source = "manual",
            mode = "check_in",
            evidence = new { method = "manual", verified = false },
            occurred_at = "2026-09-24T09:00:00.000Z",
            idempotency_key = "host-e2e-replay-1",
        };

        var first = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance/events", hostname, "kiosk_device_1", CapturePayload()), ct);
        var second = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/attendance/events", hostname, "kiosk_device_1", CapturePayload()), ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(firstBody.GetProperty("event_id").GetString(), secondBody.GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task ATT001_setting_the_policy_through_the_real_host_is_rejected_for_lack_of_authority()
    {
        // Same disclosed limitation as AccessPolicyHostChannelTests: this project has no
        // InternalsVisibleTo seam into BIO-IDN-006 to grant a real permission, so this can only
        // ever demonstrate the rejection path - the positive path is proven at the unit level
        // (AttendancePolicyServiceTests.cs) with a fake permission gateway.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att001-policy-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/attendance/policy", hostname, "unprivileged_attacker",
            new { interpretation_mode = "education", expected_start_time = "08:00", late_grace_minutes = 10 }, withProvenance: true), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATT001_setting_the_policy_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "att001-policy-provenance-e2e.example.edu.ng", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/attendance/policy", hostname, "unprivileged_attacker",
            new { interpretation_mode = "education", expected_start_time = "08:00", late_grace_minutes = 10 }, withProvenance: false), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    // AC05's own two UI-bridge-route host-channel proof moved to BIO-ATT-002's own
    // AttendanceSessionsHostChannelTests.cs (this feature no longer maps primary-action/
    // flag-exception - see AttendanceEndpoints.cs's own header for the disclosed handoff).
}
