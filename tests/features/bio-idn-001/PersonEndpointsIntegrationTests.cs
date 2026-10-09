using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.PersonRegistry.Api;
using Biolog.Features.PersonRegistry.Audit;
using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.PersonRegistry.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.PersonRegistry.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-IDN-001's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="PersonEndpoints.Map"/> directly - never through
/// <c>Biolog.Api.Modules.BioIdn001Module</c>, which lives in <c>api/Biolog.Api/Modules/</c>
/// and this project has no <c>ProjectReference</c> to (one-way dependency). Tenant context is
/// supplied by a fixed <see cref="FakeCurrentTenantProvider"/> here, proving this feature's
/// OWN logic against a resolved tenant (ADR-0002 Decision 6's "injected-resolved-context
/// test" half). The real packaged pipeline - actual <c>Program.cs</c>, actual
/// <c>TenantContextMiddleware</c>, an actual verified custom hostname registered through
/// BIO-TEN-002's own endpoints - is covered separately by
/// <c>tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs</c> (Decision 6's
/// "real shipped-host request" half).
/// </summary>
public sealed class PersonEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    public async ValueTask InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IPersonRepository, InMemoryPersonRepository>();
                services.AddSingleton<IPersonAuditSink, InMemoryPersonAuditSink>();
                services.AddSingleton<PersonLockRegistry>();
                services.AddScoped<PersonService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(PersonEndpoints.Map);
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
            request.Content = JsonContent.Create(body, options: PersonJsonOptions.Strict);
        }

        return request;
    }

    private static object ValidRegisterBody(string firstName = "Ngozi", string lastName = "Okafor") => new { first_name = firstName, last_name = lastName };

    [Fact]
    public async Task Register_returns_201_with_the_created_person_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", ValidRegisterBody()), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Ngozi", body.GetProperty("first_name").GetString());
        Assert.Equal("PRE_ENROLLED", body.GetProperty("status").GetString());
        Assert.Equal("Ngozi Okafor", body.GetProperty("full_name").GetString());
    }

    [Fact]
    public async Task Register_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/persons")
        {
            Content = JsonContent.Create(ValidRegisterBody(), options: PersonJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_rejects_an_unrecognised_JSON_field_a_role_smuggling_attempt()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = ActorRequest(HttpMethod.Post, "/api/v1/persons");
        request.Content = new StringContent(
            """{"first_name":"Ngozi","last_name":"Okafor","role":"student"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_registered_person()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", ValidRegisterBody()), ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = createdBody.GetProperty("id").GetString();

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{id}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_an_unknown_person_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/persons/per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateStatus_transitions_and_returns_the_updated_person()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", ValidRegisterBody()), ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = createdBody.GetProperty("id").GetString();
        var version = createdBody.GetProperty("version").GetInt32();

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, $"/api/v1/persons/{id}/status", new { version, status = "VERIFIED" }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("VERIFIED", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateStatus_to_an_illegal_transition_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", ValidRegisterBody()), ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = createdBody.GetProperty("id").GetString();
        var version = createdBody.GetProperty("version").GetInt32();

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, $"/api/v1/persons/{id}/status", new { version, status = "ACTIVE" }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("invalid_status_transition", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_returns_only_registered_people_for_this_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", ValidRegisterBody()), ct);
        await _client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", ValidRegisterBody("Chidi", "Eze")), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/persons"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());
    }
}
