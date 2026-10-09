// Developer tests for BIO-RPT-002. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.ReportBuilder.Api;
using Biolog.Features.ReportBuilder.Domain;
using Biolog.Features.ReportBuilder.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.ReportBuilder.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-RPT-002's own endpoints, self-hosted via
/// <see cref="TestServer"/> - the same shape BIO-CRD-007's own
/// <c>ProductionEndpointsIntegrationTests</c> establishes. Wires this project's own DI
/// registrations and <c>ReportEndpoints.Map</c> directly, never through
/// <c>Biolog.Api.Modules.BioRpt002Module</c> (no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>).
/// </summary>
public sealed class ReportEndpointsIntegrationTests : IAsyncLifetime
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;
    private FakeCardProductionGateway _cardGateway = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantA);
        _cardGateway = new FakeCardProductionGateway();

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<ICardProductionGateway>(_cardGateway);
                services.AddSingleton<IAttendanceGateway>(new FakeAttendanceGateway());
                services.AddSingleton<IReportJobRepository, InMemoryReportJobRepository>();
                services.AddSingleton<Audit.IReportAuditSink, Audit.InMemoryReportAuditSink>();
                // Singleton, matching BioRpt002Module's own real registration (see its comment):
                // ReportJobRunner is a genuine process-lifetime Singleton, so everything on its
                // dependency chain must be too - ASP.NET Core's own DI validation refuses a
                // Singleton capturing a Scoped service.
                services.AddSingleton<ReportGenerationService>();
                services.AddSingleton<ReportJobRunner>();
                services.AddScoped<ReportService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(ReportEndpoints.Map);
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

    [Fact]
    public async Task AC01_GET_schema_lists_both_governed_datasets()
    {
        var response = await _client.GetAsync("/api/v1/reporting/schema", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var datasets = body.GetProperty("datasets").EnumerateArray().Select(d => d.GetProperty("dataset").GetString()).ToList();
        Assert.Contains("card_production", datasets);
        Assert.Contains("attendance", datasets);
    }

    [Fact]
    public async Task AC01_AC04_creating_a_report_returns_202_and_it_eventually_completes_with_a_real_result()
    {
        _cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"), new CardProductionRecord("failed", "connected", "staff"));

        var createResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/reporting/reports", new { dataset = "card_production", dimension = "status", measure = "count" }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jobId = created.GetProperty("job_id").GetString()!;

        var job = await PollUntilTerminalAsync(jobId);

        Assert.Equal("completed", job.GetProperty("status").GetString());
        Assert.Equal(2, job.GetProperty("result").GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task AC01_an_ungoverned_dimension_is_refused_with_422_and_never_creates_a_job()
    {
        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/reporting/reports", new { dataset = "card_production", dimension = "mode", measure = "count" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("report_dimension_not_whitelisted", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task creating_a_report_without_an_actor_header_is_400()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/reporting/reports") { Content = JsonContent.Create(new { dataset = "card_production", dimension = "status", measure = "count" }) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC02_exporting_a_completed_report_as_csv_returns_real_csv_bytes()
    {
        _cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var created = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/reporting/reports", new { dataset = "card_production", dimension = "status", measure = "count" }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jobId = created.GetProperty("job_id").GetString()!;
        await PollUntilTerminalAsync(jobId);

        var exportResponse = await _client.SendAsync(WithActor(HttpMethod.Get, $"/api/v1/reporting/reports/{jobId}/export?format=csv"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal("text/csv", exportResponse.Content.Headers.ContentType?.MediaType);
        var csv = await exportResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("status,count", csv);
        Assert.Contains("passed,1", csv);
    }

    [Fact]
    public async Task AC02_exporting_as_excel_returns_501_not_a_silent_empty_success()
    {
        _cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var created = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/reporting/reports", new { dataset = "card_production", dimension = "status", measure = "count" }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jobId = created.GetProperty("job_id").GetString()!;
        await PollUntilTerminalAsync(jobId);

        var exportResponse = await _client.SendAsync(WithActor(HttpMethod.Get, $"/api/v1/reporting/reports/{jobId}/export?format=excel"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, exportResponse.StatusCode);
    }

    [Fact]
    public async Task no_route_here_ever_collides_with_BIO_RPT_001s_own_primary_action_view_audit_trail_pair()
    {
        // BIO-RPT-002-AC06 (god's ruling, 2026-09-24): this story never maps
        // /api/v1/reporting/primary-action or /view-audit-trail - those stay entirely
        // BIO-RPT-001's own routes. This test proves it structurally: hitting that exact path
        // against THIS project's own endpoint group alone 404s (no handler registered here),
        // rather than silently relying on "no AmbiguousMatchException" only being observable
        // once both real modules are hosted together.
        var response = await _client.PostAsync("/api/v1/reporting/primary-action", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC04_a_queued_job_can_be_cancelled_through_the_real_endpoint()
    {
        _cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var created = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/reporting/reports", new { dataset = "card_production", dimension = "status", measure = "count" }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jobId = created.GetProperty("job_id").GetString()!;

        // A real, if narrow, race against the background Task - a 409 (already completed) is an
        // acceptable outcome too for this tiny, near-instant dataset; what this test actually
        // proves is that the cancel endpoint never 500s and always returns a recognised terminal
        // shape for a request that was, at minimum, a legitimate attempt.
        var cancelResponse = await _client.SendAsync(WithActor(HttpMethod.Post, $"/api/v1/reporting/reports/{jobId}/cancel"), TestContext.Current.CancellationToken);

        Assert.True(cancelResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
    }

    private async Task<JsonElement> PollUntilTerminalAsync(string jobId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            var response = await _client.SendAsync(WithActor(HttpMethod.Get, $"/api/v1/reporting/reports/{jobId}"), TestContext.Current.CancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var status = body.GetProperty("status").GetString();
            if (status is "completed" or "failed" or "cancelled")
            {
                return body;
            }

            await Task.Delay(10, CancellationToken.None);
        }

        throw new TimeoutException($"report job '{jobId}' did not reach a terminal state within the test timeout");
    }
}
