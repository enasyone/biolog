// Developer tests for BIO-PHO-003. SYNTHETIC fixtures only - no real photo or biometric data.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.PhotoQuality.Api;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.PhotoQuality.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-PHO-003's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioPho003Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Tenant context, person
/// existence, biometric suitability and master promotion are fakes here - mirroring
/// BIO-PHO-002's own <c>PhotoBoothEndpointsIntegrationTests</c>.
///
/// <b>ZB-PHO003-01, fixed.</b> <see cref="ActorProvenanceGate"/> uses a REAL
/// <see cref="HmacActorProvenanceStore"/> configured with <see cref="ProvenanceSecret"/> (never
/// a test-only bypass of the gate itself); <see cref="_permission"/> is this project's own
/// controllable <c>FakePermissionGateway</c> (feature-local, public interface - this project has
/// no access to BIO-IDN-006's internals and never needs it, since the real RBAC wiring is
/// BioPho003Module's own concern). Defaults to granted so every pre-existing AC01/AC02/AC03
/// positive test keeps proving its own business-logic claim without also having to thread a
/// permission-denial concern through each one; <see cref="PermissionRequiredAsync"/> proves the
/// denial path explicitly instead.
/// </summary>
public sealed class PhotoQualityEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeBiometricSuitabilityGateway _biometric = null!;
    private FakePermissionGateway _permission = null!;
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string ProvenanceSecret = "test-self-hosted-photo-quality-actor-provenance-secret";
    private const string ActorId = "per_staff_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5c";

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    public async ValueTask InitializeAsync()
    {
        _biometric = new FakeBiometricSuitabilityGateway();
        _permission = new FakePermissionGateway();

        var provenanceStore = new HmacActorProvenanceStore();
        provenanceStore.Configure(ProvenanceSecret);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddSingleton<IBiometricSuitabilityGateway>(_biometric);
                services.AddSingleton<IPhotoMasterPromotionGateway>(new FakePhotoMasterPromotionGateway());
                services.AddSingleton<IPhotoQualityCaptureRepository, InMemoryPhotoQualityCaptureRepository>();
                services.AddSingleton<Audit.IPhotoQualityAuditSink, Audit.InMemoryPhotoQualityAuditSink>();
                services.AddSingleton<PhotoQualityCaptureLockRegistry>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
                services.AddSingleton<IPermissionGateway>(_permission);
                services.AddScoped<PhotoQualityService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(PhotoQualityEndpoints.Map);
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

    private static byte[] CompliantPrintContent() =>
        SyntheticImages.Png(PrintQualitySpecification.Current.FrameWidthPx, PrintQualitySpecification.Current.FrameHeightPx, PrintQualitySpecification.Current.MinimumDpi);

    private static HttpRequestMessage SubmitRequest(byte[] content, string? personId = PersonId, string? actorId = ActorId, bool withProvenance = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-quality/captures");
        if (actorId is not null)
        {
            request.Headers.Add(ActorResolver.ActorIdHeader, actorId);
            if (withProvenance) request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(actorId));
        }

        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { fileContent, "file", "capture.png" } };
        if (personId is not null) form.Add(new StringContent(personId), "person_id");
        request.Content = form;
        return request;
    }

    [Fact]
    public async Task AC01_a_real_http_submission_with_a_failing_biometric_result_is_still_created_pending_not_rejected()
    {
        _biometric.NextResult = new BiometricSuitabilityResult(false, ["no_face_detected: position your face in the frame and recapture."]);

        var response = await _client.SendAsync(SubmitRequest(CompliantPrintContent()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("print_quality").GetProperty("passed").GetBoolean());
        Assert.False(body.GetProperty("biometric_suitability").GetProperty("passed").GetBoolean());
        Assert.NotEmpty(body.GetProperty("biometric_suitability").GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public async Task AC02_a_real_http_submission_with_a_failing_print_result_surfaces_the_reason_text()
    {
        var response = await _client.SendAsync(SubmitRequest(SyntheticImages.Png(10, 10)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var reasons = body.GetProperty("print_quality").GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains(reasons, r => r!.Contains("below the required minimum"));
    }

    [Fact]
    public async Task AC03_a_real_http_approve_promotes_and_returns_the_master_version()
    {
        var submitResponse = await _client.SendAsync(SubmitRequest(CompliantPrintContent()), TestContext.Current.CancellationToken);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var sequenceNumber = submitted.GetProperty("sequence_number").GetInt32();
        var version = submitted.GetProperty("version").GetInt32();

        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/photo-quality/captures/{PersonId}/{sequenceNumber}/approve")
        {
            Content = JsonContent.Create(new ApproveCaptureRequest { ExpectedVersion = version }, options: PhotoQualityJsonOptions.Strict),
        };
        approveRequest.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        approveRequest.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(ActorId));

        var approveResponse = await _client.SendAsync(approveRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await approveResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal(1, approved.GetProperty("promoted_master_version").GetInt32());
    }

    [Fact]
    public async Task AC03_a_real_http_reject_without_a_reason_is_refused_422()
    {
        var submitResponse = await _client.SendAsync(SubmitRequest(CompliantPrintContent()), TestContext.Current.CancellationToken);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var sequenceNumber = submitted.GetProperty("sequence_number").GetInt32();
        var version = submitted.GetProperty("version").GetInt32();

        var rejectRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/photo-quality/captures/{PersonId}/{sequenceNumber}/reject")
        {
            Content = JsonContent.Create(new RejectCaptureRequest { Reason = "", ExpectedVersion = version }, options: PhotoQualityJsonOptions.Strict),
        };
        rejectRequest.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        rejectRequest.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(ActorId));

        var rejectResponse = await _client.SendAsync(rejectRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejectResponse.StatusCode);
    }

    [Fact]
    public async Task a_submission_with_no_actor_id_is_refused_400()
    {
        var response = await _client.SendAsync(SubmitRequest(CompliantPrintContent(), actorId: null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    private static HttpRequestMessage GetRequest(string path) => new(HttpMethod.Get, path)
    {
        Headers =
        {
            { ActorResolver.ActorIdHeader, ActorId },
            { ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(ActorId) },
        },
    };

    [Fact]
    public async Task listing_and_getting_a_submitted_capture_round_trips_through_real_http()
    {
        var submitResponse = await _client.SendAsync(SubmitRequest(CompliantPrintContent()), TestContext.Current.CancellationToken);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var sequenceNumber = submitted.GetProperty("sequence_number").GetInt32();

        var listResponse = await _client.SendAsync(GetRequest($"/api/v1/photo-quality/captures/{PersonId}"), TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Single(list.GetProperty("data").EnumerateArray());

        var getResponse = await _client.SendAsync(GetRequest($"/api/v1/photo-quality/captures/{PersonId}/{sequenceNumber}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task getting_a_nonexistent_capture_returns_404()
    {
        var response = await _client.SendAsync(GetRequest($"/api/v1/photo-quality/captures/{PersonId}/999"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ZB_PHO003_01_a_submission_with_no_provenance_proof_at_all_is_rejected_401()
    {
        var response = await _client.SendAsync(SubmitRequest(CompliantPrintContent(), withProvenance: false), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ZB_PHO003_01_a_forged_provenance_proof_is_rejected_401()
    {
        var request = SubmitRequest(CompliantPrintContent(), withProvenance: false);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("a-totally-wrong-secret"), Encoding.UTF8.GetBytes(ActorId))));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ZB_PHO003_01_a_provenance_verified_actor_lacking_the_required_permission_is_denied_403()
    {
        _permission.NextResult = false;

        var response = await _client.SendAsync(SubmitRequest(CompliantPrintContent()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
        Assert.Equal(1, _permission.CallCount); // the real grant lookup was genuinely consulted, not skipped
    }

    [Fact]
    public async Task ZB_PHO003_01_reads_require_the_same_real_authorization_as_writes_not_an_open_endpoint()
    {
        var submitResponse = await _client.SendAsync(SubmitRequest(CompliantPrintContent()), TestContext.Current.CancellationToken);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var sequenceNumber = submitted.GetProperty("sequence_number").GetInt32();

        _permission.NextResult = false;
        var listResponse = await _client.SendAsync(GetRequest($"/api/v1/photo-quality/captures/{PersonId}"), TestContext.Current.CancellationToken);
        var getResponse = await _client.SendAsync(GetRequest($"/api/v1/photo-quality/captures/{PersonId}/{sequenceNumber}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);
    }
}
