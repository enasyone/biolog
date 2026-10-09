// Developer tests for BIO-ACC-003's four routes. SYNTHETIC fixtures only. A real HTTP pipeline
// (TestServer, real routing/model-binding/endpoint execution), mirroring BIO-MOB-003's own
// integration-test discipline (Chioma's acceptance round 1 on that story: a mocked-client test
// alone is not evidence a central endpoint actually works).
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.AccessCache.Api;
using Biolog.Features.AccessCache.Audit;
using Biolog.Features.AccessCache.Demo;
using Biolog.Features.AccessCache.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.AccessCache.Tests;

public sealed class AccessCacheEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryDeviceCredentialStore _credentialStore = null!;
    private InMemoryPresentationEventLog _eventLog = null!;
    private FakePermissionGateway _permissionGateway = null!;
    private const string ProvenanceSecret = "test-provenance-secret-32chars-min";

    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string OtherTenantId = "ten_018f2e5a-5678-7abc-89ab-0123456789cd";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceSecret = "test-device-secret";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string ActorId = "usr_test_reviewer";

    public async ValueTask InitializeAsync()
    {
        _credentialStore = new InMemoryDeviceCredentialStore();
        _credentialStore.Register(DeviceId, DeviceSecret);
        _eventLog = new InMemoryPresentationEventLog();
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
                services.AddSingleton<IIdentityRevocationFeedGateway>(new FakeIdentityRevocationFeedGateway());
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<IPresentationEventLog>(_eventLog);
                services.AddSingleton<IAccessCacheAuditSink, InMemoryAccessCacheAuditSink>();
                services.AddScoped<AccessCacheService>();
                services.AddSingleton<AccessCacheDemoBridgeService>();
                services.AddSingleton<Tenancy.ICurrentTenantProvider>(new FixedTenantProvider(TenantId));
                services.AddSingleton<IPermissionGateway>(_permissionGateway);
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(AccessCacheEndpoints.Map);
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

    private static string NewEventId() => $"evt_{Guid.CreateVersion7():D}";

    // --- AC01/AC02: revocation-delta pull ---

    [Fact]
    public async Task Revocation_delta_returns_entries_registered_for_this_devices_own_tenant_only()
    {
        var gateway = new FakeIdentityRevocationFeedGateway()
            .Register(TenantId, new RevocationEntry(PersonId, "2026-09-24T00:00:00.000Z"))
            .Register(OtherTenantId, new RevocationEntry("per_other_tenant_person_0000000000", "2026-09-24T00:00:00.000Z"));
        await using var scope = await StartWithGateway(gateway);

        var response = await scope.Client.SendAsync(
            DeviceRequest(HttpMethod.Get, "/api/v1/access-cache/revocation-deltas"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(PersonId, items[0].GetProperty("person_id").GetString());
    }

    [Fact]
    public async Task Revocation_delta_without_device_credentials_is_rejected_with_a_generic_401()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/access-cache/revocation-deltas"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_authentication_failed", problem.GetProperty("code").GetString());
    }

    // --- AC05: presentation-event intake, exactly once ---

    private static object PresentationEvent(string eventId, string personId = PersonId, string outcome = "DENIED", string reasonCode = "DENIED_REVOKED_CACHED") => new
    {
        event_id = eventId,
        tenant_id = TenantId,
        device_id = DeviceId,
        person_id = personId,
        outcome,
        reason_code = reasonCode,
        occurred_at = "2026-09-24T00:00:00.000Z",
        cache_snapshot_at = "2026-09-23T12:00:00.000Z",
        stale = false,
    };

    private HttpRequestMessage DeviceRequest(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body);
        message.Headers.Add(HeaderDeviceAuthenticator.DeviceIdHeaderName, DeviceId);
        message.Headers.Add(HeaderDeviceAuthenticator.DeviceSecretHeaderName, DeviceSecret);
        if (idempotencyKey is not null) message.Headers.Add(AccessCacheEndpoints.IdempotencyKeyHeaderName, idempotencyKey);
        return message;
    }

    [Fact]
    public async Task A_presentation_event_is_accepted_and_returns_201()
    {
        var eventId = NewEventId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", PresentationEvent(eventId), eventId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(eventId, ack.GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task Replaying_the_identical_event_returns_the_original_result_not_a_duplicate()
    {
        var eventId = NewEventId();
        var body = PresentationEvent(eventId);

        var first = await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", body, eventId), TestContext.Current.CancellationToken);
        var second = await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", body, eventId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues(AccessCacheEndpoints.IdempotencyReplayedHeaderName).Single());

        // Exactly once: only one event durably recorded, not two.
        var stored = await _eventLog.ListRecentAsync(TenantId, 10, TestContext.Current.CancellationToken);
        Assert.Single(stored);
    }

    [Fact]
    public async Task Reusing_the_same_event_id_with_different_content_is_rejected_not_recorded_twice()
    {
        var eventId = NewEventId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", PresentationEvent(eventId), eventId), TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", PresentationEvent(eventId, outcome: "ALLOWED", reasonCode: "ALLOWED_NOT_REVOKED"), eventId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_reuse", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_payload_tenant_id_that_does_not_match_the_devices_resolved_tenant_is_rejected()
    {
        var eventId = NewEventId();
        var forged = new
        {
            event_id = eventId,
            tenant_id = OtherTenantId,
            device_id = DeviceId,
            person_id = PersonId,
            outcome = "DENIED",
            reason_code = "DENIED_REVOKED_CACHED",
            occurred_at = "2026-09-24T00:00:00.000Z",
            cache_snapshot_at = (string?)null,
            stale = false,
        };
        var response = await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", forged, eventId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("tenant_mismatch", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_idempotency_key_on_intake_is_rejected()
    {
        var eventId = NewEventId();
        var response = await _client.SendAsync(
            DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", PresentationEvent(eventId)),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    // --- AC06 human read: tenant/actor/provenance/permission gated ---

    [Fact]
    public async Task Presentation_events_read_without_an_actor_header_is_rejected()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/access-cache/presentation-events"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Presentation_events_read_with_an_actor_but_no_provenance_proof_is_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/access-cache/presentation-events");
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_provenance_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Presentation_events_read_without_the_required_permission_is_forbidden()
    {
        _permissionGateway.Grant = false;
        var request = AuthedReadRequest();
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("forbidden", problem.GetProperty("code").GetString());
        _permissionGateway.Grant = true;
    }

    [Fact]
    public async Task Presentation_events_read_with_full_authorization_returns_previously_intaken_events()
    {
        var eventId = NewEventId();
        await _client.SendAsync(DeviceRequest(HttpMethod.Post, "/api/v1/access-cache/presentation-events", PresentationEvent(eventId), eventId), TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(AuthedReadRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(eventId, items[0].GetProperty("event_id").GetString());
        Assert.Equal("DENIED", items[0].GetProperty("outcome").GetString());
    }

    private HttpRequestMessage AuthedReadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/access-cache/presentation-events");
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ProvenanceProof(ActorId));
        return request;
    }

    // --- AC06: the headerless AccessService bridge route ---

    [Fact]
    public async Task View_access_events_bridge_needs_no_headers_and_reports_a_real_count()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/access/view-access-events"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("0 access events on record.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task View_access_events_bridge_reflects_real_events_recorded_against_its_own_demo_tenant()
    {
        await _eventLog.AppendAsync(
            new PresentationEvent(
                NewEventId(), AccessCacheDemoBridgeService.DemoTenantId, DeviceId, PersonId,
                PresentationOutcome.Allowed, "ALLOWED_NOT_REVOKED", "2026-09-24T00:00:00.000Z", "2026-09-24T00:00:00.000Z", null, false),
            TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/access/view-access-events"), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("1 access event on record.", body.GetProperty("message").GetString());
    }

    /// <summary>A second, independently-configured host for the one test that needs a non-default <see cref="IIdentityRevocationFeedGateway"/> registration - cheaper than re-plumbing every other test's shared fixture for one case. Registers every service <see cref="AccessCacheEndpoints.Map"/>'s four routes need collectively, even though this test only calls one of them - ASP.NET Core's minimal-API metadata inference validates every mapped endpoint's parameters together, the first time ANY request routes through this composite, not lazily per-route.</summary>
    private async Task<TestScope> StartWithGateway(IIdentityRevocationFeedGateway gateway)
    {
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
                services.AddSingleton(gateway);
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<IPresentationEventLog>(new InMemoryPresentationEventLog());
                services.AddSingleton<IAccessCacheAuditSink, InMemoryAccessCacheAuditSink>();
                services.AddScoped<AccessCacheService>();
                services.AddSingleton<AccessCacheDemoBridgeService>();
                services.AddSingleton<Tenancy.ICurrentTenantProvider>(new FixedTenantProvider(TenantId));
                services.AddSingleton<IPermissionGateway>(new FakePermissionGateway());
                var provenanceStore = new HmacActorProvenanceStore();
                provenanceStore.Configure(ProvenanceSecret);
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(AccessCacheEndpoints.Map);
            });
        });

        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return new TestScope(host);
    }

    private sealed class TestScope(IHost host) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }
    }
}
