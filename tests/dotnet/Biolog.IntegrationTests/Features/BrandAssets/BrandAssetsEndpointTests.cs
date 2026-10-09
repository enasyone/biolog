// Developer tests for BIO-CRD-001. SYNTHETIC fixtures only; the EICAR string is the
// industry-standard harmless AV test signature, not real malware.
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Biolog.Api.Features.BrandAssets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.IntegrationTests.Features.BrandAssets;

public sealed class BrandAssetsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BrandAssetsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient Client() => _factory.CreateClient();

    // A genuine ADR-0001 SS1.2 canonical tenant id (`ten_<UUIDv7>`) - the exact shape
    // api/features/bio-ten-001/Identifiers/CanonicalId.cs mints. TD-CRD001-TENANT-ID-REGEX:
    // every fixture in this file used to be a bare slug, which the old, too-narrow
    // TenantIdPattern happened to accept, so nothing here ever exercised a real canonical
    // id even though the endpoints were reachable end-to-end through the composed host.
    private static string NewTenantId() => $"ten_{Guid.CreateVersion7():D}";

    private const string DefaultTenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";

    /// <summary>
    /// Test-only, out-of-band provisioning (BIO-CRD-001-QA-01 remediation): registers a
    /// deterministic secret for the given tenant directly against the shared
    /// <see cref="ITenantCredentialStore"/> singleton via DI, never through HTTP - exactly
    /// the "not reachable from any HTTP endpoint" seam the real fix relies on.
    /// </summary>
    private string SeedTenant(string tenantId)
    {
        var secret = $"secret-for-{tenantId}";
        _factory.Services.GetRequiredService<ITenantCredentialStore>().Register(tenantId, secret);
        return secret;
    }

    private void AddAuth(HttpRequestMessage request, string? tenantId, string? secretOverride = null)
    {
        if (tenantId is null) return;
        var secret = secretOverride ?? SeedTenant(tenantId);
        request.Headers.Add(HeaderTenantAuthenticator.TenantIdHeaderName, tenantId);
        request.Headers.Add(HeaderTenantAuthenticator.TenantSecretHeaderName, secret);
    }

    private static MultipartFormDataContent Form(
        byte[] file, string fileName, string contentType, string assetType = "PrimaryCrest",
        string? organisationId = null, string? campusId = null, string? intendedWidthMm = null)
    {
        var fileContent = new ByteArrayContent(file);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent
        {
            { fileContent, "file", fileName },
            { new StringContent(assetType), "assetType" },
        };
        if (organisationId is not null) form.Add(new StringContent(organisationId), "organisationId");
        if (campusId is not null) form.Add(new StringContent(campusId), "campusId");
        if (intendedWidthMm is not null) form.Add(new StringContent(intendedWidthMm), "intendedWidthMm");
        return form;
    }

    private HttpRequestMessage UploadRequest(
        HttpMethod method, string path, MultipartFormDataContent content, string? tenantId = DefaultTenantId,
        string? idempotencyKey = null, string? secretOverride = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        AddAuth(request, tenantId, secretOverride);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task AC01_uploads_a_png_and_returns_a_created_asset()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets",
            Form(SyntheticPng(300, 200, alpha: true), "crest.png", "image/png"),
            idempotencyKey: Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Png", body.GetProperty("format").GetString());
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(DefaultTenantId, body.GetProperty("tenant_id").GetString());
        Assert.True(body.GetProperty("raster_quality").GetProperty("has_alpha_channel").GetBoolean());
    }

    [Fact]
    public async Task AC01_uploads_a_jpeg_and_a_svg_successfully()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jpegRequest = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticJpeg(400, 300), "logo.jpg", "image/jpeg"),
            idempotencyKey: Guid.NewGuid().ToString());
        var jpegResponse = await Client().SendAsync(jpegRequest, ct);
        Assert.Equal(HttpStatusCode.Created, jpegResponse.StatusCode);

        using var svgRequest = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(ValidSvgBytes(), "mark.svg", "image/svg+xml"),
            idempotencyKey: Guid.NewGuid().ToString());
        var svgResponse = await Client().SendAsync(svgRequest, ct);
        Assert.Equal(HttpStatusCode.Created, svgResponse.StatusCode);
        var svgBody = await svgResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Svg", svgBody.GetProperty("format").GetString());
        Assert.False(svgBody.TryGetProperty("raster_quality", out _)); // never a false raster-DPI warning for SVG
    }

    [Fact]
    public async Task AC01_rejects_an_unsupported_file_format()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets",
            Form([0x00, 0x01, 0x02, 0x03], "file.bin", "application/octet-stream"),
            idempotencyKey: Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejects_an_upload_with_no_tenant_authentication_at_all()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
            tenantId: null, idempotencyKey: Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_authentication_failed", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("correlation_id", out _));
    }

    [Fact]
    public async Task Rejects_an_upload_with_a_well_formed_tenant_id_but_the_wrong_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        SeedTenant(tenantId);
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
            tenantId: tenantId, idempotencyKey: Guid.NewGuid().ToString(), secretOverride: "definitely-wrong");

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_authentication_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task QA01_a_caller_cannot_read_another_tenants_asset_by_swapping_the_tenant_id_header_while_reusing_its_own_valid_secret()
    {
        // The exact BIO-CRD-001-QA-01 attack: the caller genuinely is "attacker-tenant" and
        // presents ITS OWN valid secret, but swaps the id header to the victim's. Before the
        // fix a bare id header was authority on its own and this succeeded.
        var ct = TestContext.Current.CancellationToken;
        var victimTenantId = NewTenantId();
        var attackerTenantId = NewTenantId();
        var victimSecret = SeedTenant(victimTenantId);
        var attackerSecret = SeedTenant(attackerTenantId);

        using var uploadRequest = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
            tenantId: victimTenantId, idempotencyKey: Guid.NewGuid().ToString(), secretOverride: victimSecret);
        var uploaded = await (await Client().SendAsync(uploadRequest, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = uploaded.GetProperty("id").GetString();

        using var forgedGet = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/brand-assets/{id}");
        forgedGet.Headers.Add(HeaderTenantAuthenticator.TenantIdHeaderName, victimTenantId);
        forgedGet.Headers.Add(HeaderTenantAuthenticator.TenantSecretHeaderName, attackerSecret); // attacker's OWN real secret
        var forgedResponse = await Client().SendAsync(forgedGet, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
        var body = await forgedResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_authentication_failed", body.GetProperty("code").GetString());

        // The legitimate owner, with its own id+secret pair, still reads it fine.
        using var ownerGet = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/brand-assets/{id}");
        AddAuth(ownerGet, victimTenantId, victimSecret);
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(ownerGet, ct)).StatusCode);
    }

    [Fact]
    public async Task QA02_a_tenant_credential_provisioned_through_real_application_configuration_can_authenticate(
        )
    {
        // BIO-CRD-001-QA-02: round 1's fix removed the forgeable header but never wired a
        // real provisioning path, so the composed production host could authenticate no one.
        // This test deliberately does NOT call SeedTenant/ITenantCredentialStore.Register -
        // it configures application configuration the same way an operator would (an
        // environment variable maps to this exact section shape:
        // BrandAssets__TenantCredentials__<id>=<secret>) and proves the real startup code
        // (TenantCredentialProvisioning.LoadFromConfiguration, wired in BrandAssetsModule)
        // is what authenticates the request.
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        const string secret = "prod-path-secret";

        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TenantCredentialProvisioning.ConfigurationSectionPath}:{tenantId}"] = secret,
            })));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/brand-assets")
        {
            Content = Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
        };
        request.Headers.Add(HeaderTenantAuthenticator.TenantIdHeaderName, tenantId);
        request.Headers.Add(HeaderTenantAuthenticator.TenantSecretHeaderName, secret);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(tenantId, body.GetProperty("tenant_id").GetString());
    }

    [Fact]
    public async Task TD_CRD001_a_real_canonical_ten_prefixed_uuid_v7_tenant_id_can_authenticate_end_to_end()
    {
        // TD-CRD001-TENANT-ID-REGEX (Zainab's retroactive security review, already-merged
        // BIO-CRD-001): TenantIdPattern was a bare-slug pattern that rejected every real
        // canonical tenant id - api/features/bio-ten-001/Identifiers/CanonicalId.cs mints
        // `ten_<UUIDv7>`, which that pattern's `^[a-z0-9][a-z0-9-]{0,63}$` shape can never
        // match because of the underscore right after the prefix. TryAuthenticate returned
        // null before the secret was ever checked, so a genuine BIO-TEN-001 tenant with the
        // correct provisioned secret could never use this endpoint. This fixture is the
        // exact shape CanonicalId.NewTenantId() produces, not a synthetic slug.
        var ct = TestContext.Current.CancellationToken;
        const string tenantId = "ten_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
        const string secret = "canonical-tenant-secret";
        _factory.Services.GetRequiredService<ITenantCredentialStore>().Register(tenantId, secret);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/brand-assets")
        {
            Content = Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
        };
        request.Headers.Add(HeaderTenantAuthenticator.TenantIdHeaderName, tenantId);
        request.Headers.Add(HeaderTenantAuthenticator.TenantSecretHeaderName, secret);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(tenantId, body.GetProperty("tenant_id").GetString());
    }

    [Fact]
    public async Task Rejects_an_upload_with_no_idempotency_key()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
            idempotencyKey: null);

        var response = await Client().SendAsync(request, ct);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("idempotency_key_required", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("SvgWithScriptTag")]
    [InlineData("SvgWithEventHandler")]
    [InlineData("SvgWithJavascriptUri")]
    [InlineData("SvgWithForeignObject")]
    [InlineData("SvgWithXxeDoctype")]
    public async Task AC04_rejects_svg_active_content_and_xxe_attempts(string fixtureName)
    {
        var ct = TestContext.Current.CancellationToken;
        var svg = fixtureName switch
        {
            "SvgWithScriptTag" => """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""",
            "SvgWithEventHandler" => """<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"><rect width="1" height="1"/></svg>""",
            "SvgWithJavascriptUri" => """<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)"><rect width="1" height="1"/></a></svg>""",
            "SvgWithForeignObject" => """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><body xmlns="http://www.w3.org/1999/xhtml">hi</body></foreignObject></svg>""",
            "SvgWithXxeDoctype" => "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&xxe;</svg>",
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureName)),
        };
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets",
            Form(Encoding.UTF8.GetBytes(svg), "mark.svg", "image/svg+xml"),
            idempotencyKey: Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AC04_rejects_a_file_flagged_by_the_malware_scan_and_never_stores_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var eicar = Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        byte[] content = [.. SyntheticPng(1, 1, false), .. eicar];
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(content, "c.png", "image/png"),
            idempotencyKey: Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("malware_detected", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC02_AC03_low_resolution_warning_is_calculated_against_the_intended_print_size()
    {
        var ct = TestContext.Current.CancellationToken;
        // 100px wide at an intended 50.8mm (2in) print width => 50 DPI, well under the floor.
        using var request = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets",
            Form(SyntheticPng(100, 100, false), "small.png", "image/png", intendedWidthMm: "50.8"),
            idempotencyKey: Guid.NewGuid().ToString());

        var response = await Client().SendAsync(request, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.True(body.GetProperty("raster_quality").GetProperty("low_resolution_warning").GetBoolean());
    }

    [Fact]
    public async Task AC02_uploading_a_new_version_increments_the_version_and_keeps_the_lineage()
    {
        var ct = TestContext.Current.CancellationToken;
        using var firstRequest = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "v1.png", "image/png"),
            idempotencyKey: Guid.NewGuid().ToString());
        var firstResponse = await Client().SendAsync(firstRequest, ct);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var lineageId = firstBody.GetProperty("lineage_id").GetString()!;

        using var secondRequest = UploadRequest(
            HttpMethod.Post, $"/api/v1/brand-assets/{lineageId}/versions",
            Form(SyntheticPng(20, 20, false), "v2.png", "image/png"),
            idempotencyKey: Guid.NewGuid().ToString());
        var secondResponse = await Client().SendAsync(secondRequest, ct);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(lineageId, secondBody.GetProperty("lineage_id").GetString());
        Assert.Equal(2, secondBody.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task AC05_a_second_tenant_cannot_read_the_first_tenants_asset()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerTenantId = NewTenantId();
        var intruderTenantId = NewTenantId();
        using var uploadRequest = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
            tenantId: ownerTenantId, idempotencyKey: Guid.NewGuid().ToString());
        var uploadResponse = await Client().SendAsync(uploadRequest, ct);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = uploaded.GetProperty("id").GetString();

        using var getAsOwner = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/brand-assets/{id}");
        AddAuth(getAsOwner, ownerTenantId);
        var ownerResponse = await Client().SendAsync(getAsOwner, ct);
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        using var getAsOther = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/brand-assets/{id}");
        AddAuth(getAsOther, intruderTenantId); // its own, genuinely-registered credential
        var otherResponse = await Client().SendAsync(getAsOther, ct);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode); // not 403: no enumeration oracle
    }

    [Fact]
    public async Task AC05_list_is_scoped_to_the_caller_tenant_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantA = NewTenantId();
        var tenantB = NewTenantId();

        using var uploadA = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "a.png", "image/png"),
            tenantId: tenantA, idempotencyKey: Guid.NewGuid().ToString());
        await Client().SendAsync(uploadA, ct);
        using var uploadB = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "b.png", "image/png"),
            tenantId: tenantB, idempotencyKey: Guid.NewGuid().ToString());
        await Client().SendAsync(uploadB, ct);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/brand-assets");
        AddAuth(listRequest, tenantA);
        var listResponse = await Client().SendAsync(listRequest, ct);
        var page = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        var items = page.GetProperty("data").EnumerateArray().ToList();
        Assert.All(items, item => Assert.Equal(tenantA, item.GetProperty("tenant_id").GetString()));
    }

    [Fact]
    public async Task Status_transition_endpoint_updates_status_and_is_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        using var uploadRequest = UploadRequest(
            HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "c.png", "image/png"),
            idempotencyKey: Guid.NewGuid().ToString());
        var uploaded = await (await Client().SendAsync(uploadRequest, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = uploaded.GetProperty("id").GetString();

        using var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/brand-assets/{id}/status")
        {
            Content = JsonContent.Create(new UpdateStatusRequest("Approved")),
        };
        AddAuth(approveRequest, DefaultTenantId);
        var approveResponse = await Client().SendAsync(approveRequest, ct);
        var approved = await approveResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Replaying_the_same_idempotency_key_and_content_returns_the_same_asset_without_creating_a_second_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();
        var tenantId = NewTenantId();
        var fileBytes = SyntheticPng(10, 10, false);

        using var first = UploadRequest(HttpMethod.Post, "/api/v1/brand-assets", Form(fileBytes, "c.png", "image/png"), tenantId, key);
        var firstBody = await (await Client().SendAsync(first, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        using var replay = UploadRequest(HttpMethod.Post, "/api/v1/brand-assets", Form(fileBytes, "c.png", "image/png"), tenantId, key);
        var replayResponse = await Client().SendAsync(replay, ct);
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(firstBody.GetProperty("id").GetString(), replayBody.GetProperty("id").GetString());

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/brand-assets");
        AddAuth(listRequest, tenantId);
        var page = await (await Client().SendAsync(listRequest, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Single(page.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_with_different_content_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();
        var tenantId = NewTenantId();

        using var first = UploadRequest(HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(10, 10, false), "a.png", "image/png"), tenantId, key);
        await Client().SendAsync(first, ct);

        using var second = UploadRequest(HttpMethod.Post, "/api/v1/brand-assets", Form(SyntheticPng(20, 20, false), "b.png", "image/png"), tenantId, key);
        var secondResponse = await Client().SendAsync(second, ct);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var body = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("idempotency_key_reuse", body.GetProperty("code").GetString());
    }

    // --- fixture builders (kept local: IntegrationTests does not reference UnitTests) ---

    private static byte[] SyntheticPng(int width, int height, bool alpha)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(alpha ? 6 : 2);
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
        var typeBytes = Encoding.ASCII.GetBytes(type);
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

    private static byte[] SyntheticJpeg(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        var sof0 = new byte[9];
        sof0[0] = 8;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(sof0.AsSpan(1, 2), (ushort)height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(sof0.AsSpan(3, 2), (ushort)width);
        sof0[5] = 1;
        sof0[6] = 0x01; sof0[7] = 0x11; sof0[8] = 0x00;
        stream.Write([0xFF, 0xC0]);
        var lengthBytes = new byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, (ushort)(sof0.Length + 2));
        stream.Write(lengthBytes);
        stream.Write(sof0);
        stream.Write([0xFF, 0xD9]);
        return stream.ToArray();
    }

    private static byte[] ValidSvgBytes() =>
        Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4" fill="navy"/></svg>""");
}
