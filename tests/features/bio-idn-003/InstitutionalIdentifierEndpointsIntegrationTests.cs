using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.InstitutionalIdentifiers.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-IDN-003's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="InstitutionalIdentifierEndpoints.Map"/> directly, never through
/// <c>Biolog.Api.Modules.BioIdn003Module</c> - this project has no <c>ProjectReference</c> to
/// <c>Biolog.Api.csproj</c>. Tenant context is a fixed <see cref="FakeCurrentTenantProvider"/>
/// here; the real packaged pipeline is covered by
/// <c>tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs</c>.
/// </summary>
public sealed class InstitutionalIdentifierEndpointsIntegrationTests : IAsyncLifetime
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
                services.AddSingleton<IInstitutionalIdentifierRepository, InMemoryInstitutionalIdentifierRepository>();
                services.AddSingleton<IIdentifierUniquenessRuleRepository, InMemoryIdentifierUniquenessRuleRepository>();
                services.AddSingleton<IInstitutionalIdentifierAuditSink, InMemoryInstitutionalIdentifierAuditSink>();
                services.AddSingleton<InstitutionalIdentifierLockRegistry>();
                services.AddSingleton<IdentifierUniquenessRuleLockRegistry>();
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddScoped<InstitutionalIdentifierService>();
                services.AddScoped<IdentifierUniquenessRuleService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(InstitutionalIdentifierEndpoints.Map);
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
            request.Content = JsonContent.Create(body, options: InstitutionalIdentifierJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task Assign_returns_201_with_the_created_identifier_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers", new { type = "MATRICULATION_NUMBER", value = "MAT/2026/00123" }), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("MATRICULATION_NUMBER", body.GetProperty("type").GetString());
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("sequence_number").GetInt32());
    }

    [Fact]
    public async Task Assign_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers")
        {
            Content = JsonContent.Create(new { type = "MATRICULATION_NUMBER", value = "MAT/2026/00123" }, options: InstitutionalIdentifierJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assign_a_second_active_identifier_of_the_same_type_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers", new { type = "STAFF_NUMBER", value = "STF-1" }), ct);

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers", new { type = "STAFF_NUMBER", value = "STF-2" }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("identifier_already_active", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_returns_every_identifier_type_a_person_holds()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers", new { type = "MATRICULATION_NUMBER", value = "MAT-1" }), ct);
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers", new { type = "STAFF_NUMBER", value = "STF-1" }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/identifiers"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Get_an_unassigned_identifier_type_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/identifiers/STAFF_NUMBER"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Retire_returns_the_retired_identifier_and_history_preserves_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers", new { type = "MATRICULATION_NUMBER", value = "MAT-1" }), ct);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = body.GetProperty("version").GetInt32();

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, $"/api/v1/persons/{PersonId}/identifiers/MATRICULATION_NUMBER/1/retire", new { version }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retired = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("RETIRED", retired.GetProperty("status").GetString());

        var history = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/identifiers/MATRICULATION_NUMBER/history"), ct);
        var historyBody = await history.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, historyBody.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Assign_rejects_an_unrecognised_JSON_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/identifiers");
        request.Content = new StringContent("""{"type":"STAFF_NUMBER","value":"STF-1","credential_id":"crd_x"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Uniqueness_rule_defaults_to_enforced_and_can_be_disabled_via_the_admin_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var getDefault = await _client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/identifier-types/STAFF_NUMBER/uniqueness-rule"), ct);
        var defaultBody = await getDefault.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(defaultBody.GetProperty("enforced").GetBoolean());
        Assert.True(defaultBody.GetProperty("is_default").GetBoolean());

        var setResponse = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, "/api/v1/identifier-types/STAFF_NUMBER/uniqueness-rule", new { enforced = false }), ct);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var getAfter = await _client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/identifier-types/STAFF_NUMBER/uniqueness-rule"), ct);
        var afterBody = await getAfter.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(afterBody.GetProperty("enforced").GetBoolean());
        Assert.False(afterBody.GetProperty("is_default").GetBoolean());
    }
}
