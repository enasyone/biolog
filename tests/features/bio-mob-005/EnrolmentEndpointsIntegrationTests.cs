// Developer tests for BIO-MOB-005's six routes. SYNTHETIC fixtures only. A real HTTP pipeline
// (TestServer, real routing/model-binding/endpoint execution), mirroring BIO-MOB-003's/
// BIO-ACC-003's own integration-test discipline.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.OfflineEnrolment.Api;
using Biolog.Features.OfflineEnrolment.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.OfflineEnrolment.Tests;

public sealed class EnrolmentEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryDeviceCredentialStore _credentialStore = null!;
    private FakePermissionGateway _permissionGateway = null!;
    private const string ProvenanceSecret = "test-provenance-secret-32chars-min";

    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string OtherTenantId = "ten_018f2e5a-5678-7abc-89ab-0123456789cd";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceSecret = "test-device-secret";
    private const string ActorId = "usr_test_admin";

    public async ValueTask InitializeAsync()
    {
        _credentialStore = new InMemoryDeviceCredentialStore();
        _credentialStore.Register(DeviceId, DeviceSecret);
        _permissionGateway = new FakePermissionGateway();

        var provenanceStore = new HmacActorProvenanceStore();
        provenanceStore.Configure(ProvenanceSecret);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<IDeviceTenantResolutionGateway>(
                    new FakeDeviceTenantResolutionGateway().Register(DeviceId, TenantId));
                services.AddSingleton<IDeviceCredentialStore>(_credentialStore);
                services.AddSingleton<IDeviceAuthenticator, HeaderDeviceAuthenticator>();
                services.AddSingleton<Tenancy.ICurrentTenantProvider>(new FixedTenantProvider(TenantId));
                services.AddSingleton<IPermissionGateway>(_permissionGateway);
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<Audit.IEnrolmentAuditSink, Audit.InMemoryEnrolmentAuditSink>();
                services.AddSingleton<IEnrolmentCapturePolicyRepository, InMemoryEnrolmentCapturePolicyRepository>();
                services.AddSingleton<IEnrolmentPackageRepository, InMemoryEnrolmentPackageRepository>();
                services.AddSingleton<IEnrolmentDuplicateCandidateRepository, InMemoryEnrolmentDuplicateCandidateRepository>();
                services.AddSingleton<EnrolmentDuplicateReviewService>();
                services.AddScoped<EnrolmentPackageService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(EnrolmentEndpoints.Map);
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
    }

    private sealed class FixedTenantProvider(string tenantId) : Tenancy.ICurrentTenantProvider
    {
        public string? TenantId { get; } = tenantId;
    }

    private sealed class FakePermissionGateway : IPermissionGateway
    {
        public bool Grant { get; set; } = true;

        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Grant);
    }

    private static string ProvenanceProof(string actorId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private static string NewPackageId() => $"evt_{Guid.CreateVersion7():D}";

    private HttpRequestMessage DeviceRequest(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body);
        message.Headers.Add(HeaderDeviceAuthenticator.DeviceIdHeaderName, DeviceId);
        message.Headers.Add(HeaderDeviceAuthenticator.DeviceSecretHeaderName, DeviceSecret);
        if (idempotencyKey is not null) message.Headers.Add(EnrolmentEndpoints.IdempotencyKeyHeaderName, idempotencyKey);
        return message;
    }

    private HttpRequestMessage AdminRequest(HttpMethod method, string path, object? body = null)
    {
        var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body);
        message.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        message.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ProvenanceProof(ActorId));
        return message;
    }

    private static object Package(string packageId, Dictionary<string, string> fields) => new
    {
        package_id = packageId,
        tenant_id = TenantId,
        device_id = DeviceId,
        field_values = fields,
        photo_captured = false,
        biometric_modalities_captured = Array.Empty<string>(),
        biometric_payloads_base64 = new Dictionary<string, string>(),
        submitted_at = "2026-09-27T00:00:00.000Z",
    };

    // --- AC01: capture policy resolve/admin-write ---

    [Fact]
    public async Task Capture_policy_defaults_to_the_conservative_text_only_baseline_when_never_set()
    {
        var response = await _client.SendAsync(DeviceRequest(HttpMethod.Get, "/api/v1/enrolment/capture-policy"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var fields = body.GetProperty("allowed_fields").EnumerateArray().Select(f => f.GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "first_name", "last_name", "born_on" }, fields);
        Assert.False(body.GetProperty("allow_photo_capture").GetBoolean());
    }

    [Fact]
    public async Task An_admin_can_widen_the_capture_policy_and_the_device_sees_it_immediately()
    {
        var setResponse = await _client.SendAsync(
            AdminRequest(HttpMethod.Put, "/api/v1/enrolment/capture-policy", new
            {
                allowed_fields = new[] { "first_name", "last_name", "born_on", "institutional_identifier" },
                allow_photo_capture = true,
                allowed_biometric_modalities = new[] { "fingerprint" },
            }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var getResponse = await _client.SendAsync(DeviceRequest(HttpMethod.Get, "/api/v1/enrolment/capture-policy"), TestContext.Current.CancellationToken);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(body.GetProperty("allow_photo_capture").GetBoolean());
        Assert.Contains("fingerprint", body.GetProperty("allowed_biometric_modalities").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public async Task Setting_the_policy_to_an_unrecognised_field_is_rejected()
    {
        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Put, "/api/v1/enrolment/capture-policy", new { allowed_fields = new[] { "ssn" } }),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submitting_a_field_the_current_policy_does_not_permit_is_rejected_not_silently_captured()
    {
        var packageId = NewPackageId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(packageId, new() { ["institutional_identifier"] = "STU-1" }), packageId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("field_not_permitted", problem.GetProperty("code").GetString());
    }

    // --- AC03: duplicate/conflict checks re-run centrally after sync ---

    [Fact]
    public async Task A_package_with_no_signal_match_is_accepted_outright()
    {
        var packageId = NewPackageId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(packageId, new() { ["first_name"] = "Ada", ["last_name"] = "Obi", ["born_on"] = "2005-04-12" }), packageId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("ACCEPTED", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_second_package_matching_an_already_accepted_ones_name_and_date_of_birth_is_held_for_review()
    {
        var firstId = NewPackageId();
        var fields = new Dictionary<string, string> { ["first_name"] = "Ada", ["last_name"] = "Obi", ["born_on"] = "2005-04-12" };
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(firstId, fields), firstId), TestContext.Current.CancellationToken);

        var secondId = NewPackageId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(secondId, new(fields)), secondId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("PENDING_REVIEW", body.GetProperty("status").GetString());

        var candidates = await AdminGet("/api/v1/enrolment/duplicate-candidates");
        var items = candidates.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(secondId, items[0].GetProperty("package_id").GetString());
        Assert.Equal(firstId, items[0].GetProperty("matched_package_id").GetString());
        Assert.Equal("NAME_AND_DATE_OF_BIRTH", items[0].GetProperty("signal_source").GetString());
    }

    [Fact]
    public async Task Two_packages_that_share_only_an_institutional_identifier_are_flagged_by_that_signal()
    {
        var firstId = NewPackageId();
        await _client.SendAsync(
            AdminRequest(HttpMethod.Put, "/api/v1/enrolment/capture-policy", new { allowed_fields = new[] { "first_name", "last_name", "born_on", "institutional_identifier" } }),
            TestContext.Current.CancellationToken);
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(firstId, new() { ["first_name"] = "Ada", ["institutional_identifier"] = "STU-42" }), firstId), TestContext.Current.CancellationToken);

        var secondId = NewPackageId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(secondId, new() { ["first_name"] = "Chidi", ["institutional_identifier"] = "STU-42" }), secondId),
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("PENDING_REVIEW", body.GetProperty("status").GetString());
    }

    // --- AC04: security conflicts require human resolution ---

    [Fact]
    public async Task Confirming_a_candidate_distinct_accepts_the_held_package()
    {
        var firstId = NewPackageId();
        var fields = new Dictionary<string, string> { ["first_name"] = "Ada", ["last_name"] = "Obi", ["born_on"] = "2005-04-12" };
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(firstId, fields), firstId), TestContext.Current.CancellationToken);
        var secondId = NewPackageId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(secondId, new(fields)), secondId), TestContext.Current.CancellationToken);
        var candidateId = (await AdminGet("/api/v1/enrolment/duplicate-candidates")).GetProperty("items").EnumerateArray().First().GetProperty("candidate_id").GetString();

        var decision = await _client.SendAsync(
            AdminRequest(HttpMethod.Post, $"/api/v1/enrolment/duplicate-candidates/{candidateId}/decisions", new { decision = "CONFIRMED_DISTINCT", reason_code = "manually_verified_distinct" }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        var packages = await AdminGet("/api/v1/enrolment/packages?status=ACCEPTED");
        var packageIds = packages.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("package_id").GetString()).ToList();
        Assert.Contains(secondId, packageIds);
    }

    [Fact]
    public async Task Confirming_a_candidate_a_duplicate_rejects_the_held_package()
    {
        var firstId = NewPackageId();
        var fields = new Dictionary<string, string> { ["first_name"] = "Ada", ["last_name"] = "Obi", ["born_on"] = "2005-04-12" };
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(firstId, fields), firstId), TestContext.Current.CancellationToken);
        var secondId = NewPackageId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(secondId, new(fields)), secondId), TestContext.Current.CancellationToken);
        var candidateId = (await AdminGet("/api/v1/enrolment/duplicate-candidates")).GetProperty("items").EnumerateArray().First().GetProperty("candidate_id").GetString();

        await _client.SendAsync(
            AdminRequest(HttpMethod.Post, $"/api/v1/enrolment/duplicate-candidates/{candidateId}/decisions", new { decision = "CONFIRMED_DUPLICATE", reason_code = "same_person_reenrolled" }),
            TestContext.Current.CancellationToken);

        var packages = await AdminGet("/api/v1/enrolment/packages?status=REJECTED_DUPLICATE");
        var packageIds = packages.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("package_id").GetString()).ToList();
        Assert.Contains(secondId, packageIds);
    }

    [Fact]
    public async Task A_decision_with_no_reason_code_is_rejected()
    {
        var firstId = NewPackageId();
        var fields = new Dictionary<string, string> { ["first_name"] = "Ada", ["last_name"] = "Obi", ["born_on"] = "2005-04-12" };
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(firstId, fields), firstId), TestContext.Current.CancellationToken);
        var secondId = NewPackageId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(secondId, new(fields)), secondId), TestContext.Current.CancellationToken);
        var candidateId = (await AdminGet("/api/v1/enrolment/duplicate-candidates")).GetProperty("items").EnumerateArray().First().GetProperty("candidate_id").GetString();

        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Post, $"/api/v1/enrolment/duplicate-candidates/{candidateId}/decisions", new { decision = "CONFIRMED_DISTINCT", reason_code = "" }),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task Deciding_an_already_decided_candidate_is_rejected()
    {
        var firstId = NewPackageId();
        var fields = new Dictionary<string, string> { ["first_name"] = "Ada", ["last_name"] = "Obi", ["born_on"] = "2005-04-12" };
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(firstId, fields), firstId), TestContext.Current.CancellationToken);
        var secondId = NewPackageId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(secondId, new(fields)), secondId), TestContext.Current.CancellationToken);
        var candidateId = (await AdminGet("/api/v1/enrolment/duplicate-candidates")).GetProperty("items").EnumerateArray().First().GetProperty("candidate_id").GetString();

        await _client.SendAsync(AdminRequest(HttpMethod.Post, $"/api/v1/enrolment/duplicate-candidates/{candidateId}/decisions", new { decision = "CONFIRMED_DISTINCT", reason_code = "r1" }), TestContext.Current.CancellationToken);
        var second = await _client.SendAsync(AdminRequest(HttpMethod.Post, $"/api/v1/enrolment/duplicate-candidates/{candidateId}/decisions", new { decision = "CONFIRMED_DISTINCT", reason_code = "r2" }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Reviewing_duplicate_candidates_without_the_required_permission_is_forbidden()
    {
        _permissionGateway.Grant = false;
        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, "/api/v1/enrolment/duplicate-candidates"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _permissionGateway.Grant = true;
    }

    // --- AC05: reconnect sync uploads packages exactly once ---

    [Fact]
    public async Task Replaying_the_identical_package_returns_the_original_result_not_a_duplicate()
    {
        var packageId = NewPackageId();
        var body = Package(packageId, new() { ["first_name"] = "Ada" });

        var first = await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", body, packageId), TestContext.Current.CancellationToken);
        var second = await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", body, packageId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues(EnrolmentEndpoints.IdempotencyReplayedHeaderName).Single());

        var packages = await AdminGet("/api/v1/enrolment/packages?status=ACCEPTED");
        Assert.Single(packages.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Reusing_the_same_package_id_with_different_content_is_rejected_not_recorded_twice()
    {
        var packageId = NewPackageId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(packageId, new() { ["first_name"] = "Ada" }), packageId), TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(packageId, new() { ["first_name"] = "Chidi" }), packageId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_reuse", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_idempotency_key_on_intake_is_rejected()
    {
        var packageId = NewPackageId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", Package(packageId, new() { ["first_name"] = "Ada" })),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    [Fact]
    public async Task Package_submission_without_device_credentials_is_rejected_with_a_generic_401()
    {
        var packageId = NewPackageId();
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/enrolment/packages") { Content = JsonContent.Create(Package(packageId, new() { ["first_name"] = "Ada" })) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_authentication_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_payload_tenant_id_that_does_not_match_the_devices_resolved_tenant_is_rejected()
    {
        var packageId = NewPackageId();
        var forged = new
        {
            package_id = packageId,
            tenant_id = OtherTenantId,
            device_id = DeviceId,
            field_values = new Dictionary<string, string> { ["first_name"] = "Ada" },
            photo_captured = false,
            biometric_modalities_captured = Array.Empty<string>(),
            biometric_payloads_base64 = new Dictionary<string, string>(),
            submitted_at = "2026-09-27T00:00:00.000Z",
        };
        var response = await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/enrolment/packages", forged, packageId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("tenant_mismatch", problem.GetProperty("code").GetString());
    }

    private async Task<JsonElement> AdminGet(string path)
    {
        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, path), TestContext.Current.CancellationToken);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
