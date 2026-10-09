// Developer tests for BIO-PHO-002. SYNTHETIC fixtures only - no real photo or biometric data.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.PhotoBooth.Api;
using Biolog.Features.PhotoBooth.Domain;
using Biolog.Features.PhotoBooth.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-PHO-002's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioPho002Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Tenant context, person
/// existence, device existence and master promotion are fakes here; the real packaged pipeline
/// is a separate <c>tests/dotnet/Biolog.IntegrationTests/PhotoBoothHostChannelTests.cs</c>.
/// </summary>
public sealed class PhotoBoothEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryDeviceCredentialStore _credentialStore = null!;
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceSecret = "test-device-secret";

    public async ValueTask InitializeAsync()
    {
        _credentialStore = new InMemoryDeviceCredentialStore();
        _credentialStore.Register(DeviceId, DeviceSecret);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddSingleton<IDeviceExistenceGateway>(new FakeDeviceExistenceGateway());
                services.AddSingleton<IDeviceCredentialStore>(_credentialStore);
                services.AddSingleton<IDeviceAuthenticator, HeaderDeviceAuthenticator>();
                services.AddSingleton<IPhotoMasterPromotionGateway>(new FakePhotoMasterPromotionGateway());
                services.AddSingleton<IBoothCaptureRepository, InMemoryBoothCaptureRepository>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<Audit.ICaptureAuditSink, Audit.InMemoryCaptureAuditSink>();
                services.AddSingleton<BoothCaptureLockRegistry>();
                services.AddScoped<CaptureService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(CaptureEndpoints.Map);
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

    private static byte[] CompliantContent() =>
        SyntheticImages.Png(CaptureSpecification.Current.FrameWidthPx, CaptureSpecification.Current.FrameHeightPx, CaptureSpecification.Current.MinimumDpi);

    private static HttpRequestMessage SubmitRequest(
        byte[] content, string? personId = PersonId, string? deviceId = DeviceId, string? secret = DeviceSecret,
        string? idempotencyKey = "default-key", bool faceHeightCompliant = true, bool backgroundCompliant = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-booth/captures");
        if (deviceId is not null) request.Headers.Add(HeaderDeviceAuthenticator.DeviceIdHeaderName, deviceId);
        if (secret is not null) request.Headers.Add(HeaderDeviceAuthenticator.DeviceSecretHeaderName, secret);
        if (idempotencyKey is not null) request.Headers.Add(CaptureEndpoints.IdempotencyKeyHeaderName, idempotencyKey);

        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { fileContent, "file", "capture.png" } };
        if (personId is not null) form.Add(new StringContent(personId), "person_id");
        form.Add(new StringContent(faceHeightCompliant.ToString()), "face_height_compliant");
        form.Add(new StringContent(backgroundCompliant.ToString()), "background_compliant");
        request.Content = form;
        return request;
    }

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, "reviewer_1");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: PhotoBoothJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task AC01_the_capture_specification_is_readable_without_any_authentication()
    {
        var response = await _client.GetAsync("/api/v1/photo-booth/capture-specification", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(CaptureSpecification.Current.FrameWidthPx, body.GetProperty("frame_width_px").GetInt32());
        Assert.Equal(CaptureSpecification.Current.MinimumDpi, body.GetProperty("minimum_dpi").GetInt32());
    }

    [Fact]
    public async Task AC02_a_compliant_submission_from_an_authenticated_device_succeeds()
    {
        var response = await _client.SendAsync(SubmitRequest(CompliantContent()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC02_rejects_a_submission_with_no_device_credentials_at_all()
    {
        var response = await _client.SendAsync(SubmitRequest(CompliantContent(), deviceId: null, secret: null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("device_authentication_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC02_rejects_a_submission_with_the_wrong_device_secret()
    {
        var response = await _client.SendAsync(SubmitRequest(CompliantContent(), secret: "definitely-wrong"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AC03_replaying_the_same_idempotency_key_returns_the_original_capture()
    {
        var content = CompliantContent();
        var first = await _client.SendAsync(SubmitRequest(content, idempotencyKey: "replay-key"), TestContext.Current.CancellationToken);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var replay = await _client.SendAsync(SubmitRequest(content, idempotencyKey: "replay-key"), TestContext.Current.CancellationToken);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(firstBody.GetProperty("sequence_number").GetInt32(), replayBody.GetProperty("sequence_number").GetInt32());
        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/photo-booth/captures/{PersonId}", TestContext.Current.CancellationToken);
        Assert.Single(list.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task AC03_rejects_a_submission_with_no_idempotency_key()
    {
        var response = await _client.SendAsync(SubmitRequest(CompliantContent(), idempotencyKey: null), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC04_a_low_quality_submission_is_rejected_and_never_reaches_approve()
    {
        var tooSmall = SyntheticImages.Png(10, 10);
        var submitResponse = await _client.SendAsync(SubmitRequest(tooSmall), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, submitResponse.StatusCode);
        var body = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        var sequence = body.GetProperty("sequence_number").GetInt32();

        var approveResponse = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/photo-booth/captures/{PersonId}/{sequence}/approve", new { expected_version = 1 }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, approveResponse.StatusCode);
        var approveBody = await approveResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("capture_not_pending", approveBody.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC04_approving_a_pending_capture_promotes_it()
    {
        var submitResponse = await _client.SendAsync(SubmitRequest(CompliantContent()), TestContext.Current.CancellationToken);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var sequence = submitBody.GetProperty("sequence_number").GetInt32();

        var approveResponse = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/photo-booth/captures/{PersonId}/{sequence}/approve", new { expected_version = 1 }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var body = await approveResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Promoted", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("promoted_master_version").GetInt32() > 0);
    }

    [Fact]
    public async Task Rejecting_a_pending_capture_requires_an_asserted_actor()
    {
        var submitResponse = await _client.SendAsync(SubmitRequest(CompliantContent()), TestContext.Current.CancellationToken);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var sequence = submitBody.GetProperty("sequence_number").GetInt32();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/photo-booth/captures/{PersonId}/{sequence}/reject")
        {
            Content = JsonContent.Create(new { reason = "blurry", expected_version = 1 }, options: PhotoBoothJsonOptions.Strict),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }
}
