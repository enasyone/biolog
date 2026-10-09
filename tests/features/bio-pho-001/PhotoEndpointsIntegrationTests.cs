// Developer tests for BIO-PHO-001. SYNTHETIC fixtures only - no real photo or biometric data.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.Photography.Api;
using Biolog.Features.Photography.Domain;
using Biolog.Features.Photography.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.Photography.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-PHO-001's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioPho001Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Tenant context and the
/// existence gateway are fakes here; the real packaged pipeline is a separate
/// <c>tests/dotnet/Biolog.IntegrationTests/PhotographyHostChannelTests.cs</c>.
/// </summary>
public sealed class PhotoEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string OtherPersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5c";

    public async ValueTask InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddSingleton<IPhotoMasterRepository, InMemoryPhotoMasterRepository>();
                services.AddSingleton<IPhotoDerivativeRepository, InMemoryPhotoDerivativeRepository>();
                services.AddSingleton<IPhotoAssignmentRepository, InMemoryPhotoAssignmentRepository>();
                services.AddSingleton<IPhotoDerivativeGenerator, MetadataOnlyPhotoDerivativeGenerator>();
                services.AddSingleton<Audit.IPhotoAuditSink, Audit.InMemoryPhotoAuditSink>();
                services.AddSingleton<PhotoMasterLockRegistry>();
                services.AddSingleton<PhotoAssignmentLockRegistry>();
                services.AddScoped<PhotoMasterService>();
                services.AddScoped<PhotoDerivativeService>();
                services.AddScoped<PhotoAssignmentService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(PhotoEndpoints.Map);
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, "operator_alice");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: PhotoJsonOptions.Strict);
        }

        return request;
    }

    private static MultipartFormDataContent PhotoForm(byte[] content, string fileName, string contentType)
    {
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { fileContent, "file", fileName } };
    }

    private async Task<JsonElement> AppendMasterAsync(string personId, byte[] content, string fileName, string contentType)
    {
        var request = ActorRequest(HttpMethod.Post, $"/api/v1/people/{personId}/photo-masters");
        request.Content = PhotoForm(content, fileName, contentType);
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC01_appending_a_png_master_returns_version_1()
    {
        var body = await AppendMasterAsync(PersonId, SyntheticImages.Png(300, 200), "photo.png", "image/png");

        Assert.Equal(1, body.GetProperty("version").GetInt32());
        Assert.Equal("png", body.GetProperty("format").GetString());
        Assert.Equal(PersonId, body.GetProperty("person_id").GetString());
    }

    [Fact]
    public async Task AC01_a_second_upload_creates_version_2_and_version_1_remains_readable_unchanged()
    {
        await AppendMasterAsync(PersonId, SyntheticImages.Png(300, 200), "v1.png", "image/png");
        var second = await AppendMasterAsync(PersonId, SyntheticImages.Jpeg(640, 480), "v2.jpg", "image/jpeg");
        Assert.Equal(2, second.GetProperty("version").GetInt32());

        var getV1 = await _client.GetAsync($"/api/v1/people/{PersonId}/photo-masters/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getV1.StatusCode);
        var v1Body = await getV1.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("png", v1Body.GetProperty("format").GetString());
        Assert.Equal(300, v1Body.GetProperty("width_px").GetInt32());
    }

    [Fact]
    public async Task AC01_rejects_an_unsupported_file_format()
    {
        var request = ActorRequest(HttpMethod.Post, $"/api/v1/people/{PersonId}/photo-masters");
        request.Content = PhotoForm([0x00, 0x01, 0x02, 0x03], "file.bin", "application/octet-stream");
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("unsupported_format", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejects_an_upload_with_no_asserted_actor()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/people/{PersonId}/photo-masters")
        {
            Content = PhotoForm(SyntheticImages.Png(10, 10), "c.png", "image/png"),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC02_AC03_regenerating_a_derivative_references_the_master_and_leaves_it_unchanged()
    {
        var master = await AppendMasterAsync(PersonId, SyntheticImages.Png(300, 200), "photo.png", "image/png");
        var version = master.GetProperty("version").GetInt32();

        var regenerate = ActorRequest(HttpMethod.Post, $"/api/v1/people/{PersonId}/photo-masters/{version}/derivatives/print/regenerate");
        var regenerateResponse = await _client.SendAsync(regenerate, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);
        var derivativeBody = await regenerateResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(version, derivativeBody.GetProperty("master_version").GetInt32());
        Assert.Equal("print", derivativeBody.GetProperty("kind").GetString());

        // Regenerate again - the master is still readable, unchanged, still at version 1.
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/people/{PersonId}/photo-masters/{version}/derivatives/print/regenerate"), TestContext.Current.CancellationToken);
        var getMaster = await _client.GetAsync($"/api/v1/people/{PersonId}/photo-masters/{version}", TestContext.Current.CancellationToken);
        var masterBody = await getMaster.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, masterBody.GetProperty("version").GetInt32());
        var allMasters = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/people/{PersonId}/photo-masters", TestContext.Current.CancellationToken);
        Assert.Single(allMasters.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task AC04_assigning_a_photo_then_reassigning_requires_the_current_expected_version()
    {
        var master1 = await AppendMasterAsync(PersonId, SyntheticImages.Png(10, 10), "v1.png", "image/png");
        var master2 = await AppendMasterAsync(PersonId, SyntheticImages.Jpeg(20, 20), "v2.jpg", "image/jpeg");

        var firstAssign = ActorRequest(HttpMethod.Put, $"/api/v1/people/{PersonId}/photo-assignment",
            new { master_version = master1.GetProperty("version").GetInt32() });
        var firstResponse = await _client.SendAsync(firstAssign, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, firstBody.GetProperty("version").GetInt32());

        // Reassigning without expected_version fails closed.
        var missingPrecondition = ActorRequest(HttpMethod.Put, $"/api/v1/people/{PersonId}/photo-assignment",
            new { master_version = master2.GetProperty("version").GetInt32() });
        var missingResponse = await _client.SendAsync(missingPrecondition, TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)428, missingResponse.StatusCode);

        var secondAssign = ActorRequest(HttpMethod.Put, $"/api/v1/people/{PersonId}/photo-assignment",
            new { master_version = master2.GetProperty("version").GetInt32(), expected_version = 1 });
        var secondResponse = await _client.SendAsync(secondAssign, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(2, secondBody.GetProperty("version").GetInt32());
        Assert.Equal(master2.GetProperty("version").GetInt32(), secondBody.GetProperty("current_master_version").GetInt32());
    }

    [Fact]
    public async Task AC04_a_stale_expected_version_is_refused_with_a_concurrency_conflict()
    {
        var master1 = await AppendMasterAsync(PersonId, SyntheticImages.Png(10, 10), "v1.png", "image/png");
        await _client.SendAsync(
            ActorRequest(HttpMethod.Put, $"/api/v1/people/{PersonId}/photo-assignment", new { master_version = master1.GetProperty("version").GetInt32() }),
            TestContext.Current.CancellationToken);

        var stale = ActorRequest(HttpMethod.Put, $"/api/v1/people/{PersonId}/photo-assignment",
            new { master_version = master1.GetProperty("version").GetInt32(), expected_version = 999 });
        var response = await _client.SendAsync(stale, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("concurrency_conflict", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC05_a_deterministically_seeded_demo_photo_reads_back_identically_on_every_request()
    {
        // BIO-PHO-001-AC05: the stronger property - re-SEEDING never creates a duplicate
        // master or changes the assignment - is proven directly against the services in
        // DemoPhotoCatalogTests (no HTTP layer needed to prove it). This test proves the HTTP
        // read path is equally stable once seeded: DemoPhotoCatalog.SyntheticContentFor is a
        // pure, deterministic function of the person id, and the assignment it produces reads
        // back identically no matter how many times a caller asks.
        var master = await AppendMasterAsync(PersonId, DemoPhotoCatalog.SyntheticContentFor(PersonId), "demo.png", "image/png");
        var assign = ActorRequest(HttpMethod.Put, $"/api/v1/people/{PersonId}/photo-assignment", new { master_version = master.GetProperty("version").GetInt32() });
        var first = await _client.SendAsync(assign, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var getResponse1 = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/people/{PersonId}/photo-assignment", TestContext.Current.CancellationToken);
        var getResponse2 = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/people/{PersonId}/photo-assignment", TestContext.Current.CancellationToken);

        Assert.Equal(getResponse1.GetProperty("current_master_version").GetInt32(), getResponse2.GetProperty("current_master_version").GetInt32());
    }

    [Fact]
    public async Task Getting_a_photo_assignment_that_was_never_created_is_404()
    {
        var response = await _client.GetAsync($"/api/v1/people/{OtherPersonId}/photo-assignment", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
