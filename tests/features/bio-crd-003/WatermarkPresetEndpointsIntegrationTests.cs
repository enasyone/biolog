// Developer tests for BIO-CRD-003. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardTypography.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardTypography.Tests;

public sealed class WatermarkPresetEndpointsIntegrationTests : IAsyncLifetime
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
                app.UseEndpoints(WatermarkPresetEndpoints.Map);
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
    public async Task AC05_the_catalogue_lists_staff_student_and_visitor()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/watermark-presets", TestContext.Current.CancellationToken);
        var kinds = body.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Contains("staff", kinds);
        Assert.Contains("student", kinds);
        Assert.Contains("visitor", kinds);
    }

    [Fact]
    public async Task AC05_a_single_preset_is_readable_by_kind()
    {
        var response = await _client.GetAsync("/api/v1/card-designer/watermark-presets/staff", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("STAFF", body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task An_unknown_preset_kind_is_404()
    {
        var response = await _client.GetAsync("/api/v1/card-designer/watermark-presets/nonexistent", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("watermark_preset_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task No_tenant_or_actor_header_is_required()
    {
        var response = await _client.GetAsync("/api/v1/card-designer/watermark-presets", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
