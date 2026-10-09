using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>Developer tests for BIO-FND-008-AC01/AC02/AC03: the API actually boots and
/// serves its shell endpoints, exercised through a real in-memory HTTP pipeline rather than
/// unit-level assertions on individual modules.</summary>
public sealed class ApiShellTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiShellTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public void AC01_boots_against_development_configuration_and_layers_appsettings_development()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        Assert.Equal("Development", env.EnvironmentName);
        // appsettings.Development.json overrides this from "Information" to "Debug" -
        // proves the development file is actually layered in, not merely present on disk.
        Assert.Equal("Debug", config["Logging:LogLevel:Default"]);
    }

    [Fact]
    public async Task AC02_liveness_endpoint_reports_healthy_without_evaluating_dependency_checks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task AC02_readiness_endpoint_reports_healthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task AC03_openapi_document_is_generated_and_well_formed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: cancellationToken);

        Assert.True(document.TryGetProperty("openapi", out _), "document must declare an OpenAPI version");
        Assert.True(document.TryGetProperty("paths", out _), "document must declare a paths object");
    }
}
