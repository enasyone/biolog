// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Biolog.Features.CardObjects.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardObjects.Tests;

public sealed class BarcodeProfileEndpointsIntegrationTests : IAsyncLifetime
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
                app.UseEndpoints(BarcodeProfileEndpoints.Map);
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
    public async Task AC01_AC02_the_catalogue_names_alphanumeric_as_default_and_the_rest_as_optional()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/barcode-profiles", TestContext.Current.CancellationToken);
        Assert.Equal("alphanumeric", body.GetProperty("default_profile").GetString());

        var entries = body.GetProperty("data").EnumerateArray().ToDictionary(
            e => e.GetProperty("profile").GetString()!, e => e.GetProperty("is_default").GetBoolean());

        Assert.True(entries["alphanumeric"]);
        Assert.False(entries["qr"]);
        Assert.False(entries["pdf417"]);
        Assert.False(entries["data_matrix"]);
        Assert.False(entries["aztec"]);
    }

    [Fact]
    public async Task AC03_a_well_formed_barcode_configuration_validates_as_data_200_not_an_HTTP_error()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/card-designer/barcode/validate", new
        {
            profile = "qr",
            quiet_zone_mm = 3.0,
            width_mm = 20.0,
            height_mm = 20.0,
            foreground_color_hex = "#000000",
            background_color_hex = "#ffffff",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(body.GetProperty("is_valid").GetBoolean());
        Assert.Empty(body.GetProperty("violations").EnumerateArray());
    }

    [Fact]
    public async Task AC03_an_invalid_barcode_configuration_is_still_data_200_with_named_violations()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/card-designer/barcode/validate", new
        {
            profile = "alphanumeric",
            quiet_zone_mm = 0.0,
            width_mm = 20.0,
            height_mm = 20.0,
            foreground_color_hex = "#808080",
            background_color_hex = "#808080",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(body.GetProperty("is_valid").GetBoolean());
        Assert.NotEmpty(body.GetProperty("violations").EnumerateArray());
    }

    [Fact]
    public async Task An_unknown_profile_on_the_validate_endpoint_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/card-designer/barcode/validate", new
        {
            profile = "code39",
            quiet_zone_mm = 3.0,
            width_mm = 20.0,
            height_mm = 20.0,
            foreground_color_hex = "#000000",
            background_color_hex = "#ffffff",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("barcode_profile_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Malformed_JSON_on_the_validate_endpoint_is_a_400_problem_response_not_a_500()
    {
        var response = await _client.PostAsync(
            "/api/v1/card-designer/barcode/validate",
            new StringContent("{not-json", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task No_tenant_or_actor_header_is_required_for_either_endpoint()
    {
        var catalogue = await _client.GetAsync("/api/v1/card-designer/barcode-profiles", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, catalogue.StatusCode);
    }
}
