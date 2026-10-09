// Developer tests for BIO-CRD-007. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardProduction.Api;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardProduction.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-CRD-007's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioCrd007Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. <see cref="ICardIssueGateway"/>
/// is a fake here; the real packaged pipeline (a genuine cross-story dependency on BIO-CRD-005)
/// is exercised separately in
/// <c>tests/dotnet/Biolog.IntegrationTests/CardProductionHostChannelTests.cs</c>.
/// </summary>
public sealed class ProductionEndpointsIntegrationTests : IAsyncLifetime
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;
    private FakeCardIssueGateway _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantA);
        _gateway = new FakeCardIssueGateway();

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<ICardIssueGateway>(_gateway);
                services.AddSingleton<IProductionJobRepository, InMemoryProductionJobRepository>();
                services.AddSingleton<IProductionPackageStore, InMemoryProductionPackageStore>();
                services.AddSingleton<IProductionSigningKeyStore, InMemoryProductionSigningKeyStore>();
                services.AddSingleton<Audit.IProductionAuditSink, Audit.InMemoryProductionAuditSink>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<ICardActivationBarrier, FakeCardActivationBarrier>();
                services.AddSingleton<ProductionJobLockRegistry>();
                services.AddScoped<ProductionService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(ProductionEndpoints.Map);
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

    private const string CardPath = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/production";

    [Fact]
    public async Task CreateJob_then_get_returns_InProgress_AC01()
    {
        _gateway.Seed(PersonId, CardClass, 1);

        var createResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"{CardPath}/jobs", new { printer_ref = "p1", workstation_ref = "w1" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var body = await createResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("in_progress", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("job_sequence_number").GetInt32());

        var getResponse = await _client.SendAsync(WithActor(HttpMethod.Get, $"{CardPath}/jobs/1"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateJob_without_an_actor_header_is_400()
    {
        _gateway.Seed(PersonId, CardClass, 1);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"{CardPath}/jobs") { Content = JsonContent.Create(new { printer_ref = "p1", workstation_ref = "w1" }) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Recording_all_three_passing_steps_then_activating_succeeds_end_to_end_AC01_AC02_AC05()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        var created = await (await _client.SendAsync(WithActor(HttpMethod.Post, $"{CardPath}/jobs", new { printer_ref = "p1", workstation_ref = "w1" }), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var version = created.GetProperty("version").GetInt32();

        var afterPrint = await StepAsync("print", version);
        var afterEncode = await StepAsync("encode", afterPrint);
        var afterReadBack = await StepAsync("readback-qc", afterEncode);

        var finalJob = await (await _client.SendAsync(WithActor(HttpMethod.Get, $"{CardPath}/jobs/1"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("passed", finalJob.GetProperty("status").GetString());

        var activateRequest = WithActor(HttpMethod.Post, $"{CardPath}/activate", new { expected_version = 1 });
        activateRequest.Headers.Add(ProductionEndpoints.IdempotencyKeyHeaderName, "idem-1");
        var activateResponse = await _client.SendAsync(activateRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("active", activated.GetProperty("status").GetString());

        async Task<int> StepAsync(string step, int expectedVersion)
        {
            var response = await _client.SendAsync(
                WithActor(HttpMethod.Post, $"{CardPath}/jobs/1/steps/{step}", new { passed = true, expected_version = expectedVersion }), TestContext.Current.CancellationToken);
            var stepBody = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            return stepBody.GetProperty("version").GetInt32();
        }
    }

    [Fact]
    public async Task Activation_is_refused_through_the_real_pipeline_when_no_job_has_passed_AC02()
    {
        _gateway.Seed(PersonId, CardClass, 1);

        var activateRequest = WithActor(HttpMethod.Post, $"{CardPath}/activate", new { expected_version = 1 });
        activateRequest.Headers.Add(ProductionEndpoints.IdempotencyKeyHeaderName, "idem-1");
        var response = await _client.SendAsync(activateRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("production_not_passed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reject_records_a_reason_AC04()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        var created = await (await _client.SendAsync(WithActor(HttpMethod.Post, $"{CardPath}/jobs", new { printer_ref = "p1", workstation_ref = "w1" }), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"{CardPath}/jobs/1/reject", new { reason = "damaged laminate", expected_version = created.GetProperty("version").GetInt32() }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("rejected", body.GetProperty("status").GetString());
        Assert.Equal("damaged laminate", body.GetProperty("rejection_reason").GetString());
    }

    [Fact]
    public async Task IssuePackage_and_OfflineComplete_round_trip_AC03()
    {
        _gateway.Seed(PersonId, CardClass, 1);

        var issueResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"{CardPath}/packages", new { printer_ref = "p1", workstation_ref = "w1" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        var package = await issueResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = package.GetProperty("token").GetString()!;

        var completeRequest = WithActor(HttpMethod.Post, "/api/v1/card-production/offline-complete", new
        {
            token,
            print_passed = true,
            encode_passed = true,
            readback_qc_passed = true,
        });
        completeRequest.Headers.Add(ProductionEndpoints.IdempotencyKeyHeaderName, "idem-offline-1");
        var completeResponse = await _client.SendAsync(completeRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var job = await completeResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("passed", job.GetProperty("status").GetString());
        Assert.Equal("offline", job.GetProperty("source").GetString());
    }

    [Fact]
    public async Task OfflineComplete_without_idempotency_key_is_428()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        var issueResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"{CardPath}/packages", new { printer_ref = "p1", workstation_ref = "w1" }), TestContext.Current.CancellationToken);
        var package = await issueResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var response = await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/card-production/offline-complete", new
            {
                token = package.GetProperty("token").GetString(),
                print_passed = true,
                encode_passed = true,
                readback_qc_passed = true,
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(428, (int)response.StatusCode);
    }

    [Fact]
    public async Task Production_jobs_are_isolated_per_tenant()
    {
        _gateway.Seed(PersonId, CardClass, 1);
        await _client.SendAsync(WithActor(HttpMethod.Post, $"{CardPath}/jobs", new { printer_ref = "p1", workstation_ref = "w1" }), TestContext.Current.CancellationToken);

        _tenantProvider.TenantId = TenantB;
        var response = await _client.SendAsync(WithActor(HttpMethod.Get, $"{CardPath}/jobs/1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
