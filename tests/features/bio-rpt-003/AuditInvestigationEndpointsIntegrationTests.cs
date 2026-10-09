// Developer tests for BIO-RPT-003. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.AuditInvestigation.Api;
using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Biolog.Features.AuditInvestigation.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.AuditInvestigation.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-RPT-003's own endpoints, self-hosted via <see cref="TestServer"/>
/// - the same shape BIO-CRD-008's own <c>CardTemplateEndpointsIntegrationTests</c> establishes.
/// Wires this project's own DI registrations and <c>AuditInvestigationEndpoints.Map</c> directly,
/// never through <c>Biolog.Api.Modules.BioRpt003Module</c> (no <c>ProjectReference</c> to
/// <c>Biolog.Api.csproj</c>) - the four real sibling-feature adapters are exercised separately, at
/// the module-wiring level, by the offline structural harness and the LIVE proof instead.
/// </summary>
public sealed class AuditInvestigationEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeAuditExportPermissionGateway _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _gateway = new FakeAuditExportPermissionGateway();
        var seeded = TestData.Record("rbac", "evt_seed", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "internal");

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TestData.TenantA));
                services.AddSingleton<IAuditExportPermissionGateway>(_gateway);
                services.AddSingleton<IAuditEventSource>(new FakeAuditEventSource("rbac", [seeded]));
                services.AddSingleton<IAuditInvestigationAuditSink, InMemoryAuditInvestigationAuditSink>();
                services.AddScoped<AuditInvestigationService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(AuditInvestigationEndpoints.Map);
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

    private static HttpRequestMessage WithActor(HttpMethod method, string path, string actorId = "viewer-1", object? body = null) => new(method, path)
    {
        Content = body is null ? null : JsonContent.Create(body),
        Headers = { { "X-Actor-Id", actorId } },
    };

    [Fact]
    public async Task AC01_search_returns_the_seeded_record_for_the_resolved_tenant()
    {
        var response = await _client.SendAsync(WithActor(HttpMethod.Get, "/api/v1/audit-investigations"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, body.GetProperty("total_count").GetInt32());
        Assert.Equal("evt_seed", body.GetProperty("records")[0].GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task GetById_returns_404_problem_json_for_an_unknown_event_id()
    {
        var response = await _client.SendAsync(WithActor(HttpMethod.Get, "/api/v1/audit-investigations/evt_does_not_exist"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("audit_investigation_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC03_export_returns_403_when_the_actor_lacks_the_export_permission()
    {
        var response = await _client.SendAsync(WithActor(HttpMethod.Post, "/api/v1/audit-investigations/export", body: new { }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("audit_export_forbidden", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC03_export_returns_csv_when_the_actor_holds_the_export_permission()
    {
        _gateway.Grant("exporter-1", AuditPermissionKeys.Export);

        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/audit-investigations/export", actorId: "exporter-1", body: new { }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("evt_seed", csv);
    }

    [Fact]
    public async Task Missing_actor_header_returns_400_actor_required()
    {
        var response = await _client.GetAsync("/api/v1/audit-investigations", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("PUT", "/api/v1/audit-investigations")]
    [InlineData("PATCH", "/api/v1/audit-investigations")]
    [InlineData("DELETE", "/api/v1/audit-investigations")]
    [InlineData("PUT", "/api/v1/audit-investigations/evt_seed")]
    [InlineData("PATCH", "/api/v1/audit-investigations/evt_seed")]
    [InlineData("DELETE", "/api/v1/audit-investigations/evt_seed")]
    public async Task AC02_no_mutation_route_exists_under_this_prefix(string method, string path)
    {
        var response = await _client.SendAsync(WithActor(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        // The route TEMPLATE exists (GET is mapped there), but no handler this feature wrote
        // accepts PUT/PATCH/DELETE - ASP.NET Core's own routing reports that as 405 Method Not
        // Allowed, not 404 (confirmed empirically; an earlier draft of this test wrongly assumed
        // 404). That is in fact the stronger proof of AC02's "read-only to tenant users": a 405
        // explicitly asserts "this resource supports other methods, not this one," whereas a 404
        // would only say "nothing is here."
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
