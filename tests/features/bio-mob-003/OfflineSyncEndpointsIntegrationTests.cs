// Developer tests for BIO-MOB-003's central intake endpoint. SYNTHETIC fixtures only.
//
// Chioma's acceptance round 1 FAIL: "Every HTTP behavior test uses MockClient; there is no
// client-to-central integration test or transcript proving an addition, update, or revocation
// is accepted centrally. AC02 is server-side behavior... no executable evidence proves that the
// server stores the original result, rejects key reuse with a changed fingerprint, or returns
// the stored result on replay." These tests are that evidence: a real HTTP pipeline
// (TestServer, real routing/model-binding/endpoint execution, not a mock) exercising every
// scenario her remediation named.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.OfflineSync.Api;
using Biolog.Features.OfflineSync.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.OfflineSync.Tests;

public sealed class OfflineSyncEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryDeviceCredentialStore _credentialStore = null!;

    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string OtherTenantId = "ten_018f2e5a-5678-7abc-89ab-0123456789cd";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceSecret = "test-device-secret";
    private const string AggregateId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    public async ValueTask InitializeAsync()
    {
        _credentialStore = new InMemoryDeviceCredentialStore();
        _credentialStore.Register(DeviceId, DeviceSecret);

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
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<IAggregateVersionLedger, InMemoryAggregateVersionLedger>();
                services.AddSingleton<Audit.IOfflineSyncAuditSink, Audit.InMemoryOfflineSyncAuditSink>();
                services.AddSingleton<OfflineMutationLockRegistry>();
                services.AddScoped<OfflineMutationIntakeService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(OfflineSyncEndpoints.Map);
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

    private static object Delta(
        string mutationId, string aggregateId, int baseVersion, string operation, string idempotencyKey, object? payload = null) => new
        {
            mutation_id = mutationId,
            tenant_id = TenantId,
            device_id = DeviceId,
            aggregate_id = aggregateId,
            base_version = baseVersion,
            idempotency_key = idempotencyKey,
            occurred_at = "2026-09-23T00:00:00.000Z",
            sync_state = "UPLOADING",
            operation,
            payload = payload ?? new { field = "SYNTHETIC" },
        };

    private HttpRequestMessage Request(object body, string idempotencyKey, string? deviceId = DeviceId, string? secret = DeviceSecret)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/offline-mutations")
        {
            Content = JsonContent.Create(body),
        };
        if (deviceId is not null) message.Headers.Add(HeaderDeviceAuthenticator.DeviceIdHeaderName, deviceId);
        if (secret is not null) message.Headers.Add(HeaderDeviceAuthenticator.DeviceSecretHeaderName, secret);
        message.Headers.Add(OfflineSyncEndpoints.IdempotencyKeyHeaderName, idempotencyKey);
        return message;
    }

    private static string NewMutationId() => $"evt_{Guid.CreateVersion7():D}";

    [Fact]
    public async Task An_addition_is_accepted_centrally_and_returns_201()
    {
        var mutationId = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(mutationId, AggregateId, 1, "ADDITION", $"idem-{mutationId}"), $"idem-{mutationId}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains(OfflineSyncEndpoints.IdempotencyReplayedHeaderName));
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(mutationId, ack.GetProperty("mutation_id").GetString());
        Assert.Equal(1, ack.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task An_update_against_the_now_current_version_is_accepted()
    {
        var first = NewMutationId();
        await _client.SendAsync(Request(Delta(first, AggregateId, 1, "ADDITION", $"idem-{first}"), $"idem-{first}"), TestContext.Current.CancellationToken);
        // The addition above is this aggregate's first-ever mutation: the ledger adopts its
        // claimed base_version (1) directly as the resulting current version (see
        // AggregateVersionLedger's own header) - so the next mutation must claim base_version 1
        // to match, not 2.

        var second = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(second, AggregateId, 1, "UPDATE", $"idem-{second}"), $"idem-{second}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(2, ack.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_revocation_is_accepted_centrally_the_same_as_any_other_operation()
    {
        var mutationId = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(mutationId, AggregateId, 1, "REVOCATION", $"idem-{mutationId}"), $"idem-{mutationId}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("REVOCATION", ack.GetProperty("operation").GetString());
    }

    [Fact]
    public async Task Replaying_the_identical_request_with_the_same_idempotency_key_returns_the_original_stored_result()
    {
        var mutationId = NewMutationId();
        var idempotencyKey = $"idem-{mutationId}";
        var delta = Delta(mutationId, AggregateId, 1, "ADDITION", idempotencyKey);

        var first = await _client.SendAsync(Request(delta, idempotencyKey), TestContext.Current.CancellationToken);
        var firstAck = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var second = await _client.SendAsync(Request(delta, idempotencyKey), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues(OfflineSyncEndpoints.IdempotencyReplayedHeaderName).Single());
        var secondAck = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(firstAck.GetProperty("mutation_id").GetString(), secondAck.GetProperty("mutation_id").GetString());
        Assert.Equal(firstAck.GetProperty("version").GetInt32(), secondAck.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Reusing_the_same_idempotency_key_with_different_content_is_rejected_not_executed_again()
    {
        var idempotencyKey = "idem-reused-key";
        var first = NewMutationId();
        await _client.SendAsync(
            Request(Delta(first, AggregateId, 1, "ADDITION", idempotencyKey), idempotencyKey), TestContext.Current.CancellationToken);

        var second = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(second, AggregateId, 1, "ADDITION", idempotencyKey), idempotencyKey), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_reuse", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_stale_base_version_is_rejected_with_a_concurrency_conflict_carrying_the_current_version()
    {
        var seed = NewMutationId();
        await _client.SendAsync(Request(Delta(seed, AggregateId, 1, "ADDITION", $"idem-{seed}"), $"idem-{seed}"), TestContext.Current.CancellationToken);
        var advance = NewMutationId();
        await _client.SendAsync(Request(Delta(advance, AggregateId, 1, "UPDATE", $"idem-{advance}"), $"idem-{advance}"), TestContext.Current.CancellationToken);
        // Ledger's current version for AggregateId is now 2.

        // Still claims base_version 1 - stale.
        var stale = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(stale, AggregateId, 1, "UPDATE", $"idem-{stale}"), $"idem-{stale}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());
        Assert.Equal(2, problem.GetProperty("detail").GetProperty("current_version").GetInt32());
    }

    [Fact]
    public async Task A_conflict_never_advances_the_ledger_or_commits_anything_no_merge()
    {
        var first = NewMutationId();
        await _client.SendAsync(Request(Delta(first, AggregateId, 1, "ADDITION", $"idem-{first}"), $"idem-{first}"), TestContext.Current.CancellationToken);
        // Ledger's current version for AggregateId is 1.

        var conflicting = NewMutationId();
        var conflictResponse = await _client.SendAsync(
            Request(Delta(conflicting, AggregateId, 5, "UPDATE", $"idem-{conflicting}"), $"idem-{conflicting}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        // A correctly-versioned mutation right after the conflict must still see version 1 as
        // current - the rejected attempt above never advanced anything.
        var correct = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(correct, AggregateId, 1, "UPDATE", $"idem-{correct}"), $"idem-{correct}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(2, ack.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Missing_device_credentials_are_rejected_with_a_generic_401()
    {
        var mutationId = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(mutationId, AggregateId, 1, "ADDITION", $"idem-{mutationId}"), $"idem-{mutationId}", deviceId: null, secret: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_authentication_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_wrong_device_secret_is_rejected_identically_to_a_missing_one()
    {
        var mutationId = NewMutationId();
        var response = await _client.SendAsync(
            Request(Delta(mutationId, AggregateId, 1, "ADDITION", $"idem-{mutationId}"), $"idem-{mutationId}", secret: "wrong-secret"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_payload_tenant_id_that_does_not_match_the_devices_resolved_tenant_is_rejected_not_routed()
    {
        // Kemi's ruling: the payload's tenant_id is a claim, never trusted for routing on its
        // own. This device is registered to TenantId; the body claims OtherTenantId.
        var mutationId = NewMutationId();
        var forged = new
        {
            mutation_id = mutationId,
            tenant_id = OtherTenantId,
            device_id = DeviceId,
            aggregate_id = AggregateId,
            base_version = 1,
            idempotency_key = $"idem-{mutationId}",
            occurred_at = "2026-09-23T00:00:00.000Z",
            sync_state = "UPLOADING",
            operation = "ADDITION",
            payload = new { field = "SYNTHETIC" },
        };
        var response = await _client.SendAsync(Request(forged, $"idem-{mutationId}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("tenant_mismatch", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_payload_device_id_that_does_not_match_the_authenticated_device_is_rejected_not_routed()
    {
        // BIO-MOB-003-QA-02 (Chioma, acceptance round 2): the same discipline as the tenant_id
        // check, applied to device_id. This device authenticates as DeviceId; the body claims a
        // different (but still well-formed) device's canonical id as the mutation's origin.
        var otherDeviceId = "dev_018f2e5a-1111-7a41-9b2e-4f7c8d1a6e5b";
        var mutationId = NewMutationId();
        var forged = new
        {
            mutation_id = mutationId,
            tenant_id = TenantId,
            device_id = otherDeviceId,
            aggregate_id = AggregateId,
            base_version = 1,
            idempotency_key = $"idem-{mutationId}",
            occurred_at = "2026-09-23T00:00:00.000Z",
            sync_state = "UPLOADING",
            operation = "ADDITION",
            payload = new { field = "SYNTHETIC" },
        };
        var response = await _client.SendAsync(Request(forged, $"idem-{mutationId}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_mismatch", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Two_updates_racing_the_same_established_aggregate_a_reconnect_storm_at_the_server_do_not_both_succeed()
    {
        // BIO-MOB-003-AC05's server-side complement: even if two client processes (or the same
        // device retrying concurrently) submit against the same aggregate at once, the ledger's
        // single lock makes exactly one of them win a given version - no double-application.
        var seed = NewMutationId();
        await _client.SendAsync(Request(Delta(seed, AggregateId, 1, "ADDITION", $"idem-{seed}"), $"idem-{seed}"), TestContext.Current.CancellationToken);
        // Ledger's current version for AggregateId is 1 - both racers below claim exactly that.

        var first = NewMutationId();
        var second = NewMutationId();
        var firstTask = _client.SendAsync(
            Request(Delta(first, AggregateId, 1, "UPDATE", $"idem-{first}"), $"idem-{first}"), TestContext.Current.CancellationToken);
        var secondTask = _client.SendAsync(
            Request(Delta(second, AggregateId, 1, "UPDATE", $"idem-{second}"), $"idem-{second}"), TestContext.Current.CancellationToken);
        var responses = await Task.WhenAll(firstTask, secondTask);

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflicted = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);
    }
}
