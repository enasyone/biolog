using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host (Program.cs's actual
/// module list and middleware pipeline via <see cref="WebApplicationFactory{TEntryPoint}"/>),
/// that BIO-TEN-002's host channel is genuinely shipped - not just correct in isolation.
///
/// <c>Biolog.Features.TenantDomainRegistry.Tests.TenantDomainEndpointsIntegrationTests</c>
/// self-hosts ONLY the feature project against its own bare <c>TestServer</c>, deliberately
/// never touching <c>Biolog.Api</c>'s <c>Program.cs</c> - that was the exact gap Seyi's round-2
/// review named (BIO-TEN-002-QA-01 follow-up): a correct-and-tested resolver that the
/// packaged API never actually reached. These tests instead boot the same <c>Program</c>
/// class the packaged API ships, so <see cref="Biolog.Api.Modules.BioTen002Module"/> being
/// registered in Program.cs's module array and
/// <see cref="Biolog.Features.TenantDomainRegistry.TenantDomainService"/> resolving cleanly
/// from the real DI container inside <see cref="Biolog.Api.Tenancy.TenantContextMiddleware"/>
/// are both exercised, not merely asserted.
///
/// <c>Biolog.UnitTests.Tenancy.TenantContextMiddlewareTests</c> covers the complementary
/// concern this file cannot cheaply cover: direct assertions on
/// <see cref="Biolog.Api.Tenancy.ITenantContextAccessor.Context"/>'s exact shape (tenant id,
/// channel list) for verified/unknown/unverified hosts, against a hand-constructed
/// <c>TenantDomainService</c>. Neither file alone proves both "the logic is correct" and "the
/// logic is what actually runs in the shipped host" - together they do.
/// </summary>
public sealed class TenantContextHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    public TenantContextHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static HttpRequestMessage AdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Actor-Id", "admin_alice");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<string> RegisterAndVerifyAsync(HttpClient client, string hostname, CancellationToken ct)
    {
        var registerResponse = await client.SendAsync(
            AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname, tenant_id = TenantId }), ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = registered.GetProperty("version").GetInt32();

        var verifyResponse = await client.SendAsync(
            AdminRequest(HttpMethod.Put, $"/api/v1/control-plane/tenant-domains/{hostname}/status", new { version, status = "VERIFIED" }), ct);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        return hostname;
    }

    [Fact]
    public async Task BioTen002Module_is_registered_in_the_packaged_host_and_TenantDomainService_resolves_from_real_DI()
    {
        // If BioTen002Module were still unregistered (the state this test would have caught
        // before this story's integration), this request 404s at the routing layer before
        // ever reaching TenantDomainService - there would be no route at all.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(
            AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname = "boot-check.example.edu.ng", tenant_id = TenantId }), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_verified_host_resolves_through_the_real_packaged_pipeline()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyAsync(client, "portal-e2e.example.edu.ng", ct);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenant-domains/resolve") { Headers = { Host = hostname } };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantId, body.GetProperty("tenant_id").GetString());
        Assert.Equal("VERIFIED", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unknown_host_fails_closed_as_host_not_recognised_through_the_real_packaged_pipeline()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenant-domains/resolve")
        {
            Headers = { Host = "nobody-registered-this-e2e.example.edu.ng" },
        };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("host_not_recognised", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unverified_pending_host_fails_closed_identically_through_the_real_packaged_pipeline()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = "pending-e2e.example.edu.ng";
        var registerResponse = await client.SendAsync(
            AdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname, tenant_id = TenantId }), ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenant-domains/resolve") { Headers = { Host = hostname } };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("host_not_recognised", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unrelated_endpoint_still_succeeds_under_an_arbitrary_Host_header()
    {
        // TenantContextMiddleware runs on every request (BIO-FND-008-AC04). If DI failed to
        // resolve TenantDomainService for the middleware constructor/method injection, every
        // request - not just tenant-domain ones - would 500. This is the regression that
        // registering the module wrong, or not at all, would actually produce.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live") { Headers = { Host = "whatever-e2e.example.edu.ng" } };
        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
