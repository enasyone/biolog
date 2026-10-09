// Developer tests for BIO-CRD-008. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardTemplates.Api;
using Biolog.Features.CardTemplates.Domain;
using Biolog.Features.CardTemplates.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardTemplates.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-CRD-008's own endpoints, self-hosted via
/// <see cref="TestServer"/> - the same shape BIO-RPT-002's own
/// <c>ReportEndpointsIntegrationTests</c> establishes. Wires this project's own DI registrations
/// and <c>CardTemplateEndpoints.Map</c> directly, never through
/// <c>Biolog.Api.Modules.BioCrd008Module</c> (no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>).
/// </summary>
public sealed class CardTemplateEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;
    private FakeSignatureAssetGateway _signatureGateway = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TestData.TenantA);
        _signatureGateway = new FakeSignatureAssetGateway();

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<ISignatureAssetGateway>(_signatureGateway);
                services.AddSingleton<ICardTemplateRepository, InMemoryCardTemplateRepository>();
                services.AddSingleton<Audit.ICardTemplateAuditSink, Audit.InMemoryCardTemplateAuditSink>();
                services.AddScoped<CardTemplateService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(CardTemplateEndpoints.Map);
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

    private static HttpRequestMessage WithActor(HttpMethod method, string path, object? body = null) => new(method, path)
    {
        Content = body is null ? null : JsonContent.Create(body),
        Headers = { { "X-Actor-Id", "operator-1" } },
    };

    private static object MinimalSnapshotBody() => new
    {
        format_kind = "cr80",
        orientation = "portrait",
        front_elements = new[]
        {
            new { id = "el-1", kind = "text", label = "Name", x_px = 10, y_px = 10, width_px = 200, height_px = 40, z_index = 0, font_family = "Inter", font_size_px = 14, text = "{{full_name}}" },
        },
        back_elements = Array.Empty<object>(),
        watermark = "staff",
        barcode = "qr",
    };

    [Fact]
    public async Task AC01_full_lifecycle_draft_to_published_then_immutable()
    {
        var created = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/card-templates", new { name = "Staff ID v1", snapshot = MinimalSnapshotBody() }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var templateId = created.GetProperty("template_id").GetString()!;
        var version = created.GetProperty("version").GetInt32();
        var revision = created.GetProperty("revision").GetInt32();
        Assert.Equal("draft", created.GetProperty("status").GetString());

        var submitted = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/{version}/submit", new { expected_revision = revision }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("validation", submitted.GetProperty("status").GetString());

        var validated = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/{version}/validate", new { passed = true, expected_revision = submitted.GetProperty("revision").GetInt32() }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("approval", validated.GetProperty("status").GetString());

        var publishResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/{version}/publish", new { expected_revision = validated.GetProperty("revision").GetInt32() }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("published", published.GetProperty("status").GetString());

        // AC01: now immutable - an edit attempt is refused, never silently applied
        var editResponse = await _client.SendAsync(
            WithActor(HttpMethod.Put, $"/api/v1/card-templates/{templateId}/versions/{version}", new { snapshot = MinimalSnapshotBody(), expected_revision = published.GetProperty("revision").GetInt32() }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
        var problem = await editResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("card_template_immutable", problem.GetProperty("code").GetString());

        // AC04: production's one sanctioned source resolves the exact published pair
        var publishedLookup = await (await _client.GetAsync($"/api/v1/card-templates/{templateId}/published", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(version, publishedLookup.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task AC02_a_new_version_from_a_published_template_is_created_via_its_own_dedicated_endpoint()
    {
        var templateId = await CreateAndPublishAsync();

        var nextResponse = await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/1/new-version", new { }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, nextResponse.StatusCode);
        var next = await nextResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(2, next.GetProperty("version").GetInt32());
        Assert.Equal("draft", next.GetProperty("status").GetString());
    }

    [Fact]
    public async Task an_unwhitelisted_format_kind_is_422_not_a_500()
    {
        var badBody = new
        {
            name = "Bad",
            snapshot = new
            {
                format_kind = "letter", // not a governed CardFormatKind
                orientation = "portrait",
                front_elements = Array.Empty<object>(),
                back_elements = Array.Empty<object>(),
            },
        };

        var response = await _client.SendAsync(WithActor(HttpMethod.Post, "/api/v1/card-templates", badBody), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task creating_a_draft_without_an_actor_header_is_400()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/card-templates") { Content = JsonContent.Create(new { name = "x", snapshot = MinimalSnapshotBody() }) },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task getting_an_unknown_template_is_404_not_a_500()
    {
        var response = await _client.GetAsync("/api/v1/card-templates/tpl_018f2e5a-9999-7abc-89ab-0123456789ab/versions/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task no_route_here_collides_with_any_other_card_module_own_paths()
    {
        // BIO-RPT-002/BIO-RPT-001's own lesson: check the actual mapped route surface, not just
        // assume a fresh prefix is safe. `/api/v1/card-templates` is this story's own, never
        // reused by BIO-CRD-002/003/004/005/006/007's own endpoint groups (grepped the whole
        // api/ tree before mapping - see docs/features/bio-crd-008/README.md).
        var response = await _client.GetAsync("/api/v1/card-designer/save", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // not mapped by this project at all - would 404, never ambiguous-match at startup
    }

    private async Task<string> CreateAndPublishAsync()
    {
        var created = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, "/api/v1/card-templates", new { name = "Staff ID v1", snapshot = MinimalSnapshotBody() }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var templateId = created.GetProperty("template_id").GetString()!;
        var revision = created.GetProperty("revision").GetInt32();

        var submitted = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/1/submit", new { expected_revision = revision }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var validated = await (await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/1/validate", new { passed = true, expected_revision = submitted.GetProperty("revision").GetInt32() }),
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        await _client.SendAsync(
            WithActor(HttpMethod.Post, $"/api/v1/card-templates/{templateId}/versions/1/publish", new { expected_revision = validated.GetProperty("revision").GetInt32() }),
            TestContext.Current.CancellationToken);

        return templateId;
    }
}
