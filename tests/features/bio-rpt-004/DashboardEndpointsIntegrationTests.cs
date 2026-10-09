// Developer tests for BIO-RPT-004. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.OperationalDashboard.Api;
using Biolog.Features.OperationalDashboard.Domain;
using Biolog.Features.OperationalDashboard.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.OperationalDashboard.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-RPT-004's own endpoint, self-hosted via <see cref="TestServer"/>
/// - the same shape BIO-RPT-002's own <c>ReportEndpointsIntegrationTests</c> establishes. Wires
/// this project's own DI registrations and <c>DashboardEndpoints.Map</c> directly, never through
/// <c>Biolog.Api.Modules.BioRpt004Module</c> (no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>).
///
/// Uses a REAL <see cref="HmacActorProvenanceStore"/> configured with a test secret (not a fake) -
/// BIO-RPT-004-F03's whole point is that the gate is a genuine cryptographic check, so these tests
/// compute real HMAC proofs the same way a legitimate out-of-band caller would.
/// </summary>
public sealed class DashboardEndpointsIntegrationTests : IAsyncLifetime
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string GrantedActor = "per_018f2e5a-aaaa-7abc-89ab-0123456789ab";
    private const string UngrantedActor = "per_018f2e5a-bbbb-7abc-89ab-0123456789ab";
    private const string TestSecret = "this-is-a-32-plus-character-test-secret-only";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;
    private FakePermissionGateway _permissionGateway = null!;
    private FakeIdentityHealthGateway _identityGateway = null!;
    private FakeAccessHealthGateway _accessGateway = null!;
    private FakeProductionHealthGateway _productionGateway = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantA);
        _permissionGateway = new FakePermissionGateway();
        _permissionGateway.Grant(GrantedActor);
        _identityGateway = new FakeIdentityHealthGateway();
        _accessGateway = new FakeAccessHealthGateway();
        _productionGateway = new FakeProductionHealthGateway();

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<IDashboardPermissionGateway>(_permissionGateway);
                services.AddSingleton<IIdentityHealthGateway>(_identityGateway);
                services.AddSingleton<IAccessHealthGateway>(_accessGateway);
                services.AddSingleton<IProductionHealthGateway>(_productionGateway);
                var provenanceStore = new HmacActorProvenanceStore();
                provenanceStore.Configure(TestSecret);
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
                services.AddSingleton<LastKnownHealthCache>();
                services.AddScoped<DashboardService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(DashboardEndpoints.Map);
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

    /// <summary>Computes the SAME HMAC-SHA256(TestSecret, actorId) proof a legitimate out-of-band caller would - mirrors <see cref="HmacActorProvenanceStore.Verify"/>'s own expected-value computation exactly.</summary>
    private static string ComputeProof(string actorId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TestSecret), Encoding.UTF8.GetBytes(actorId)));

    private static HttpRequestMessage WithActor(string actorId, string? provenanceProof = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/operational-health");
        request.Headers.Add("X-Actor-Id", actorId);
        if (provenanceProof is not null)
        {
            request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, provenanceProof);
        }

        return request;
    }

    /// <summary>The default, fully-authenticated request every other test below builds on: a real actor id with its own genuine, correctly-computed proof.</summary>
    private static HttpRequestMessage WithVerifiedActor(string actorId) => WithActor(actorId, ComputeProof(actorId));

    [Fact]
    public async Task AC01_GET_operational_health_returns_200_with_all_six_panels_for_a_granted_verified_actor()
    {
        _identityGateway.Seed(TenantA, new IdentityCounts(10, 7, 2, 1));
        _accessGateway.Seed(TenantA, new AccessCounts(5, 3, 2));
        _productionGateway.Seed(TenantA, new ProductionCounts(4, 2, 1, 0, 1, 0));

        var response = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(TenantA, body.GetProperty("tenant_id").GetString());
        Assert.Equal(10, body.GetProperty("identity").GetProperty("total").GetInt32());
        Assert.Equal(5, body.GetProperty("access").GetProperty("total").GetInt32());
        Assert.Equal(4, body.GetProperty("production").GetProperty("total").GetInt32());
    }

    /// <summary>Seyi round-1, BIO-RPT-004-F01: "negative host tests proving an unprivileged role cannot read metrics." The ungranted actor still presents a genuine, correctly-computed proof for their OWN id - this proves the permission check itself, independent of F03's provenance gate.</summary>
    [Fact]
    public async Task F01_a_GET_from_a_verified_actor_without_the_dashboard_permission_is_403_not_200()
    {
        _identityGateway.Seed(TenantA, new IdentityCounts(10, 7, 2, 1));

        var response = await _client.SendAsync(WithVerifiedActor(UngrantedActor), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("dashboard_view_forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task F01_a_GET_with_no_X_Actor_Id_header_at_all_is_400_not_200()
    {
        var response = await _client.GetAsync("/api/v1/dashboard/operational-health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", problem.GetProperty("code").GetString());
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F03 (Critical): this is the literal attack the finding described - an attacker who knows a privileged actor's id asserts it with NO provenance proof at all. Must be refused, never silently authorized by the real permission grant.</summary>
    [Fact]
    public async Task F03_asserting_a_privileged_actors_id_with_no_provenance_proof_at_all_is_401_never_200()
    {
        _identityGateway.Seed(TenantA, new IdentityCounts(10, 7, 2, 1));

        var response = await _client.SendAsync(WithActor(GrantedActor, provenanceProof: null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_provenance_required", problem.GetProperty("code").GetString());
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F03: a WRONG proof (garbage hex) for a genuinely privileged actor id must still be refused - the gate verifies the proof, it does not just check "is something present."</summary>
    [Fact]
    public async Task F03_asserting_a_privileged_actors_id_with_a_fabricated_bogus_proof_is_401_never_200()
    {
        var response = await _client.SendAsync(WithActor(GrantedActor, provenanceProof: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_provenance_required", problem.GetProperty("code").GetString());
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F03: a proof computed for a DIFFERENT actor id (e.g. the caller's own unprivileged one) does not transfer to a privileged id it is merely attached alongside.</summary>
    [Fact]
    public async Task F03_a_proof_computed_for_a_different_actor_id_does_not_authorize_the_asserted_privileged_one()
    {
        var mismatchedProof = ComputeProof(UngrantedActor); // genuinely valid, but for the WRONG actor id

        var response = await _client.SendAsync(WithActor(GrantedActor, mismatchedProof), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F03: the provenance gate runs BEFORE the permission check - an unprivileged, unproven actor gets 401 (provenance), not 403 (permission), proving the ordering, not just the final outcome.</summary>
    [Fact]
    public async Task F03_the_provenance_gate_runs_before_the_permission_check()
    {
        var response = await _client.SendAsync(WithActor(UngrantedActor, provenanceProof: null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_provenance_required", problem.GetProperty("code").GetString()); // not dashboard_view_forbidden
    }

    [Fact]
    public async Task AC03_the_wire_response_labels_card_device_and_sync_as_unavailable_with_a_non_empty_reason_and_this_differs_from_stale()
    {
        var response = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        foreach (var panelName in new[] { "card", "device", "sync" })
        {
            var panel = body.GetProperty(panelName);
            Assert.Equal("unavailable", panel.GetProperty("freshness").GetString());
            Assert.False(string.IsNullOrWhiteSpace(panel.GetProperty("reason").GetString()));
        }

        foreach (var panelName in new[] { "identity", "access", "production" })
        {
            Assert.Equal("live", body.GetProperty(panelName).GetProperty("freshness").GetString());
        }
    }

    /// <summary>Seyi round-1, BIO-RPT-004-F02 at the real HTTP boundary: a failing real source surfaces as a 200 with that one panel Stale, not a 500 for the whole request.</summary>
    [Fact]
    public async Task F02_a_failing_identity_gateway_still_returns_200_with_identity_Stale_and_the_other_panels_unaffected()
    {
        _identityGateway.ThrowOnNextCall();
        _accessGateway.Seed(TenantA, new AccessCounts(5, 3, 2));

        var response = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("stale", body.GetProperty("identity").GetProperty("freshness").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("identity").GetProperty("reason").GetString()));
        Assert.Equal("live", body.GetProperty("access").GetProperty("freshness").GetString());
        Assert.Equal(5, body.GetProperty("access").GetProperty("total").GetInt32());
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F04 at the real HTTP boundary: a source that has NEVER succeeded reports null counts on failure, never a fabricated zero.</summary>
    [Fact]
    public async Task F04_a_source_that_has_never_succeeded_reports_null_counts_on_the_wire_not_zero()
    {
        _identityGateway.ThrowOnNextCall();

        var response = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var identity = body.GetProperty("identity");
        Assert.Equal("stale", identity.GetProperty("freshness").GetString());
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("total").ValueKind);
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F04 at the real HTTP boundary: a source that previously succeeded retains its real last-known values and their true observation time across two separate requests.</summary>
    [Fact]
    public async Task F04_a_source_that_previously_succeeded_retains_real_values_and_the_true_observation_time_across_requests()
    {
        _identityGateway.Seed(TenantA, new IdentityCounts(10, 7, 2, 1));
        var firstResponse = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var lastGoodObservedAt = firstBody.GetProperty("identity").GetProperty("observed_at").GetString();

        _identityGateway.ThrowOnNextCall();
        var secondResponse = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var identity = secondBody.GetProperty("identity");

        Assert.Equal("stale", identity.GetProperty("freshness").GetString());
        Assert.Equal(10, identity.GetProperty("total").GetInt32());
        Assert.Equal(lastGoodObservedAt, identity.GetProperty("observed_at").GetString());
    }

    [Fact]
    public async Task every_panel_carries_its_own_observed_at_on_the_wire()
    {
        var response = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        foreach (var panelName in new[] { "identity", "access", "production", "card", "device", "sync" })
        {
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty(panelName).GetProperty("observed_at").GetString()));
        }
    }

    [Fact]
    public async Task no_tenant_resolved_returns_400_tenant_context_missing_not_a_default_tenant()
    {
        _tenantProvider.TenantId = null;

        var response = await _client.SendAsync(WithVerifiedActor(GrantedActor), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("tenant_context_missing", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC02_no_mutation_route_exists_under_this_prefix()
    {
        // This feature is read-only by construction (DashboardService exposes nothing but
        // GetSnapshotAsync) - this proves it structurally at the HTTP boundary too, the same
        // "read-only to tenant users" discipline BIO-RPT-003's own identical test applies.
        var postResponse = await _client.PostAsync("/api/v1/dashboard/operational-health", content: null, TestContext.Current.CancellationToken);
        var putResponse = await _client.PutAsync("/api/v1/dashboard/operational-health", content: null, TestContext.Current.CancellationToken);
        var deleteResponse = await _client.DeleteAsync("/api/v1/dashboard/operational-health", TestContext.Current.CancellationToken);

        // The route itself exists (GET only) - ASP.NET Core's own router reports a mismatched
        // verb against a known route as 405, not 404 (see the distinct 404 proof below, which
        // hits a path no module here maps at all).
        Assert.Equal(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, putResponse.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task no_route_here_ever_collides_with_a_sibling_ReportingService_path()
    {
        var reportingResponse = await _client.GetAsync("/api/v1/reporting/schema", TestContext.Current.CancellationToken);
        var auditResponse = await _client.GetAsync("/api/v1/audit-investigations", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, reportingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, auditResponse.StatusCode);
    }
}
