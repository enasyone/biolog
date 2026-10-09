// Developer tests for BIO-MOB-004's five routes. SYNTHETIC fixtures only. A real HTTP pipeline
// (TestServer, real routing/model-binding/endpoint execution), mirroring BIO-MOB-005's own
// integration-test discipline. BIO-ACC-002's own decision engine is faked at this project's
// boundary (FakeAccessDecisionGateway.cs's own header explains why) - this file proves BIO-MOB-004's
// own device-auth/role-gate/override/sanitised-response behaviour, not BIO-ACC-002's.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.MobileVerification.Api;
using Biolog.Features.MobileVerification.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.MobileVerification.Tests;

public sealed class VerificationEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryDeviceCredentialStore _credentialStore = null!;
    private FakePermissionGateway _permissionGateway = null!;
    private FakeAccessDecisionGateway _decisionGateway = null!;
    private const string ProvenanceSecret = "test-provenance-secret-32chars-min";

    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceSecret = "test-device-secret";
    private const string PersonId = "per_018f2e5a-aaaa-7abc-89ab-0123456789ab";
    private const string ActorId = "usr_test_admin";

    public async ValueTask InitializeAsync()
    {
        _credentialStore = new InMemoryDeviceCredentialStore();
        _credentialStore.Register(DeviceId, DeviceSecret);
        _permissionGateway = new FakePermissionGateway();
        _decisionGateway = new FakeAccessDecisionGateway();

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
                services.AddSingleton<Audit.IVerificationAuditSink, Audit.InMemoryVerificationAuditSink>();
                services.AddSingleton<IVerificationDeviceRoleRepository, InMemoryVerificationDeviceRoleRepository>();
                services.AddSingleton<IVerificationOverrideRepository, InMemoryVerificationOverrideRepository>();
                services.AddSingleton<IAccessDecisionGateway>(_decisionGateway);
                services.AddScoped<VerificationService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(VerificationEndpoints.Map);
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

    private static object DecisionRequest(string? personId = PersonId) => new
    {
        person_id = personId,
        credential_class = "card",
        location_id = "main-gate",
        presented_card_status = "ACTIVE",
        policy_key = "default",
    };

    private HttpRequestMessage DeviceRequest(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body);
        message.Headers.Add(HeaderDeviceAuthenticator.DeviceIdHeaderName, DeviceId);
        message.Headers.Add(HeaderDeviceAuthenticator.DeviceSecretHeaderName, DeviceSecret);
        if (idempotencyKey is not null) message.Headers.Add(VerificationEndpoints.IdempotencyKeyHeaderName, idempotencyKey);
        return message;
    }

    private HttpRequestMessage AdminRequest(HttpMethod method, string path, object? body = null, bool includeActor = true)
    {
        var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body);
        if (includeActor)
        {
            message.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
            message.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ProvenanceProof(ActorId));
        }
        return message;
    }

    // --- AC01: sanitised decision response ---

    [Fact]
    public async Task A_decision_request_returns_no_biometric_evidence_field_at_all()
    {
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/mobile-verification/decisions", DecisionRequest(), "evt_018f2e5a-0001-7abc-89ab-0123456789ab"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(body.TryGetProperty("biometric_evidence", out _));
        Assert.False(body.TryGetProperty("score", out _));
        Assert.Equal("allow", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Requesting_a_decision_without_device_credentials_is_rejected()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/mobile-verification/decisions") { Content = JsonContent.Create(DecisionRequest()) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_authentication_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_malformed_person_id_is_rejected()
    {
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/mobile-verification/decisions", DecisionRequest(personId: "not-a-canonical-id"), "evt_018f2e5a-0002-7abc-89ab-0123456789ab"),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    // --- AC04: idempotent sync ---

    [Fact]
    public async Task A_decision_request_without_an_Idempotency_Key_header_is_rejected()
    {
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/mobile-verification/decisions", DecisionRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_Idempotency_Key_header_is_passed_through_to_the_downstream_gateway_unchanged()
    {
        const string key = "evt_018f2e5a-0003-7abc-89ab-0123456789ab";
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/mobile-verification/decisions", DecisionRequest(), key),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = Assert.Single(_decisionGateway.DecideCalls);
        Assert.Equal(key, call.Query.IdempotencyKey);
        Assert.Equal(TenantId, call.TenantId);
    }

    // --- AC03: actions obey role permissions ---

    [Fact]
    public async Task An_unassigned_device_defaults_to_Officer_and_may_execute_a_decision()
    {
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/mobile-verification/decisions", DecisionRequest(), "evt_018f2e5a-0004-7abc-89ab-0123456789ab"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_unassigned_device_defaults_to_Officer_and_may_read_history()
    {
        _decisionGateway.HistoryToReturn = [];
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Get, $"/api/v1/mobile-verification/decisions?person_id={PersonId}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unassigned_Officer_device_may_not_override_a_decision()
    {
        var response = await _client.SendAsync(
            DeviceRequest(
                HttpMethod.Post,
                "/api/v1/mobile-verification/decisions/evt_018f2e5a-0005-7abc-89ab-0123456789ab/override",
                new { person_id = PersonId, reason_code = "supervisor_manual_admit" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("role_forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_admin_can_assign_the_Supervisor_role_and_the_device_can_then_override()
    {
        var setRoleResponse = await _client.SendAsync(
            AdminRequest(HttpMethod.Put, $"/api/v1/mobile-verification/devices/{DeviceId}/role", new { role = "SUPERVISOR" }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, setRoleResponse.StatusCode);

        var roleResponse = await _client.SendAsync(
            DeviceRequest(HttpMethod.Get, "/api/v1/mobile-verification/device-role"),
            TestContext.Current.CancellationToken);
        var roleBody = await roleResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("SUPERVISOR", roleBody.GetProperty("role").GetString());

        var overrideResponse = await _client.SendAsync(
            DeviceRequest(
                HttpMethod.Post,
                "/api/v1/mobile-verification/decisions/evt_018f2e5a-0006-7abc-89ab-0123456789ab/override",
                new { person_id = PersonId, reason_code = "supervisor_manual_admit" }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, overrideResponse.StatusCode);
    }

    [Fact]
    public async Task Overriding_without_a_reason_code_is_rejected()
    {
        await _client.SendAsync(
            AdminRequest(HttpMethod.Put, $"/api/v1/mobile-verification/devices/{DeviceId}/role", new { role = "SUPERVISOR" }),
            TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            DeviceRequest(
                HttpMethod.Post,
                "/api/v1/mobile-verification/decisions/evt_018f2e5a-0007-7abc-89ab-0123456789ab/override",
                new { person_id = PersonId, reason_code = "" }),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("override_reason_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assigning_a_device_role_without_the_required_permission_is_forbidden()
    {
        _permissionGateway.Grant = false;

        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Put, $"/api/v1/mobile-verification/devices/{DeviceId}/role", new { role = "SUPERVISOR" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assigning_a_device_role_without_an_actor_id_is_rejected()
    {
        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Put, $"/api/v1/mobile-verification/devices/{DeviceId}/role", new { role = "SUPERVISOR" }, includeActor: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assigning_an_unrecognised_role_value_is_rejected()
    {
        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Put, $"/api/v1/mobile-verification/devices/{DeviceId}/role", new { role = "MANAGER" }),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("invalid_device_role", problem.GetProperty("code").GetString());
    }
}
