using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Features.DeviceCertification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// Developer tests for BIO-DEV-002, exercised through the real composed host (module
/// wiring, routing, JSON serialization). The platform-wide matrix
/// (<c>/api/v1/device-certifications</c>) is control-plane - no tenant resolution
/// involved, mirroring how <c>BioTen001Module</c>'s own control-plane endpoints need no
/// Host header. The tenant-override group (<c>/api/v1/tenant/device-certifications</c>,
/// BIO-DEV-002-AC03) is proven through the REAL shipped BIO-TEN-002 host channel - not an
/// injected test double - the same "upgraded evidence" shape
/// <c>PersonHostChannelTests.cs</c> already established for ADR-0002 Decision 6: BIO-TEN-002
/// shipped before this story started, so there is no interim accommodation to fall back to.
/// </summary>
public sealed class DeviceCertificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5aaa";

    public DeviceCertificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static DeviceCertificationRegistrationRequest SampleRequest(string model = "ReaderX") => new(
        Vendor: "Acme",
        Model: model,
        Firmware: "1.2.3",
        Sdk: "sdk-2.0",
        Status: CertificationStatus.Certified,
        TestEvidenceReference: "https://evidence.internal/reports/acme-readerx-1.2.3",
        ActorType: "user",
        ActorId: "usr_ops",
        CorrelationId: null);

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

    private static HttpRequestMessage HostRequest(HttpMethod method, string url, string host, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    // --- Control plane: the platform-wide matrix needs no tenant context ---

    [Fact]
    public async Task AC01_registering_the_global_matrix_entry_records_the_sdk_firmware_and_test_evidence()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/device-certifications", SampleRequest("ReaderAC01"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DeviceCertification>(TestContext.Current.CancellationToken);
        Assert.Equal("1.2.3", created!.Firmware);
        Assert.Equal("sdk-2.0", created.Sdk);
        Assert.Equal("https://evidence.internal/reports/acme-readerx-1.2.3", created.TestEvidenceReference);
        Assert.Equal(1, created.Version);
        Assert.Null(created.TenantId);
    }

    [Fact]
    public async Task device_certification_endpoints_are_routed_under_the_versioned_api_path()
    {
        // ADR-0004 §1.1: every public HTTP endpoint is versioned in its path. Unlike
        // DeviceRegistryTests' equivalent check, an unknown lookup here legitimately
        // returns a real 404 (GetEffectiveAsync found nothing) - indistinguishable over
        // HTTP from an unmatched route also 404ing, so "assert not 404" would prove
        // nothing for this specific endpoint. Registering first and asserting a genuine
        // 200 instead proves the exact path was both routed AND executed.
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/v1/device-certifications", SampleRequest("ReaderRouting"), TestContext.Current.CancellationToken);

        var response = await client.GetAsync(
            "/api/v1/device-certifications/lookup?vendor=Acme&model=ReaderRouting&firmware=1.2.3&sdk=sdk-2.0",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC02_the_global_listing_surfaces_an_unsupported_combination_alongside_a_certified_one()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/device-certifications", SampleRequest("ReaderAC02Certified"), TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync(
            "/api/v1/device-certifications",
            SampleRequest("ReaderAC02Unsupported") with { Status = CertificationStatus.Unsupported },
            TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/v1/device-certifications?status=Unsupported", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<DeviceCertification[]>(TestContext.Current.CancellationToken);
        Assert.Contains(entries!, e => e.Model == "ReaderAC02Unsupported");
        Assert.DoesNotContain(entries!, e => e.Model == "ReaderAC02Certified");
    }

    [Fact]
    public async Task registering_the_same_combination_twice_is_rejected_not_silently_overwritten()
    {
        using var client = _factory.CreateClient();
        var request = SampleRequest("ReaderDuplicate");
        await client.PostAsJsonAsync("/api/v1/device-certifications", request, TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/v1/device-certifications", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("device_certification_already_exists", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AC04_updating_the_global_entrys_status_increments_version_and_is_auditable_through_the_host()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/device-certifications", SampleRequest("ReaderAC04"), TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<DeviceCertification>(TestContext.Current.CancellationToken);

        var updateRequest = new DeviceCertificationStatusChangeRequest(
            created!.Vendor, created.Model, created.Firmware, created.Sdk,
            CertificationStatus.Deprecated, "https://evidence.internal/reports/rev-2",
            ExpectedVersion: created.Version, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);
        var updateResponse = await client.PatchAsJsonAsync(
            "/api/v1/device-certifications/status", updateRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DeviceCertification>(TestContext.Current.CancellationToken);
        Assert.Equal(CertificationStatus.Deprecated, updated!.Status);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task updating_with_a_stale_expected_version_returns_a_concurrency_conflict()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/device-certifications", SampleRequest("ReaderConflict"), TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<DeviceCertification>(TestContext.Current.CancellationToken);

        var staleRequest = new DeviceCertificationStatusChangeRequest(
            created!.Vendor, created.Model, created.Firmware, created.Sdk,
            CertificationStatus.Deprecated, "n/a", ExpectedVersion: 999, ActorType: "user", ActorId: "usr_admin", CorrelationId: null);
        var response = await client.PatchAsJsonAsync("/api/v1/device-certifications/status", staleRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("concurrency_conflict", body.GetProperty("title").GetString());
    }

    // --- BIO-DEV-002-AC03, upgraded evidence (ADR-0002 Decision 6): a real verified host resolves the tenant ---

    [Fact]
    public async Task Registering_a_tenant_override_through_a_verified_hostname_resolves_the_real_tenant_and_stores_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "cert-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(
            HostRequest(HttpMethod.Post, "/api/v1/tenant/device-certifications", hostname, SampleRequest("ReaderOverrideAC03")), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(TenantA, body.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task Registering_a_tenant_override_through_an_unrecognised_host_fails_closed_as_tenant_context_missing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(HostRequest(
            HttpMethod.Post, "/api/v1/tenant/device-certifications", "nobody-registered-this-cert-host.example.edu.ng",
            SampleRequest("ReaderNoHost")), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_tenants_override_registered_under_its_own_verified_host_does_not_appear_in_another_tenants_override_listing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "cert-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "cert-tenant-b-e2e.example.edu.ng", TenantB, ct);

        var created = await client.SendAsync(
            HostRequest(HttpMethod.Post, "/api/v1/tenant/device-certifications", hostA, SampleRequest("ReaderIsolationAC03")), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var listForB = await client.SendAsync(HostRequest(HttpMethod.Get, "/api/v1/tenant/device-certifications", hostB), ct);
        var listForA = await client.SendAsync(HostRequest(HttpMethod.Get, "/api/v1/tenant/device-certifications", hostA), ct);

        var entriesForB = await listForB.Content.ReadFromJsonAsync<JsonElement[]>(ct);
        var entriesForA = await listForA.Content.ReadFromJsonAsync<JsonElement[]>(ct);
        Assert.DoesNotContain(entriesForB!, e => e.GetProperty("model").GetString() == "ReaderIsolationAC03");
        Assert.Contains(entriesForA!, e => e.GetProperty("model").GetString() == "ReaderIsolationAC03");
    }

    [Fact]
    public async Task A_tenants_override_takes_precedence_over_the_global_default_when_looked_up_through_that_tenants_host()
    {
        // BIO-DEV-002-AC03's central behaviour, end to end through the real host: the same
        // combination has two different effective statuses depending on which verified
        // tenant host asks - both proven through the composed host, not unit-level mocks.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "cert-effective-e2e.example.edu.ng", TenantA, ct);
        var request = SampleRequest("ReaderEffectiveAC03");

        await client.PostAsJsonAsync("/api/v1/device-certifications", request, ct); // global: Certified
        await client.SendAsync(HostRequest(
            HttpMethod.Post, "/api/v1/tenant/device-certifications", hostname,
            request with { Status = CertificationStatus.Unsupported }), ct); // this tenant's override: Unsupported

        var lookupUrl = "/api/v1/device-certifications/lookup?vendor=Acme&model=ReaderEffectiveAC03&firmware=1.2.3&sdk=sdk-2.0";
        var effectiveForTenant = await client.SendAsync(HostRequest(HttpMethod.Get, lookupUrl, hostname), ct);
        var effectiveWithNoHost = await client.GetAsync(lookupUrl, ct);

        var tenantBody = await effectiveForTenant.Content.ReadFromJsonAsync<JsonElement>(ct);
        var globalBody = await effectiveWithNoHost.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Unsupported", tenantBody.GetProperty("status").GetString());
        Assert.Equal("Certified", globalBody.GetProperty("status").GetString());
    }
}
