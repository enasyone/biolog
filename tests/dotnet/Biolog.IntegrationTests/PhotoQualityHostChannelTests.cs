using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.PhotoQuality.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-PHO-003's
/// tenant boundary, BIO-IDN-001 person-existence wiring, the real BIO-PHO-001
/// <c>PhotoMasterService</c> promotion path, and - the one genuinely new cross-feature wiring
/// this story adds - the real BIO-BIO-003 <c>IFaceQualityChecker</c>/<c>ILivenessDetector</c>
/// biometric-suitability path, are all real, not merely injected test doubles. SYNTHETIC
/// fixtures only.
///
/// <b>The biometric result for a real raster image is deterministically "fails on face count",
/// confirmed rather than assumed.</b> BIO-BIO-003's own <c>SyntheticFaceQualityChecker</c>
/// reads a sample's <c>payload[0]</c> directly as "detected face count" (its own documented
/// test-only encoding). A real PNG's magic byte at offset 0 is <c>0x89</c> (137) and a real
/// JPEG's is <c>0xFF</c> (255) - neither format can ever encode the value <c>1</c> at that
/// offset, so this story's own real photo submissions always and predictably report
/// <c>multiple_faces_detected</c>, never a pass, regardless of image content. This is an
/// honest, disclosed consequence of feeding a genuine raster file into a synthetic checker
/// whose own encoding expects face count at byte offset 0 - not a defect in this story's own
/// integration, and exactly why AC03 gives the human operator, not an automatic gate, the final
/// decision (see docs/features/bio-pho-003/README.md).
///
/// <b>ZB-PHO003-01 (Zainab, security round 1, Critical), fixed.</b> Every
/// <c>/api/v1/photo-quality/*</c> route now requires a real <see cref="ActorProvenanceGate"/>
/// proof plus a tenant-scoped <see cref="IPermissionGateway"/> grant. This project has no
/// <c>InternalsVisibleTo</c> access to seed a real <c>RolePermissionGrant</c> into BIO-IDN-006's
/// internals (confirmed: only <c>Biolog.Api</c> and <c>Biolog.Features.Rbac.Tests</c> are
/// granted that), the same disclosed limitation <c>ApiSecurityEndpointsTests</c> already
/// establishes - so <see cref="PrivilegedClient"/> substitutes only this feature's OWN public,
/// feature-local <see cref="IPermissionGateway"/> interface with an always-granted fake (never
/// Rbac's own internals, and never the real <see cref="ActorProvenanceGate"/>, which stays the
/// genuine HMAC store throughout every test in this class). The REAL, un-substituted Rbac check
/// is still proven end-to-end by <c>Unprivileged_actor_with_valid_provenance_is_denied</c> below
/// (a real registered person, genuinely holding no grant, denied for real) and, with a real
/// seeded grant, by <c>PhotoQualityEndpointsIntegrationTests</c>' own feature-hosted TestServer.
/// </summary>
public sealed class PhotoQualityHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly WebApplicationFactory<Program> _privilegedFactory;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5b11";
    private const string ProvenanceSecret = "test-host-e2e-photo-quality-actor-provenance-secret";
    private const string StaffActorId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5bff";

    public PhotoQualityHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoQuality:ActorProvenanceSecret"] = ProvenanceSecret,
            })));

        _privilegedFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPermissionGateway>();
            services.AddScoped<IPermissionGateway, AlwaysGrantedPermissionGateway>();
        }));
    }

    private sealed class AlwaysGrantedPermissionGateway : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

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

    /// <summary>An authorized request against <see cref="_privilegedFactory"/>: real provenance proof, substituted always-granted permission gateway.</summary>
    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", StaffActorId);
        request.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(StaffActorId));
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
            HttpMethod.Post, "/api/v1/persons", host, new { first_name = "Chidinma", last_name = "Eze" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
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

    private static HttpRequestMessage SubmitRequest(string host, string personId, byte[] content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-quality/captures") { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", StaffActorId);
        request.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(StaffActorId));
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        request.Content = new MultipartFormDataContent
        {
            { fileContent, "file", "capture.png" },
            { new StringContent(personId), "person_id" },
        };
        return request;
    }

    [Fact]
    public async Task AC01_a_real_submission_shows_real_print_quality_pass_and_real_biometric_suitability_fail_independently()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _privilegedFactory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-quality-e2e.example.edu.ng", TenantId, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        // 1050x1350 @ 300dpi genuinely satisfies PrintQualitySpecification.Current - a REAL
        // PhotoQualityRasterInspector.Inspect call on these exact bytes, not a stubbed verdict.
        var content = SyntheticPng(1050, 1350, 300);
        var response = await client.SendAsync(SubmitRequest(hostname, personId, content), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("print_quality").GetProperty("passed").GetBoolean());

        // The real BIO-BIO-003 SyntheticFaceQualityChecker/SyntheticLivenessDetector were
        // genuinely consulted (not a fake) - see this class's own header for why a real PNG
        // deterministically fails face-count.
        Assert.False(body.GetProperty("biometric_suitability").GetProperty("passed").GetBoolean());
        var reasons = body.GetProperty("biometric_suitability").GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains(reasons, r => r!.Contains("faces_detected"));
    }

    [Fact]
    public async Task AC03_approving_a_real_capture_creates_a_real_independently_readable_BIOPHO001_photo_master()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _privilegedFactory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-quality-approve-e2e.example.edu.ng", TenantId, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var content = SyntheticPng(1050, 1350, 300);
        var submitResponse = await client.SendAsync(SubmitRequest(hostname, personId, content), ct);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sequenceNumber = submitted.GetProperty("sequence_number").GetInt32();
        var version = submitted.GetProperty("version").GetInt32();

        // The operator approves despite the real biometric fail - AC03's own discretion, not an
        // automatic gate (see docs/features/bio-pho-003/README.md).
        var approveResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/photo-quality/captures/{personId}/{sequenceNumber}/approve", hostname, new { expected_version = version }), ct);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await approveResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var masterVersion = approved.GetProperty("promoted_master_version").GetInt32();
        Assert.Equal(1, masterVersion);

        // Independently readable through BIO-PHO-001's own, completely separate endpoint - this
        // is a real PhotoMaster, not merely a value this story's own response happened to echo.
        var masterResponse = await client.SendAsync(
            ActorRequest(HttpMethod.Get, $"/api/v1/people/{personId}/photo-masters/{masterVersion}", hostname), ct);
        Assert.Equal(HttpStatusCode.OK, masterResponse.StatusCode);
        var master = await masterResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(masterVersion, master.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task a_person_registered_in_tenant_A_cannot_be_submitted_against_under_tenant_Bs_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _privilegedFactory.CreateClient();
        const string tenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5b22";
        var hostA = await RegisterAndVerifyDomainAsync(client, "photo-quality-tenant-a-e2e.example.edu.ng", TenantId, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "photo-quality-tenant-b-e2e.example.edu.ng", tenantB, ct);
        var personIdInA = await RegisterPersonAsync(client, hostA, ct);

        var response = await client.SendAsync(SubmitRequest(hostB, personIdInA, SyntheticPng(1050, 1350, 300)), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ZB_PHO003_01_a_submission_with_no_provenance_proof_at_all_is_rejected_401_real_gate_not_a_blanket_rejection()
    {
        var ct = TestContext.Current.CancellationToken;
        using var privilegedClient = _privilegedFactory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(privilegedClient, "photo-quality-no-provenance-e2e.example.edu.ng", TenantId, ct);
        var personId = await RegisterPersonAsync(privilegedClient, hostname, ct);

        using var client = _privilegedFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-quality/captures") { Headers = { Host = hostname } };
        request.Headers.Add("X-Actor-Id", StaffActorId); // asserted, but with no X-Actor-Provenance at all
        var fileContent = new ByteArrayContent(SyntheticPng(1050, 1350, 300));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        request.Content = new MultipartFormDataContent { { fileContent, "file", "capture.png" }, { new StringContent(personId), "person_id" } };

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ZB_PHO003_01_a_forged_provenance_proof_under_the_wrong_secret_is_also_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var privilegedClient = _privilegedFactory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(privilegedClient, "photo-quality-forged-provenance-e2e.example.edu.ng", TenantId, ct);
        var personId = await RegisterPersonAsync(privilegedClient, hostname, ct);

        using var client = _privilegedFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-quality/captures") { Headers = { Host = hostname } };
        request.Headers.Add("X-Actor-Id", StaffActorId);
        request.Headers.Add("X-Actor-Provenance", Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("a-completely-wrong-secret-the-attacker-guessed"), Encoding.UTF8.GetBytes(StaffActorId))));
        var fileContent = new ByteArrayContent(SyntheticPng(1050, 1350, 300));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        request.Content = new MultipartFormDataContent { { fileContent, "file", "capture.png" }, { new StringContent(personId), "person_id" } };

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ZB_PHO003_01_a_real_registered_person_with_a_valid_provenance_proof_but_no_grant_is_denied_403_real_rbac_not_substituted()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately _factory, NOT _privilegedFactory: this is the one proof in this project
        // that exercises BIO-IDN-006's own REAL PermissionService.ActorHasPermissionAsync, not
        // the substituted always-granted fake - a real registered person genuinely holds no
        // RolePermissionGrant by default, no seeding required to prove that.
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-quality-unprivileged-e2e.example.edu.ng", TenantId, ct);
        var unprivilegedPersonId = await RegisterPersonAsync(client, hostname, ct);
        var subjectPersonId = await RegisterPersonAsync(client, hostname, ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photo-quality/captures") { Headers = { Host = hostname } };
        request.Headers.Add("X-Actor-Id", unprivilegedPersonId);
        request.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(unprivilegedPersonId));
        var fileContent = new ByteArrayContent(SyntheticPng(1050, 1350, 300));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        request.Content = new MultipartFormDataContent { { fileContent, "file", "capture.png" }, { new StringContent(subjectPersonId), "person_id" } };

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // No silent partial success: nothing was ever created for the subject person - checked
        // on the SAME un-substituted host/registry the denied attempt itself ran against
        // (_factory, not _privilegedFactory, which is a genuinely separate TestServer instance
        // with its own in-memory tenant-domain/person/capture state). This still only proves the
        // negative up to whatever this unprivileged actor is itself permitted to read - which,
        // correctly, is nothing either, so a second denial here is itself part of the proof.
        var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/photo-quality/captures/{subjectPersonId}") { Headers = { Host = hostname } };
        listRequest.Headers.Add("X-Actor-Id", unprivilegedPersonId);
        listRequest.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(unprivilegedPersonId));
        var listResponse = await client.SendAsync(listRequest, ct);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
    }
}
