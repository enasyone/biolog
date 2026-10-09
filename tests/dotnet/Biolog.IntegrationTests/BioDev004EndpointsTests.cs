using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceCertification;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.DeviceOperations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// Developer tests for BIO-DEV-004, exercised through the real composed host: the real
/// <see cref="Api.Modules.BioDev004Module"/> DI wiring, the real
/// <see cref="Api.Features.DeviceRegistry.DeviceService"/> (BIO-DEV-001, including this
/// story's own new <c>RecordHeartbeatAsync</c>/<c>ListAsync</c>), the real
/// <see cref="Api.Features.DeviceCertification.DeviceCertificationService"/> (BIO-DEV-002)
/// and the real BIO-DEV-003 vendor adapter framework. Reuses the same header-switched
/// tenant test seam <c>DeviceRegistryTests</c>/<c>DeviceAdapterEndpointsTests</c> already
/// established, for the same reason documented there.
///
/// <b>SY-DEV004-01 (Seyi, round 1, Critical), fixed</b>: every route now requires a resolved,
/// provenance-verified actor who holds this story's own least-privilege permission
/// (<see cref="DeviceHealthPollingService.ReadPermission"/>/<see cref="DeviceRemoteJobService.ExecutePermission"/>).
/// <see cref="AuthorizedClientForTenant"/> is the default authorized caller every
/// pre-existing functional test below now uses; dedicated tests at the bottom of this class
/// prove missing actor / missing provenance / forged provenance / an unprivileged actor are
/// each refused explicitly, with a real, distinct status code - not a blanket rejection.
///
/// The internal <see cref="Biolog.Features.DeviceOperations.IPermissionGateway"/> is replaced
/// here with a test-controllable <see cref="MutableFakePermissionGateway"/> (the same shape
/// BIO-ATT-003's own <c>WorkforceAttendanceEndpointsIntegrationTests</c> uses) rather than
/// seeding real BIO-IDN-006 RBAC grants - this class's own per-test unique tenant ids (used
/// to prove certification/cross-tenant isolation, unlike BIO-TRN-003's own single fixed demo
/// tenant) would otherwise need a real grant seeded per tenant, which would re-prove BIO-IDN-006's
/// own <c>PermissionService</c> rather than BIO-DEV-004's own gating/wiring - already proven
/// end-to-end once, for a different story, by <c>BioTrn003EndpointsTests</c>.
/// </summary>
public sealed class BioDev004EndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";
    private const string ProvenanceSecret = "test-host-e2e-dev004-actor-provenance-secret-ok";
    private const string DefaultActorId = "usr_ops";

    public BioDev004EndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private WebApplicationFactory<Program> WithTenantSwitching(bool grantAllPermissions = true) =>
        _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Biolog.Features.DeviceOperations.Api.ActorProvenanceProvisioning.ConfigurationKey] = ProvenanceSecret,
            }))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<ITenantContextAccessor>();
                services.AddHttpContextAccessor();
                services.AddScoped<ITenantContextAccessor, HeaderSwitchedTenantContextAccessor>();

                services.RemoveAll<Biolog.Features.DeviceOperations.IPermissionGateway>();
                var permissionGateway = new MutableFakePermissionGateway { GrantAll = grantAllPermissions };
                services.AddSingleton(permissionGateway);
                services.AddSingleton<Biolog.Features.DeviceOperations.IPermissionGateway>(permissionGateway);
            }));

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private static HttpClient CreateClientForTenant(WebApplicationFactory<Program> factory, string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestTenantHeader, tenantId);
        return client;
    }

    /// <summary>The default authorized caller for every functional (non-auth-focused) test
    /// below: a resolved actor with a valid provenance proof, against a
    /// <see cref="MutableFakePermissionGateway"/> configured to grant everything.</summary>
    private static HttpClient AuthorizedClientForTenant(WebApplicationFactory<Program> factory, string tenantId, string actorId = DefaultActorId)
    {
        var client = CreateClientForTenant(factory, tenantId);
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorResolver.ActorIdHeader, actorId);
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(actorId));
        return client;
    }

    private sealed class HeaderSwitchedTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
    {
        public TenantContext? Context
        {
            get
            {
                var tenantId = httpContextAccessor.HttpContext?.Request.Headers[TestTenantHeader].FirstOrDefault();
                return tenantId is null ? null : new TenantContext(tenantId, $"binding-{tenantId}", []);
            }
            set { /* ignore - resolved from the test header instead */ }
        }
    }

    /// <summary>Granted-by-default (<see cref="GrantAll"/>), with per-permission-key denial for
    /// the tests that specifically prove the gate itself - same shape as the feature-unit
    /// project's own <c>FakePermissionGateway</c>, but mutable after construction since it is
    /// registered once per <see cref="WebApplicationFactory{Program}"/> and some tests need to
    /// flip it mid-test (create as an unprivileged actor, then re-check as an authorized one).</summary>
    private sealed class MutableFakePermissionGateway : Biolog.Features.DeviceOperations.IPermissionGateway
    {
        public bool GrantAll { get; set; } = true;
        public HashSet<string> DeniedPermissionKeys { get; } = [];

        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(GrantAll && !DeniedPermissionKeys.Contains(permissionKey));
    }

    private static async Task<Device> RegisterDeviceAsync(
        HttpClient client, string vendor, string model, string firmware, string sdk, string serial, CancellationToken ct)
    {
        var request = new DeviceRegistrationRequest(
            serial, vendor, model, firmware, sdk, "Main Campus", new NetworkMetadata(null, null, null), CredentialReference: null);
        var response = await client.PostAsJsonAsync("/api/v1/devices", request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Device>(ct))!;
    }

    private static async Task RegisterGlobalCertificationAsync(
        HttpClient client, string vendor, string model, string firmware, string sdk, CertificationStatus status, CancellationToken ct)
    {
        var request = new DeviceCertificationRegistrationRequest(
            vendor, model, firmware, sdk, status, "https://evidence.internal/reports/bio-dev-004",
            ActorType: "user", ActorId: "usr_ops", CorrelationId: null);
        var response = await client.PostAsJsonAsync("/api/v1/device-certifications", request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AC01_polling_health_for_a_certified_device_updates_last_seen_status_and_health()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = AuthorizedClientForTenant(factory, "ten_dev004_ac01");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(controlPlaneClient, "Acme", "FS-100-D4", "2.3.1", "sdk-4.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(client, "Acme", "FS-100-D4", "2.3.1", "sdk-4.0", "SN-D4-1", ct);
        Assert.Null(device.LastSeenAt);

        var pollRequest = new { CorrelationId = (string?)null };
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/poll-health", pollRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The poll endpoint returns this story's own narrow DeviceSnapshot (same "return what's
        // relevant to the action" shape BIO-DEV-003's capabilities/health endpoints already use),
        // not the full Device record - read as JsonElement rather than binding to Device.
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(updated.GetProperty("lastSeenAt").ValueKind == JsonValueKind.Null);
        Assert.Equal("Online", updated.GetProperty("health").GetString());
        Assert.Equal(device.Version + 1, updated.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task AC01_the_device_list_reflects_a_recorded_heartbeat()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = AuthorizedClientForTenant(factory, "ten_dev004_ac01_list");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(controlPlaneClient, "Acme", "FS-100-LIST", "2.3.1", "sdk-4.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(client, "Acme", "FS-100-LIST", "2.3.1", "sdk-4.0", "SN-LIST-1", ct);
        await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/poll-health", new { CorrelationId = (string?)null }, ct);

        var listResponse = await client.GetAsync("/api/v1/devices", ct);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var devices = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>(ct);
        var listed = Assert.Single(devices!, d => d.GetProperty("id").GetString() == device.Id);
        Assert.False(listed.GetProperty("lastSeenAt").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AC04_polling_a_combination_with_no_certification_record_fails_explicitly()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = AuthorizedClientForTenant(WithTenantSwitching(), "ten_dev004_ac04_norecord");

        var device = await RegisterDeviceAsync(client, "Acme", "FS-999-UNSEEN-D4", "9.9.9", "sdk-9", "SN-D4-2", ct);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/devices/{device.Id}/poll-health", new { CorrelationId = (string?)null }, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_combination_unsupported", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AC02_AC03_running_a_supported_remote_job_succeeds_and_records_a_real_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = AuthorizedClientForTenant(factory, "ten_dev004_ac02_ok");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(controlPlaneClient, "Acme", "FS-100-JOB", "2.3.1", "sdk-4.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(client, "Acme", "FS-100-JOB", "2.3.1", "sdk-4.0", "SN-JOB-1", ct);

        // Acme resolves to FingerprintScanner, which this module's own job table supports
        // Reboot for (see BioDev004Module.SupportedJobsByCategory).
        var jobRequest = new { JobType = "Reboot", CorrelationId = (string?)null };
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/remote-jobs", jobRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Succeeded", body.GetProperty("outcome").GetString());

        using var scope = factory.Services.CreateScope();
        var auditSink = (InMemoryAuditSink)scope.ServiceProvider.GetRequiredService<IAuditSink>();
        var recorded = Assert.Single(auditSink.Events, e => e.Target.Id == device.Id && e.Action == "device.remote_job_executed");
        Assert.Equal("ten_dev004_ac02_ok", recorded.TenantId);
        Assert.Equal("Reboot", recorded.ReasonCode);
        Assert.Equal(DefaultActorId, recorded.Actor.Id);
    }

    [Fact]
    public async Task AC02_AC04_a_job_type_the_devices_category_does_not_support_is_rejected_explicitly()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = AuthorizedClientForTenant(factory, "ten_dev004_ac02_unsupported_job");
        var controlPlaneClient = factory.CreateClient();

        // OrbitId resolves to NfcUhfReader, which only supports Reboot in this module's table.
        await RegisterGlobalCertificationAsync(controlPlaneClient, "OrbitId", "RD-200-D4", "1.0.0", "sdk-2.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(client, "OrbitId", "RD-200-D4", "1.0.0", "sdk-2.0", "SN-JOB-2", ct);

        var jobRequest = new { JobType = "UpdateFirmware", CorrelationId = (string?)null };
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/remote-jobs", jobRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("remote_job_not_supported", body.GetProperty("title").GetString());
        Assert.Equal("UpdateFirmware", body.GetProperty("job_type").GetString());

        using var scope = factory.Services.CreateScope();
        var auditSink = (InMemoryAuditSink)scope.ServiceProvider.GetRequiredService<IAuditSink>();
        Assert.DoesNotContain(auditSink.Events, e => e.Target.Id == device.Id);
    }

    [Fact]
    public async Task AC04_a_job_against_an_uncertified_combination_fails_explicitly_with_no_audit_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = AuthorizedClientForTenant(factory, "ten_dev004_ac04_job_norecord");

        var device = await RegisterDeviceAsync(client, "Acme", "FS-777-NOCERT", "1.0.0", "sdk-1", "SN-JOB-3", ct);

        var jobRequest = new { JobType = "Reboot", CorrelationId = (string?)null };
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/remote-jobs", jobRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_combination_unsupported", body.GetProperty("title").GetString());

        using var scope = factory.Services.CreateScope();
        var auditSink = (InMemoryAuditSink)scope.ServiceProvider.GetRequiredService<IAuditSink>();
        Assert.DoesNotContain(auditSink.Events, e => e.Target.Id == device.Id);
    }

    [Fact]
    public async Task remote_job_capabilities_endpoint_reports_the_supported_job_types_for_a_certified_device()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = AuthorizedClientForTenant(factory, "ten_dev004_caps");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(controlPlaneClient, "NovaVision", "FT-300-D4", "1.0.0", "sdk-3.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(client, "NovaVision", "FT-300-D4", "1.0.0", "sdk-3.0", "SN-CAPS-1", ct);

        var response = await client.GetAsync($"/api/v1/devices/{device.Id}/remote-job-capabilities", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jobTypes = await response.Content.ReadFromJsonAsync<string[]>(ct);
        Assert.NotNull(jobTypes);
        Assert.Contains("Reboot", jobTypes);
        Assert.Contains("UpdateFirmware", jobTypes);
        Assert.Contains("PushConfiguration", jobTypes);
    }

    [Fact]
    public async Task devices_list_and_poll_health_endpoints_fail_closed_with_no_resolved_tenant_context()
    {
        var factory = WithTenantSwitching();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorResolver.ActorIdHeader, DefaultActorId);
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(DefaultActorId));

        var listResponse = await client.GetAsync("/api/v1/devices", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, listResponse.StatusCode);

        var pollResponse = await client.PostAsJsonAsync(
            "/api/v1/devices/dev_00000000-0000-7000-8000-000000000000/poll-health",
            new { CorrelationId = (string?)null },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, pollResponse.StatusCode);
    }

    [Fact]
    public async Task AC04_a_device_registered_under_one_tenant_cannot_be_polled_or_job_run_by_a_different_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var clientA = AuthorizedClientForTenant(factory, "ten_dev004_isolation_a");
        var clientB = AuthorizedClientForTenant(factory, "ten_dev004_isolation_b");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(controlPlaneClient, "Acme", "FS-100-ISO", "2.3.1", "sdk-4.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(clientA, "Acme", "FS-100-ISO", "2.3.1", "sdk-4.0", "SN-ISO-1", ct);

        var pollResponse = await clientB.PostAsJsonAsync(
            $"/api/v1/devices/{device.Id}/poll-health", new { CorrelationId = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.NotFound, pollResponse.StatusCode);

        var jobResponse = await clientB.PostAsJsonAsync(
            $"/api/v1/devices/{device.Id}/remote-jobs", new { JobType = "Reboot", CorrelationId = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.NotFound, jobResponse.StatusCode);
    }

    // --- SY-DEV004-01: the authorization-boundary tests themselves ---

    [Fact]
    public async Task With_no_X_Actor_Id_at_all_every_route_is_rejected_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_dev004_noactor");

        var listResponse = await client.GetAsync("/api/v1/devices", ct);
        Assert.Equal(HttpStatusCode.BadRequest, listResponse.StatusCode);
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task With_no_X_Actor_Provenance_at_all_every_route_is_rejected_401_not_a_blanket_rejection()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_dev004_noprovenance");
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorResolver.ActorIdHeader, DefaultActorId);

        var listResponse = await client.GetAsync("/api/v1/devices", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_forged_X_Actor_Provenance_computed_for_a_different_actor_id_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_dev004_forgedprovenance");
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorResolver.ActorIdHeader, "usr_attacker");
        // A real proof, but computed for a DIFFERENT actor id than the one asserted above -
        // the exact forged-attribution shape SY-DEV004-01 named.
        client.DefaultRequestHeaders.Add(Biolog.Features.DeviceOperations.Api.ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof("usr_someone_else"));

        var listResponse = await client.GetAsync("/api/v1/devices", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
    }

    [Fact]
    public async Task An_actor_with_no_read_permission_is_denied_listing_devices_and_polling_health_403_not_a_silent_empty_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching(grantAllPermissions: false);
        var client = AuthorizedClientForTenant(factory, "ten_dev004_unpriv_read");

        var listResponse = await client.GetAsync("/api/v1/devices", ct);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", listBody.GetProperty("title").GetString());
        Assert.Equal(DeviceHealthPollingService.ReadPermission, listBody.GetProperty("permission_key").GetString());

        var pollResponse = await client.PostAsJsonAsync(
            "/api/v1/devices/dev_00000000-0000-7000-8000-000000000000/poll-health", new { CorrelationId = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, pollResponse.StatusCode);
    }

    [Fact]
    public async Task An_actor_with_read_but_not_execute_permission_can_see_capabilities_but_cannot_run_a_job()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var gateway = factory.Services.GetRequiredService<MutableFakePermissionGateway>();
        gateway.DeniedPermissionKeys.Add(DeviceRemoteJobService.ExecutePermission);

        var client = AuthorizedClientForTenant(factory, "ten_dev004_read_only");
        var controlPlaneClient = factory.CreateClient();
        await RegisterGlobalCertificationAsync(controlPlaneClient, "Acme", "FS-100-ROPERM", "2.3.1", "sdk-4.0", CertificationStatus.Certified, ct);
        var device = await RegisterDeviceAsync(client, "Acme", "FS-100-ROPERM", "2.3.1", "sdk-4.0", "SN-ROPERM-1", ct);

        var capabilitiesResponse = await client.GetAsync($"/api/v1/devices/{device.Id}/remote-job-capabilities", ct);
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);

        var jobResponse = await client.PostAsJsonAsync(
            $"/api/v1/devices/{device.Id}/remote-jobs", new { JobType = "Reboot", CorrelationId = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, jobResponse.StatusCode);
        var jobBody = await jobResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(DeviceRemoteJobService.ExecutePermission, jobBody.GetProperty("permission_key").GetString());

        using var scope = factory.Services.CreateScope();
        var auditSink = (InMemoryAuditSink)scope.ServiceProvider.GetRequiredService<IAuditSink>();
        Assert.DoesNotContain(auditSink.Events, e => e.Target.Id == device.Id);
    }
}
