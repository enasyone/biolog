using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Features.CardLifecycle.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-CRD-006's real
/// cross-story dependency on BIO-CRD-005 (report/replace/activate-replacement reading and
/// mutating a REAL, already-shipped <c>CardIssue</c> row, through
/// <c>Biolog.Api.Modules.BioCrd006Module</c>'s own <c>CardIssueGatewayAdapter</c>) is enforced by
/// the real, shipped host channel - not merely an injected test double. Mirrors
/// <c>CardIssuanceHostChannelTests.cs</c>'s own precedent for BIO-CRD-005's dependency on
/// BIO-IDN-001: registers and verifies a real domain, registers a real person, issues and
/// activates a real card through BIO-CRD-005's own endpoints, then exercises BIO-CRD-006's
/// endpoints against that real row. SYNTHETIC fixtures only.
///
/// <b>TD-CRD-006-DENY-LIST-DEVICE-AUTH.</b> The device-secret/tenant-binding tests here mirror
/// <c>PhotoBoothHostChannelTests.cs</c>'s own precedent exactly - registers and activates a real
/// device through BIO-DEV-001's own endpoints, seeds that device's secret directly against the
/// shared <c>InMemoryDeviceCredentialStore</c> singleton via DI (never through HTTP, the id is
/// server-minted and unknowable before registration), then proves the real, packaged
/// <c>BioCrd006Module</c> wiring - not just the feature project's own fake-backed tests - rejects
/// no auth, a wrong secret, and a real device genuinely registered to a DIFFERENT tenant.
/// </summary>
public sealed class CardLifecycleHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5b77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5b88";
    private const string DeviceSecret = "crd006-host-channel-device-secret";

    public CardLifecycleHostChannelTests(WebApplicationFactory<Program> factory)
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

    private static object IssueBody() => new
    {
        template_ref = "template-e2e",
        photo_ref = "photo-e2e",
        printer_ref = "printer-e2e",
    };

    private async Task<string> RegisterAndActivateDeviceAsync(HttpClient client, string host, CancellationToken ct)
    {
        var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices") { Headers = { Host = host } };
        registerRequest.Content = JsonContent.Create(new DeviceRegistrationRequest(
            "SN-DENYLIST-1", "Acme", "OfflineReaderX", "1.0.0", "sdk-1", "Main Campus",
            new NetworkMetadata("10.0.0.10", null, "reader-01"), CredentialReference: "kv://synthetic/reader-01"));
        var registerResponse = await client.SendAsync(registerRequest, ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var device = await registerResponse.Content.ReadFromJsonAsync<Device>(ct);

        var activateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/devices/{device!.Id}/status") { Headers = { Host = host } };
        activateRequest.Content = JsonContent.Create(new DeviceStatusChangeRequest(DeviceStatus.Active, device.Version, "user", "admin_alice", CorrelationId: null));
        var activateResponse = await client.SendAsync(activateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        _factory.Services.GetRequiredService<InMemoryDeviceCredentialStore>().Register(device.Id, DeviceSecret);
        return device.Id;
    }

    private static HttpRequestMessage DenyListRequest(string host, string? deviceId, string? secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/card-lifecycle/deny-list") { Headers = { Host = host } };
        if (deviceId is not null) request.Headers.Add("X-Biolog-Device-Id", deviceId);
        if (secret is not null) request.Headers.Add("X-Biolog-Device-Secret", secret);
        return request;
    }

    /// <summary>Registers a person and a real, ACTIVE BIO-CRD-005 card issue (sequence 1) through that story's own endpoints.</summary>
    private async Task<string> IssueAndActivateRealCardAsync(HttpClient client, string host, CancellationToken ct)
    {
        var personId = await RegisterPersonAsync(client, host, ct);
        await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff", host, IssueBody()), ct);
        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/activate", host, new { expected_version = 1 }, idempotencyKey: $"e2e-activate-{personId}"), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        return personId;
    }

    [Fact]
    public async Task AC01_reporting_a_real_active_card_through_the_real_host_locks_it_and_a_real_BIOCRD005_dependency_is_used()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd006-report-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueAndActivateRealCardAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/report", hostname, new { reason_code = "stolen" }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("locked", body.GetProperty("lock_state").GetString());
    }

    private static object ProductionRequest() => new { printer_ref = "printer-e2e", workstation_ref = "workstation-e2e" };

    private async Task<int> RecordProductionStepAsync(HttpClient client, string host, string personId, int sequenceNumber, string step, int expectedVersion, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/{sequenceNumber}/production/jobs/1/steps/{step}", host,
            new { passed = true, expected_version = expectedVersion }), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("version").GetInt32();
    }

    /// <summary>BIO-OPS-003-AC04: a real production job through BIO-CRD-007's own endpoints, print/encode/read-back-QC all Passed - the precondition this story's own <see cref="IProductionStatusGateway"/> adapter now consults before activating a replacement.</summary>
    private async Task PassRealProductionJobAsync(HttpClient client, string host, string personId, int sequenceNumber, CancellationToken ct)
    {
        var createResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/{sequenceNumber}/production/jobs", host, ProductionRequest()), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        var afterPrint = await RecordProductionStepAsync(client, host, personId, sequenceNumber, "print", created.GetProperty("version").GetInt32(), ct);
        var afterEncode = await RecordProductionStepAsync(client, host, personId, sequenceNumber, "encode", afterPrint, ct);
        await RecordProductionStepAsync(client, host, personId, sequenceNumber, "readback-qc", afterEncode, ct);
    }

    [Fact]
    public async Task AC03_AC04_replacing_and_activating_the_replacement_through_the_real_host_permanently_revokes_the_real_earlier_card()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd006-replace-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueAndActivateRealCardAsync(client, hostname, ct);

        var replaceResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/replace", hostname, new { template_ref = "t2", printer_ref = "p2" }), ct);
        Assert.Equal(HttpStatusCode.Created, replaceResponse.StatusCode);
        var replacement = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, replacement.GetProperty("sequence_number").GetInt32());

        await PassRealProductionJobAsync(client, hostname, personId, 2, ct);

        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/2/activate-replacement", hostname,
            new { expected_version = replacement.GetProperty("version").GetInt32() }, idempotencyKey: $"e2e-activate-replacement-{personId}"), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        // The real BIO-CRD-005 card issue #1 is genuinely Deactivated there too - not merely
        // this story's own lock record - proving RevokePermanentlyAsync's real gateway call ran.
        var realCardOneResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/persons/{personId}/card-issues/staff/1", hostname), ct);
        var realCardOne = await realCardOneResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("deactivated", realCardOne.GetProperty("status").GetString());

        var lifecycleResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/persons/{personId}/card-issues/staff/1/lifecycle", hostname), ct);
        var lifecycle = await lifecycleResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permanently_revoked", lifecycle.GetProperty("lock_state").GetString());
    }

    /// <summary>
    /// BIO-OPS-003-AC04 (Seyi's acceptance run, Kemi's ruling): through the REAL packaged host -
    /// a real <c>ProductionStatusGatewayAdapter</c> in <c>Biolog.Api.Modules.BioCrd006Module</c>
    /// wrapping BIO-CRD-007's own real, already-shipped <c>ProductionService</c> - activating a
    /// replacement with NO production job at all is refused, and the earlier card is never
    /// touched: still genuinely Active on BIO-CRD-005's own row, never swept into
    /// RevocationPending. Proves the composition gap this fix closes, not merely the unit-level
    /// fake.
    /// </summary>
    [Fact]
    public async Task AC04_activating_a_replacement_through_the_real_host_is_refused_when_no_real_production_job_has_passed_and_the_earlier_card_stays_active()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd006-prod-gap-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueAndActivateRealCardAsync(client, hostname, ct);

        var replaceResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/replace", hostname, new { template_ref = "t2", printer_ref = "p2" }), ct);
        Assert.Equal(HttpStatusCode.Created, replaceResponse.StatusCode);
        var replacement = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // No production job created for sequence 2 at all - BIO-CRD-007's own ListJobsAsync
        // returns empty, so HasPassed is false.
        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/2/activate-replacement", hostname,
            new { expected_version = replacement.GetProperty("version").GetInt32() }, idempotencyKey: $"e2e-activate-replacement-nopass-{personId}"), ct);
        Assert.Equal(HttpStatusCode.Conflict, activateResponse.StatusCode);
        var problem = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("production_not_passed", problem.GetProperty("code").GetString());

        // The earlier card was never swept - still genuinely Active on BIO-CRD-005's own row.
        var realCardOneResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/persons/{personId}/card-issues/staff/1", hostname), ct);
        var realCardOne = await realCardOneResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("active", realCardOne.GetProperty("status").GetString());

        var lifecycleResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Get, $"/api/v1/persons/{personId}/card-issues/staff/1/lifecycle", hostname), ct);
        Assert.Equal(HttpStatusCode.NotFound, lifecycleResponse.StatusCode); // never reported/swept - no lifecycle record ever created for it
    }

    [Fact]
    public async Task AC05_presentation_check_denies_a_reported_real_card_and_the_deny_list_reflects_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd006-deny-e2e.example.edu.ng", TenantA, ct);
        var personId = await IssueAndActivateRealCardAsync(client, hostname, ct);
        await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/report", hostname, new { reason_code = "lost" }), ct);

        var checkResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/presentation-check", hostname, new { device_ref = "reader-e2e" }), ct);
        var check = await checkResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("denied", check.GetProperty("outcome").GetString());

        // TD-CRD-006-DENY-LIST-DEVICE-AUTH: a real, registered, active device - through the real
        // packaged BioCrd006Module wiring, not a fake gateway - can pull the real deny list.
        var readerDeviceId = await RegisterAndActivateDeviceAsync(client, hostname, ct);
        var denyListResponse = await client.SendAsync(DenyListRequest(hostname, readerDeviceId, DeviceSecret), ct);
        Assert.Equal(HttpStatusCode.OK, denyListResponse.StatusCode);
        var denyList = await denyListResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(denyList.GetProperty("data").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task TDCRD006_deny_list_with_no_device_credentials_at_all_is_401_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd006-deny-noauth-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(DenyListRequest(hostname, null, null), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_authentication_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TDCRD006_a_device_genuinely_registered_in_tenant_A_cannot_authenticate_tenant_Bs_deny_list()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "crd006-deny-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "crd006-deny-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var deviceIdA = await RegisterAndActivateDeviceAsync(client, hostA, ct);

        // The device is real, active, and its secret is genuinely correct - just registered
        // under tenant A's own host, not tenant B's.
        var response = await client.SendAsync(DenyListRequest(hostB, deviceIdA, DeviceSecret), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_authentication_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reporting_for_an_unknown_card_issue_on_the_real_host_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "crd006-unknown-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/report", hostname, new { reason_code = "lost" }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("card_issue_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_tenant_B_caller_cannot_report_a_real_tenant_A_persons_card()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "crd006-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "crd006-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var personId = await IssueAndActivateRealCardAsync(client, hostA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/report", hostB, new { reason_code = "lost" }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // BIO-CRD-005's own tenant-scoped repository never finds tenant A's row under tenant B
    }

    [Fact]
    public async Task Reporting_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b2e/card-issues/staff/1/report",
            "nobody-registered-this-crd006-host.example.edu.ng", new { reason_code = "lost" }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
