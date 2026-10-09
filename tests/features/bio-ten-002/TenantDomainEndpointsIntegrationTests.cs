using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.TenantDomainRegistry.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-TEN-002's own admin/resolve endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="TenantDomainEndpoints.Map"/> directly rather than going through
/// <c>Biolog.Api.Modules.BioTen002Module</c> - that adapter now lives in
/// <c>api/Biolog.Api/Modules/</c> (BIO-TEN-002's own QA-01 closure), and this project
/// deliberately has no <c>ProjectReference</c> back to <c>Biolog.Api.csproj</c> (one-way
/// dependency, <c>scripts/check-module-project-references.sh</c>). This registration list must
/// stay identical in substance to <c>BioTen002Module.RegisterServices</c>/<c>MapEndpoints</c>.
/// The real packaged pipeline - <c>Biolog.Api</c>'s actual <c>Program.cs</c> plus
/// <c>TenantContextMiddleware</c>'s host-channel wiring - is covered separately by
/// <c>tests/dotnet/Biolog.IntegrationTests/TenantContextHostChannelTests.cs</c>.
/// </summary>
public sealed class TenantDomainEndpointsIntegrationTests : IAsyncLifetime
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
                services.AddSingleton<ITenantDomainRegistry, InMemoryTenantDomainRegistry>();
                services.AddSingleton<ITenantDomainAuditSink, InMemoryTenantDomainAuditSink>();
                services.AddSingleton<TenantDomainService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(TenantDomainEndpoints.Map);
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
            request.Content = JsonContent.Create(body, options: TenantDomainJsonOptions.Strict);
        }

        return request;
    }

    private static object ValidRegisterBody(string hostname = "portal.example.edu.ng") => new { hostname, tenant_id = TenantId };

    [Fact]
    public async Task Register_returns_201_with_the_created_domain_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody()), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("portal.example.edu.ng", body.GetProperty("hostname").GetString());
        Assert.Equal("PENDING_VERIFICATION", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("is_primary").GetBoolean());
    }

    [Fact]
    public async Task Register_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/control-plane/tenant-domains")
        {
            Content = JsonContent.Create(ValidRegisterBody(), options: TenantDomainJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_with_an_unrecognised_field_is_a_validation_problem_response_not_a_500()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = new StringContent(
            $$"""{"hostname":"portal.example.edu.ng","tenant_id":"{{TenantId}}","a_record":"203.0.113.10"}""",
            System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/control-plane/tenant-domains") { Content = payload };
        request.Headers.Add(ActorResolver.ActorIdHeader, "admin_alice");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_of_a_duplicate_hostname_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody()), ct);

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody()), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("duplicate_hostname", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_of_an_unknown_hostname_is_404_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/control-plane/tenant-domains/nobody.example.edu.ng", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_domain_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_then_get_round_trips_the_domain()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("roundtrip.example.edu.ng")), ct);

        var getResponse = await _client.GetAsync("/api/v1/control-plane/tenant-domains/roundtrip.example.edu.ng", ct);
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("roundtrip.example.edu.ng", fetched.GetProperty("hostname").GetString());
    }

    [Fact]
    public async Task List_filters_by_tenant_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("list-a.example.edu.ng")), ct);
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("list-b.example.edu.ng")), ct);

        var response = await _client.GetAsync($"/api/v1/control-plane/tenant-domains?tenant_id={TenantId}", ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task List_without_tenant_id_is_a_validation_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/control-plane/tenant-domains", ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Status_update_then_get_reflects_verified()
    {
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("status.example.edu.ng")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = created.GetProperty("version").GetInt32();

        var statusResponse = await _client.SendAsync(AdminRequest(
            HttpMethod.Put, "/api/v1/control-plane/tenant-domains/status.example.edu.ng/status", new { version, status = "VERIFIED" }), ct);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var updated = await statusResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("VERIFIED", updated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SetPrimary_on_an_unverified_domain_is_a_validation_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("primary.example.edu.ng")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = created.GetProperty("version").GetInt32();

        var response = await _client.SendAsync(AdminRequest(
            HttpMethod.Put, "/api/v1/control-plane/tenant-domains/primary.example.edu.ng/primary", new { version }), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// BIO-TEN-002-QA-01 fix: the resolve endpoint reads the request's real HTTP <c>Host</c>
    /// header, never a query parameter - so exercising it in a test means setting that header,
    /// the same way DNS-based routing (or a curl <c>-H "Host: ..."</c>) would in a real
    /// deployment, not appending <c>?host=</c>.
    /// </summary>
    private static HttpRequestMessage ResolveRequest(string host) =>
        new(HttpMethod.Get, "/api/v1/tenant-domains/resolve") { Headers = { Host = host } };

    [Fact]
    public async Task Resolve_of_a_verified_host_returns_its_tenant_with_no_actor_required()
    {
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("resolve.example.edu.ng")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = created.GetProperty("version").GetInt32();
        await _client.SendAsync(AdminRequest(
            HttpMethod.Put, "/api/v1/control-plane/tenant-domains/resolve.example.edu.ng/status", new { version, status = "VERIFIED" }), ct);

        // No X-Actor-Id: resolution is the anonymous, request-time host channel, not admin CRUD.
        var response = await _client.SendAsync(ResolveRequest("RESOLVE.example.edu.ng"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantId, body.GetProperty("tenant_id").GetString());
        Assert.Equal("resolve.example.edu.ng", body.GetProperty("hostname").GetString());
    }

    [Fact]
    public async Task Resolve_of_an_unknown_host_is_404_host_not_recognised()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ResolveRequest("nobody.example.edu.ng"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("host_not_recognised", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_of_a_pending_verification_host_is_404_host_not_recognised_identically()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("pending.example.edu.ng")), ct);

        var response = await _client.SendAsync(ResolveRequest("pending.example.edu.ng"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("host_not_recognised", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_ignores_a_query_string_host_parameter_entirely()
    {
        // BIO-TEN-002-QA-01: a caller-controlled query parameter is not a channel (ADR-0002
        // §1.1). Registering the tenant under the REAL Host header value and passing a
        // DIFFERENT, verified-sounding hostname via ?host= must not affect the outcome -
        // proving the endpoint no longer even looks at the query string.
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", ValidRegisterBody("real-host.example.edu.ng")), ct);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = created.GetProperty("version").GetInt32();
        await _client.SendAsync(AdminRequest(
            HttpMethod.Put, "/api/v1/control-plane/tenant-domains/real-host.example.edu.ng/status", new { version, status = "VERIFIED" }), ct);

        var request = ResolveRequest("nobody.example.edu.ng");
        request.RequestUri = new Uri("/api/v1/tenant-domains/resolve?host=real-host.example.edu.ng", UriKind.Relative);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // the real (unverified-for-this-name) Host header governs, not the query string
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_back_verbatim()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = AdminRequest(HttpMethod.Get, $"/api/v1/control-plane/tenant-domains?tenant_id={TenantId}");
        var supplied = "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79";
        request.Headers.Add(CorrelationIdHeader.HeaderName, supplied);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(supplied, response.Headers.GetValues(CorrelationIdHeader.HeaderName).Single());
    }
}
