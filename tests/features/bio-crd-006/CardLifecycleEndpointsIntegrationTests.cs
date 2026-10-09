// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardLifecycle.Api;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-CRD-006's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioCrd006Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. <see cref="ICardIssueGateway"/>
/// is a fake here; the real packaged pipeline (a genuine cross-story dependency on BIO-CRD-005)
/// is exercised separately in
/// <c>tests/dotnet/Biolog.IntegrationTests/CardLifecycleHostChannelTests.cs</c>.
/// </summary>
public sealed class CardLifecycleEndpointsIntegrationTests : IAsyncLifetime
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";
    private const string DeviceIdTenantA = "dev_018f2e5a-1234-7abc-89ab-0123456789a1";
    private const string DeviceIdTenantB = "dev_018f2e5a-1234-7abc-89ab-0123456789a2";
    private const string DeviceIdInactive = "dev_018f2e5a-1234-7abc-89ab-0123456789a3";
    private const string DeviceSecret = "test-device-secret";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;
    private FakeCardIssueGateway _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantA);
        _gateway = new FakeCardIssueGateway();

        var credentialStore = new InMemoryDeviceCredentialStore();
        credentialStore.Register(DeviceIdTenantA, DeviceSecret);
        credentialStore.Register(DeviceIdTenantB, DeviceSecret);
        credentialStore.Register(DeviceIdInactive, DeviceSecret);

        var deviceOwningTenant = new Dictionary<string, string>
        {
            [DeviceIdTenantA] = TenantA,
            [DeviceIdTenantB] = TenantB,
            [DeviceIdInactive] = TenantA,
        };
        var inactiveDeviceIds = new HashSet<string> { DeviceIdInactive };

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<ICardIssueGateway>(_gateway);
                services.AddSingleton<IProductionStatusGateway, FakeProductionStatusGateway>();
                services.AddSingleton<ICardActivationBarrier, FakeCardActivationBarrier>();
                services.AddSingleton<IDeviceCredentialStore>(credentialStore);
                services.AddSingleton<IDeviceExistenceGateway>(new FakeDeviceExistenceGateway(_tenantProvider, deviceOwningTenant, inactiveDeviceIds));
                services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();
                services.AddSingleton<ICardLifecycleRepository, InMemoryCardLifecycleRepository>();
                services.AddSingleton<ICardRecoveryPolicyStore, InMemoryCardRecoveryPolicyStore>();
                services.AddSingleton<Audit.ICardLifecycleAuditSink, Audit.InMemoryCardLifecycleAuditSink>();
                services.AddSingleton<IPresentationEventLog, InMemoryPresentationEventLog>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<CardLifecycleLockRegistry>();
                services.AddScoped<CardLifecycleService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(CardLifecycleEndpoints.Map);
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

    private static HttpRequestMessage WithActor(HttpMethod method, string path, object? body = null) => new(method, path)
    {
        Content = body is null ? null : JsonContent.Create(body),
        Headers = { { "X-Actor-Id", "operator-1" } },
    };

    /// <summary>TD-CRD-006-DENY-LIST-DEVICE-AUTH: GET /deny-list is device-authenticated, not actor-asserted - no X-Actor-Id header at all, matching production callers (an offline reader, not a logged-in operator).</summary>
    private static HttpRequestMessage WithDevice(string? deviceId, string? secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/card-lifecycle/deny-list");
        if (deviceId is not null) request.Headers.Add(HeaderDeviceAuthenticator.DeviceIdHeaderName, deviceId);
        if (secret is not null) request.Headers.Add(HeaderDeviceAuthenticator.DeviceSecretHeaderName, secret);
        return request;
    }

    [Fact]
    public async Task Report_then_get_lifecycle_returns_Locked_AC01()
    {
        _gateway.Seed(PersonId, CardClass, 1);

        var reportResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "stolen", reason = "left in a taxi" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var body = await reportResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("locked", body.GetProperty("lock_state").GetString());
        Assert.Equal("stolen", body.GetProperty("reason_code").GetString());

        var getResponse = await _client.SendAsync(
            WithActor(HttpMethod.Get, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/lifecycle"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Report_without_an_actor_header_is_400()
    {
        _gateway.Seed(PersonId, CardClass, 1);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report")
            {
                Content = JsonContent.Create(new { reason_code = "lost" }),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_for_an_unknown_card_issue_is_404()
    {
        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "lost" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Report_and_recover_round_trip_AC02()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        var reportResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "lost" }),
            TestContext.Current.CancellationToken);
        var reported = await reportResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var version = reported.GetProperty("version").GetInt32();

        var recoverResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/recover", new { expected_version = version }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, recoverResponse.StatusCode);
        var recovered = await recoverResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("recovered", recovered.GetProperty("lock_state").GetString());
    }

    [Fact]
    public async Task Recover_without_expected_version_is_428()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        await _client.SendAsync(WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "lost" }), TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/recover", new { }), TestContext.Current.CancellationToken);

        Assert.Equal(428, (int)response.StatusCode);
    }

    [Fact]
    public async Task Replace_uses_the_next_sequence_and_activate_replacement_revokes_the_earlier_card_AC03_AC04()
    {
        _gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);

        var replaceResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/replace", new { template_ref = "t", printer_ref = "p" }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, replaceResponse.StatusCode);
        var replacement = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(2, replacement.GetProperty("sequence_number").GetInt32());

        var activateRequest = WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/2/activate-replacement",
            new { expected_version = replacement.GetProperty("version").GetInt32() });
        activateRequest.Headers.Add(CardLifecycleEndpoints.IdempotencyKeyHeaderName, "idem-http-1");
        var activateResponse = await _client.SendAsync(activateRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("active", activated.GetProperty("status").GetString());

        var lifecycleResponse = await _client.SendAsync(
            WithActor(HttpMethod.Get, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/lifecycle"), TestContext.Current.CancellationToken);
        var record = await lifecycleResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("permanently_revoked", record.GetProperty("lock_state").GetString());
    }

    [Fact]
    public async Task ActivateReplacement_without_idempotency_key_is_428()
    {
        _gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var replaceResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/replace", new { template_ref = "t", printer_ref = "p" }),
            TestContext.Current.CancellationToken);
        var replacement = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/2/activate-replacement",
                new { expected_version = replacement.GetProperty("version").GetInt32() }),
            TestContext.Current.CancellationToken);

        Assert.Equal(428, (int)response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOCRD006QA02_a_stale_expected_version_on_activate_replacement_is_rejected_through_the_real_pipeline_without_touching_the_earlier_cards_lifecycle_record()
    {
        _gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        await _client.SendAsync(WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "lost" }), TestContext.Current.CancellationToken);
        await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/replace", new { template_ref = "t", printer_ref = "p" }),
            TestContext.Current.CancellationToken);

        var activateRequest = WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/2/activate-replacement", new { expected_version = 999 });
        activateRequest.Headers.Add(CardLifecycleEndpoints.IdempotencyKeyHeaderName, "idem-stale");
        var activateResponse = await _client.SendAsync(activateRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, activateResponse.StatusCode);
        var problem = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());

        var lifecycleResponse = await _client.SendAsync(
            WithActor(HttpMethod.Get, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/lifecycle"), TestContext.Current.CancellationToken);
        var record = await lifecycleResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("locked", record.GetProperty("lock_state").GetString()); // never destroyed by the rejected activation attempt
    }

    [Fact]
    public async Task PresentationCheck_denies_a_reported_card_and_the_deny_list_includes_it_AC01_AC05()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        await _client.SendAsync(WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "stolen" }), TestContext.Current.CancellationToken);

        var checkResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/presentation-check", new { device_ref = "reader-1" }),
            TestContext.Current.CancellationToken);
        var check = await checkResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("denied", check.GetProperty("outcome").GetString());

        var denyListResponse = await _client.SendAsync(
            WithDevice(DeviceIdTenantA, DeviceSecret), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, denyListResponse.StatusCode);
        var denyList = await denyListResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, denyList.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task TDCRD006_DenyList_with_no_device_headers_at_all_is_401()
    {
        var response = await _client.SendAsync(WithDevice(null, null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_authentication_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TDCRD006_DenyList_with_a_well_formed_device_id_and_the_wrong_secret_is_401()
    {
        var response = await _client.SendAsync(WithDevice(DeviceIdTenantA, "wrong-secret"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TDCRD006_DenyList_with_an_unregistered_device_id_is_401_regardless_of_the_secret_presented()
    {
        var response = await _client.SendAsync(WithDevice("dev_018f2e5a-1234-7abc-89ab-0123456789ff", "anything"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TDCRD006_DenyList_with_an_inactive_device_and_its_correct_secret_is_401()
    {
        var response = await _client.SendAsync(WithDevice(DeviceIdInactive, DeviceSecret), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TDCRD006_DenyList_a_devices_real_secret_does_not_authenticate_it_into_a_different_tenants_deny_list()
    {
        // Tenant binding: DeviceIdTenantB is a real, active device with its own genuinely correct
        // secret - just registered to TenantB, not the ambient TenantA this request resolves to.
        _gateway.Seed(PersonId, CardClass, 1);
        await _client.SendAsync(WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "stolen" }), TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(WithDevice(DeviceIdTenantB, DeviceSecret), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IntakePresentationEvent_requires_idempotency_key_and_then_durably_logs_AC05()
    {
        var missingKeyResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/card-lifecycle/presentation-events", new
            {
                person_id = PersonId,
                card_class = CardClass,
                sequence_number = 1,
                outcome = "denied",
                reason_code = "stolen",
                occurred_at = "2026-01-01T08:00:00.000Z",
                device_ref = "reader-offline-1",
            }),
            TestContext.Current.CancellationToken);
        Assert.Equal(428, (int)missingKeyResponse.StatusCode);

        var request = WithActor(HttpMethod.Post, "/api/v1/card-lifecycle/presentation-events", new
        {
            person_id = PersonId,
            card_class = CardClass,
            sequence_number = 1,
            outcome = "denied",
            reason_code = "stolen",
            occurred_at = "2026-01-01T08:00:00.000Z",
            device_ref = "reader-offline-1",
        });
        request.Headers.Add(CardLifecycleEndpoints.IdempotencyKeyHeaderName, "idem-offline-1");
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("offline_sync", evt.GetProperty("source").GetString());
    }

    [Fact]
    public async Task RecoveryPolicy_can_be_set_to_refuse_recovery_for_a_reason_code()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        var setResponse = await _client.SendAsync(
            WithActor(HttpMethod.Put, "/api/v1/card-lifecycle/recovery-policy", new { reason_code = "stolen", recovery_permitted = false }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, setResponse.StatusCode);

        var reportResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "stolen" }),
            TestContext.Current.CancellationToken);
        var reported = await reportResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var recoverResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/recover", new { expected_version = reported.GetProperty("version").GetInt32() }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, recoverResponse.StatusCode);
        var problem = await recoverResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("recovery_not_permitted", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Card_lifecycle_records_are_isolated_per_tenant()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        await _client.SendAsync(WithActor(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/report", new { reason_code = "lost" }), TestContext.Current.CancellationToken);

        _tenantProvider.TenantId = TenantB;
        var response = await _client.SendAsync(
            WithActor(HttpMethod.Get, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/lifecycle"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
