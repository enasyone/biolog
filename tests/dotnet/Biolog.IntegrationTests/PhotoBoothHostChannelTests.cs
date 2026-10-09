using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Features.PhotoBooth.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-PHO-002's
/// tenant boundary, BIO-IDN-001 person-existence wiring, BIO-DEV-001 device-existence wiring
/// and - the one genuine cross-story runtime dependency in this epic - the real BIO-PHO-001
/// <c>PhotoMasterService</c> promotion path, are all real, not merely injected test doubles.
/// Registers and verifies a real domain through BIO-TEN-002's own endpoints, registers a real
/// person through BIO-IDN-001's own endpoint, registers and activates a real device through
/// BIO-DEV-001's own endpoints, submits and approves a capture through BIO-PHO-002's own
/// endpoints, then confirms the resulting PhotoMaster is real and independently readable through
/// BIO-PHO-001's own endpoint. SYNTHETIC fixtures only.
///
/// <b>Device credential seeding.</b> BIO-DEV-001 mints its own <c>dev_</c> id server-side (never
/// caller-supplied), so a device's id cannot be known before registration - which makes seeding
/// its secret through <see cref="DeviceCredentialProvisioning.LoadFromConfiguration"/> (fixed at
/// host-build time, before any id exists) impossible within one HTTP-only flow. This test seeds
/// the credential directly against the shared <see cref="IDeviceCredentialStore"/> singleton via
/// DI once the real id is known, never through HTTP - the same "not reachable from any HTTP
/// endpoint" seam BIO-CRD-001's own host-channel tests use. The real configuration-loading path
/// itself is proven separately and directly in
/// <c>tests/features/bio-pho-002/DeviceCredentialProvisioningTests.cs</c> - this test proves
/// device EXISTENCE/ACTIVE-status wiring and promotion are real, which do not depend on which
/// provisioning path filled the credential store.
/// </summary>
public sealed class PhotoBoothHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string DeviceSecret = "host-channel-device-secret";

    public PhotoBoothHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static HttpRequestMessage DomainAdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Actor-Id", "admin_alice");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "operator_ada");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<string> RegisterAndVerifyDomainAsync(HttpClient client, string hostname, string tenantId, CancellationToken ct)
    {
        var registerResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname, tenant_id = tenantId }), ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = registered.GetProperty("version").GetInt32();

        var verifyResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Put, $"/api/v1/control-plane/tenant-domains/{hostname}/status", new { version, status = "VERIFIED" }), ct);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        return hostname;
    }

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons", host, new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    private async Task<string> RegisterAndActivateDeviceAsync(HttpClient client, string host, CancellationToken ct)
    {
        var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices") { Headers = { Host = host } };
        registerRequest.Content = JsonContent.Create(new DeviceRegistrationRequest(
            "SN-BOOTH-1", "Acme", "PhotoBoothX", "1.0.0", "sdk-1", "Main Campus",
            new NetworkMetadata("10.0.0.9", null, "booth-01"), CredentialReference: "kv://synthetic/booth-01"));
        var registerResponse = await client.SendAsync(registerRequest, ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var device = await registerResponse.Content.ReadFromJsonAsync<Device>(ct);

        var activateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/devices/{device!.Id}/status") { Headers = { Host = host } };
        activateRequest.Content = JsonContent.Create(new DeviceStatusChangeRequest(DeviceStatus.Active, device.Version, "user", "admin_alice", CorrelationId: null));
        var activateResponse = await client.SendAsync(activateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        _factory.Services.GetRequiredService<InMemoryDeviceCredentialStore>().Register(device.Id, DeviceSecret);
        return device.Id;
    }

    private static byte[] SyntheticPng(int width, int height, double? dpi = null)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(stream, "IHDR", ihdr);
        if (dpi is { } value)
        {
            var pixelsPerMetre = (uint)Math.Round(value / 0.0254);
            var phys = new byte[9];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(0, 4), pixelsPerMetre);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4, 4), pixelsPerMetre);
            phys[8] = 1;
            WriteChunk(stream, "pHYs", phys);
        }
        WriteChunk(stream, "IDAT", []);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in typeBytes.Concat(data))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        var crcBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFF);
        stream.Write(crcBytes);
    }

    private static HttpRequestMessage SubmitRequest(string host, string personId, string deviceId, byte[] content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-booth/captures") { Headers = { Host = host } };
        request.Headers.Add("X-Biolog-Device-Id", deviceId);
        request.Headers.Add("X-Biolog-Device-Secret", DeviceSecret);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        request.Content = new MultipartFormDataContent
        {
            { fileContent, "file", "capture.png" },
            { new StringContent(personId), "person_id" },
            { new StringContent("true"), "face_height_compliant" },
            { new StringContent("true"), "background_compliant" },
        };
        return request;
    }

    [Fact]
    public async Task AC02_a_capture_submitted_by_a_real_registered_active_device_for_a_real_person_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-booth-e2e.example.edu.ng", TenantId, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        var deviceId = await RegisterAndActivateDeviceAsync(client, hostname, ct);

        var content = SyntheticPng(CaptureSpecification.Current.FrameWidthPx, CaptureSpecification.Current.FrameHeightPx, CaptureSpecification.Current.MinimumDpi);
        var response = await client.SendAsync(SubmitRequest(hostname, personId, deviceId, content), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(deviceId, body.GetProperty("device_id").GetString());
    }

    [Fact]
    public async Task BIOPHO002_a_device_registered_in_tenant_A_cannot_authenticate_a_submission_under_tenant_Bs_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        const string tenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
        var hostA = await RegisterAndVerifyDomainAsync(client, "photo-booth-tenant-a-e2e.example.edu.ng", TenantId, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "photo-booth-tenant-b-e2e.example.edu.ng", tenantB, ct);
        var personIdA = await RegisterPersonAsync(client, hostA, ct);
        var deviceIdA = await RegisterAndActivateDeviceAsync(client, hostA, ct);

        var content = SyntheticPng(CaptureSpecification.Current.FrameWidthPx, CaptureSpecification.Current.FrameHeightPx, CaptureSpecification.Current.MinimumDpi);
        var response = await client.SendAsync(SubmitRequest(hostB, personIdA, deviceIdA, content), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_authentication_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC04_approving_a_real_capture_creates_a_real_independently_readable_BIOPHO001_photo_master()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-booth-promotion-e2e.example.edu.ng", TenantId, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        var deviceId = await RegisterAndActivateDeviceAsync(client, hostname, ct);

        var content = SyntheticPng(CaptureSpecification.Current.FrameWidthPx, CaptureSpecification.Current.FrameHeightPx, CaptureSpecification.Current.MinimumDpi);
        var submitResponse = await client.SendAsync(SubmitRequest(hostname, personId, deviceId, content), ct);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sequence = submitBody.GetProperty("sequence_number").GetInt32();

        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/photo-booth/captures/{personId}/{sequence}/approve") { Headers = { Host = hostname } };
        approveRequest.Headers.Add("X-Actor-Id", "reviewer_1");
        approveRequest.Content = JsonContent.Create(new { expected_version = 1 });
        var approveResponse = await client.SendAsync(approveRequest, ct);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approveBody = await approveResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var masterVersion = approveBody.GetProperty("promoted_master_version").GetInt32();

        // Independently confirm the promoted master is real by reading it back through
        // BIO-PHO-001's OWN endpoint, not merely trusting this story's own response claim.
        var masterRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/people/{personId}/photo-masters/{masterVersion}") { Headers = { Host = hostname } };
        var masterResponse = await client.SendAsync(masterRequest, ct);
        Assert.Equal(HttpStatusCode.OK, masterResponse.StatusCode);
        var masterBody = await masterResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(personId, masterBody.GetProperty("person_id").GetString());
        Assert.Equal(masterVersion, masterBody.GetProperty("version").GetInt32());
    }
}
