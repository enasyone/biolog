using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.PersonProfileRegistry.Api;
using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.PersonProfileRegistry.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-IDN-002's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="PersonProfileEndpoints.Map"/> directly, never through
/// <c>Biolog.Api.Modules.BioIdn002Module</c> - this project has no <c>ProjectReference</c> to
/// <c>Biolog.Api.csproj</c>. Tenant context is a fixed <see cref="FakeCurrentTenantProvider"/>
/// here; the real packaged pipeline is covered by
/// <c>tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs</c>.
/// </summary>
public sealed class PersonProfileEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    public async ValueTask InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IPersonProfileRepository, InMemoryPersonProfileRepository>();
                services.AddSingleton<IPersonProfileAuditSink, InMemoryPersonProfileAuditSink>();
                services.AddSingleton<PersonProfileLockRegistry>();
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddScoped<PersonProfileService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(PersonProfileEndpoints.Map);
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, "registrar_ada");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: PersonProfileJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task Assign_returns_201_with_the_created_profile_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles", new { type = "STUDENT" }), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("STUDENT", body.GetProperty("type").GetString());
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("is_currently_in_effect").GetBoolean());
    }

    [Fact]
    public async Task Assign_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles")
        {
            Content = JsonContent.Create(new { type = "STUDENT" }, options: PersonProfileJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assign_the_same_type_twice_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles", new { type = "STUDENT" }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles", new { type = "STUDENT" }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("duplicate_profile_type", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_returns_every_profile_type_a_person_holds()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles", new { type = "STUDENT" }), ct);
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles", new { type = "EMPLOYEE" }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/profiles"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Get_an_unassigned_profile_type_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/profiles/EMPLOYEE"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_returns_the_revoked_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles", new { type = "STUDENT" }), ct);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = body.GetProperty("version").GetInt32();

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, $"/api/v1/persons/{PersonId}/profiles/STUDENT/revoke", new { version }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var revoked = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("REVOKED", revoked.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Assign_rejects_an_unrecognised_JSON_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/profiles");
        request.Content = new StringContent("""{"type":"STUDENT","role":"admin"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
