// Developer tests for BIO-CRD-002. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardDesigner.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardDesigner.Tests;

/// <summary>Real HTTP pipeline tests for BIO-CRD-002's own endpoints, self-hosted via <see cref="TestServer"/>. No tenant/actor fakes needed - both endpoints are open, non-tenant-scoped reference data.</summary>
public sealed class CardFormatEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services => services.AddRouting());
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(CardFormatEndpoints.Map);
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

    [Fact]
    public async Task AC01_the_catalogue_lists_all_three_standard_formats_with_correct_dimensions()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/card-formats", TestContext.Current.CancellationToken);

        var data = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(3, data.Count);
        var cr80 = data.Single(e => e.GetProperty("kind").GetString() == "cr80");
        Assert.Equal(85.60, cr80.GetProperty("long_edge_mm").GetDouble());
        Assert.Equal(53.98, cr80.GetProperty("short_edge_mm").GetDouble());
    }

    [Fact]
    public async Task AC03_the_catalogue_response_also_carries_the_custom_size_bounds()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/card-formats", TestContext.Current.CancellationToken);

        var bounds = body.GetProperty("custom_size_bounds");
        Assert.Equal(20.0, bounds.GetProperty("min_edge_mm").GetDouble());
        Assert.Equal(200.0, bounds.GetProperty("max_edge_mm").GetDouble());
    }

    [Fact]
    public async Task AC03_validating_a_valid_custom_size_returns_200_with_is_valid_true()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/card-designer/custom-size/validate", new { width_mm = 90.0, height_mm = 60.0 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(body.GetProperty("is_valid").GetBoolean());
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task AC03_validating_an_out_of_bounds_custom_size_still_returns_200_with_is_valid_false()
    {
        // AC03's verdict is data, not an HTTP error - the request itself was well-formed.
        var response = await _client.PostAsJsonAsync("/api/v1/card-designer/custom-size/validate", new { width_mm = 5.0, height_mm = 500.0 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(body.GetProperty("is_valid").GetBoolean());
        Assert.Equal(2, body.GetProperty("errors").EnumerateArray().Count());
    }

    [Fact]
    public async Task A_malformed_request_body_is_a_real_validation_failed_422()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/card-designer/custom-size/validate")
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task No_tenant_or_actor_header_is_required_for_either_endpoint()
    {
        var specResponse = await _client.GetAsync("/api/v1/card-designer/card-formats", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, specResponse.StatusCode);

        var validateResponse = await _client.PostAsJsonAsync("/api/v1/card-designer/custom-size/validate", new { width_mm = 50.0, height_mm = 50.0 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
    }
}
