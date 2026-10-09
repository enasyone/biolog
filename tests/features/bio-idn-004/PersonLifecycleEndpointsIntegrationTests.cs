using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.PersonLifecycle.Api;
using Biolog.Features.PersonLifecycle.Domain;
using Biolog.Features.PersonLifecycle.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.PersonLifecycle.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-IDN-004's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="PersonLifecycleEndpoints.Map"/> directly, never through
/// <c>Biolog.Api.Modules.BioIdn004Module</c> - this project has no <c>ProjectReference</c> to
/// <c>Biolog.Api.csproj</c>. Tenant context and the gateway are fixed test doubles here; the
/// real packaged pipeline is covered by
/// <c>tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs</c>.
/// </summary>
public sealed class PersonLifecycleEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakePersonLifecycleGateway _gateway = null!;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    public async ValueTask InitializeAsync()
    {
        _gateway = new FakePersonLifecycleGateway();
        _gateway.Seed(PersonId, TenantId, PersonStatus.Active);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IPersonLifecycleGateway>(_gateway.ForTenant(TenantId));
                services.AddSingleton<Audit.IPersonLifecycleAuditSink, Audit.InMemoryPersonLifecycleAuditSink>();
                services.AddSingleton<ICredentialAccessRevocationEventSink, InMemoryCredentialAccessRevocationEventSink>();
                services.AddSingleton<IPendingSuspensionReconciliationStore, InMemoryPendingSuspensionReconciliationStore>();
                services.AddSingleton<PersonLifecycleLockRegistry>();
                services.AddScoped<PersonLifecycleService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(PersonLifecycleEndpoints.Map);
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, object? body = null, string? permission = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, "registrar_ada");
        if (permission is not null)
        {
            request.Headers.Add(PermissionResolver.PermissionHeader, permission);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: PersonLifecycleJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task Get_returns_the_current_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/lifecycle"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Emergency_suspension_returns_200_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "SUSPENDED", version = 1 }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("SUSPENDED", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reactivation_without_the_permission_header_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "SUSPENDED", version = 1 }), ct);

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "ACTIVE", version = 2, reason = "cleared" }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reactivation_with_permission_and_reason_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "SUSPENDED", version = 1 }), ct);

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "ACTIVE", version = 2, reason = "cleared" },
                permission: "person.reactivate"),
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unrecognised_transition_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "PRE_ENROLLED", version = 1 }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("transition_not_allowed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Events_returns_the_full_lifecycle_history_including_after_archival()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions", new { target_status = "ARCHIVED", version = 1 }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/lifecycle/events"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("data").GetArrayLength());

        var snapshotResponse = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/lifecycle"), ct);
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode); // still readable
    }

    [Fact]
    public async Task Transition_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions")
        {
            Content = JsonContent.Create(new { target_status = "SUSPENDED", version = 1 }, options: PersonLifecycleJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transition_rejects_an_unrecognised_JSON_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/lifecycle/transitions");
        request.Content = new StringContent("""{"target_status":"SUSPENDED","version":1,"unexpected":"x"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
