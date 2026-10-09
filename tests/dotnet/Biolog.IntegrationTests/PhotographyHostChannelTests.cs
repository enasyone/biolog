using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-PHO-001's
/// tenant boundary and BIO-IDN-001 existence-gateway wiring are enforced by the real, shipped
/// host channel and person registry - not merely an injected test double. Mirrors
/// <c>PersonProfileHostChannelTests.cs</c> exactly: registers and verifies a real domain
/// through BIO-TEN-002's own endpoints, registers a real person through BIO-IDN-001's own
/// endpoint, then exercises BIO-PHO-001's endpoints through that verified hostname. SYNTHETIC
/// fixtures only.
/// </summary>
public sealed class PhotographyHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    public PhotographyHostChannelTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage UploadRequest(string url, string host, byte[] content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "operator_ada");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        request.Content = new MultipartFormDataContent { { fileContent, "file", "photo.png" } };
        return request;
    }

    private static byte[] SyntheticPng(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(stream, "IHDR", ihdr);
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

    [Fact]
    public async Task AC01_appending_a_master_through_a_verified_hostname_resolves_the_real_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(UploadRequest($"/api/v1/people/{personId}/photo-masters", hostname, SyntheticPng(300, 200)), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantA, body.GetProperty("tenant_id").GetString());
        Assert.Equal(1, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task AC04_assigning_a_real_master_to_a_real_person_through_the_real_host_records_the_assignment()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-assign-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        var uploadResponse = await client.SendAsync(UploadRequest($"/api/v1/people/{personId}/photo-masters", hostname, SyntheticPng(10, 10)), ct);
        var master = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        var assignResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Put, $"/api/v1/people/{personId}/photo-assignment", hostname, new { master_version = master.GetProperty("version").GetInt32() }), ct);

        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        var body = await assignResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantA, body.GetProperty("tenant_id").GetString());
        Assert.Equal(1, body.GetProperty("current_master_version").GetInt32());
    }

    [Fact]
    public async Task BIOPHO001_appending_a_master_for_an_unknown_canonical_person_id_is_rejected()
    {
        // Same shape as BIO-IDN-002-QA-01's required-closure test: a real, verified tenant
        // host, but a person id that was never registered anywhere - through the ACTUAL
        // packaged host's real IPersonExistenceGateway adapter, not an injected fake.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "photo-unknown-person-e2e.example.edu.ng", TenantA, ct);
        const string neverRegisteredPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

        var response = await client.SendAsync(UploadRequest($"/api/v1/people/{neverRegisteredPersonId}/photo-masters", hostname, SyntheticPng(10, 10)), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOPHO001_a_tenant_B_caller_cannot_append_a_master_for_a_real_tenant_A_person()
    {
        // Canonical identifiers carry no authority (ADR-0001 §1.5) - a REAL person that
        // genuinely exists in tenant A must still be rejected, identically to an unknown one,
        // when referenced through tenant B's host.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "photo-real-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "photo-real-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var tenantAPersonId = await RegisterPersonAsync(client, hostA, ct);

        var response = await client.SendAsync(UploadRequest($"/api/v1/people/{tenantAPersonId}/photo-masters", hostB, SyntheticPng(10, 10)), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString()); // identical to the wholly-unknown case
    }

    [Fact]
    public async Task Appending_a_master_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(UploadRequest(
            "/api/v1/people/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d/photo-masters",
            "nobody-registered-this-photo-host.example.edu.ng", SyntheticPng(10, 10)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
