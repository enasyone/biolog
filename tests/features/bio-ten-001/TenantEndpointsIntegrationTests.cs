using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.TenantRegistry.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-TEN-001's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="TenantEndpoints.Map"/> directly rather than going through
/// <c>Biolog.Api.Modules.BioTen001Module</c> - that adapter now lives in
/// <c>api/Biolog.Api/Modules/</c> (the circular-reference fix), and this project has no
/// <c>ProjectReference</c> back to <c>Biolog.Api.csproj</c> (one-way dependency,
/// <c>scripts/check-module-project-references.sh</c>). This registration list must stay
/// identical in substance to <c>BioTen001Module.RegisterServices</c>/<c>MapEndpoints</c>.
/// </summary>
public sealed class TenantEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ITenantRegistry, InMemoryTenantRegistry>();
                services.AddSingleton<ITenantAuditSink, InMemoryTenantAuditSink>();
                services.AddSingleton<TenantService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(TenantEndpoints.Map);
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(TestContext.Current.CancellationToken);
        _host.Dispose();
    }

    private static HttpRequestMessage AdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, "admin_alice");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: TenantJsonOptions.Strict);
        }

        return request;
    }

    private static object ValidCreateBody(string name = "Acme University") => new
    {
        name,
        license_plan = "enterprise",
        license_seats = 500,
        routing_region = "eu-west",
    };

    [Fact]
    public async Task Create_returns_201_with_the_created_tenant_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenants", ValidCreateBody()), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.StartsWith("ten_", body.GetProperty("id").GetString(), StringComparison.Ordinal);
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Create_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/control-plane/tenants")
        {
            Content = JsonContent.Create(ValidCreateBody(), options: TenantJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_with_an_unrecognised_field_is_a_validation_problem_response_not_a_500()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = new StringContent(
            """{"name":"Acme","license_plan":"enterprise","license_seats":10,"routing_region":"eu-west","date_of_birth":"2000-01-01"}""",
            System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/control-plane/tenants") { Content = payload };
        request.Headers.Add(ActorResolver.ActorIdHeader, "admin_alice");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_of_an_unknown_tenant_is_404_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/control-plane/tenants/ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_not_found", body.GetProperty("code").GetString());
        Assert.True(CanonicalIdIsEvent(body.GetProperty("correlation_id").GetString()));
    }

    [Fact]
    public async Task Create_then_get_round_trips_the_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenants", ValidCreateBody("Roundtrip U")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = created.GetProperty("id").GetString();

        var getResponse = await _client.GetAsync($"/api/v1/control-plane/tenants/{id}", ct);
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("Roundtrip U", fetched.GetProperty("name").GetString());
    }

    [Fact]
    public async Task List_returns_the_cursor_pagination_envelope_shape()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenants", ValidCreateBody("List Test A")), ct);
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenants", ValidCreateBody("List Test B")), ct);

        var response = await _client.GetAsync("/api/v1/control-plane/tenants?limit=1", ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("data").ValueKind);
        Assert.Single(body.GetProperty("data").EnumerateArray());
        Assert.True(body.GetProperty("page").TryGetProperty("next_cursor", out _));
        Assert.True(body.GetProperty("page").TryGetProperty("limit", out _));
    }

    [Fact]
    public async Task Status_update_then_get_reflects_the_new_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenants", ValidCreateBody("Status Test")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = created.GetProperty("id").GetString();
        var version = created.GetProperty("version").GetInt32();

        var statusResponse = await _client.SendAsync(AdminRequest(
            HttpMethod.Put, $"/api/v1/control-plane/tenants/{id}/status", new { version, status = "suspended" }), ct);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var updated = await statusResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("suspended", updated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delete_soft_disables_and_the_tenant_remains_gettable()
    {
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenants", ValidCreateBody("Delete Test")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = created.GetProperty("id").GetString();
        var version = created.GetProperty("version").GetInt32();

        var deleteResponse = await _client.SendAsync(AdminRequest(HttpMethod.Delete, $"/api/v1/control-plane/tenants/{id}?version={version}"), ct);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/v1/control-plane/tenants/{id}", ct);
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode); // still there, only disabled
        Assert.Equal("disabled", fetched.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_back_verbatim()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = AdminRequest(HttpMethod.Get, "/api/v1/control-plane/tenants");
        var supplied = "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
        request.Headers.Add(CorrelationIdHeader.HeaderName, supplied);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(supplied, response.Headers.GetValues(CorrelationIdHeader.HeaderName).Single());
    }

    private static bool CanonicalIdIsEvent(string? value) => value is not null && value.StartsWith("evt_", StringComparison.Ordinal);
}
