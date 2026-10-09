using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Biolog.Features.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// Developer tests for BIO-RPT-001, exercised through the real composed host: the real
/// <see cref="Api.Modules.BioRpt001Module"/> DI wiring, the real
/// <see cref="IDomainEventBus"/>/<see cref="NotificationDispatchService"/> subscription
/// wired at startup (BIO-RPT-001-AC01), and the exact two endpoint paths
/// `web/features/reporting/live.ts` (BIO-UX-005) already targets for "Run report" and "View
/// audit trail" (BIO-RPT-001-AC05). Uses the same header-switched tenant test seam
/// `DeviceRegistryTests`/`DeviceAdapterEndpointsTests` established, for the same reason
/// documented there.
/// </summary>
public sealed class NotificationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";

    public NotificationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private WebApplicationFactory<Program> WithTenantSwitching() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContextAccessor>();
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContextAccessor, HeaderSwitchedTenantContextAccessor>();
        }));

    private static HttpClient CreateClientForTenant(WebApplicationFactory<Program> factory, string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestTenantHeader, tenantId);
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

    [Fact]
    public async Task AC01_registering_a_rule_and_running_the_primary_action_dispatches_and_delivers()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_rpt001_ac01");

        var ruleResponse = await client.PostAsJsonAsync(
            "/api/v1/notification-rules",
            new NotificationRuleRegistrationRequest("Device demo rule", AggregateType.Device, NotificationChannel.Email, 1, 1), ct);
        Assert.Equal(HttpStatusCode.Created, ruleResponse.StatusCode);

        var actionResponse = await client.PostAsync("/api/v1/reporting/primary-action", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, actionResponse.StatusCode);
        var body = await actionResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("1 delivery attempt", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AC05_view_audit_trail_summarizes_real_delivery_records_not_synthetic_ui_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = CreateClientForTenant(factory, "ten_rpt001_ac05");

        await client.PostAsJsonAsync(
            "/api/v1/notification-rules",
            new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Sms, 1, 1), ct);
        await client.PostAsync("/api/v1/reporting/primary-action", content: null, ct);

        var trailResponse = await client.PostAsync("/api/v1/reporting/view-audit-trail", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, trailResponse.StatusCode);
        var body = await trailResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("1 delivered", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task view_audit_trail_is_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var clientA = CreateClientForTenant(factory, "ten_rpt001_scope_a");
        var clientB = CreateClientForTenant(factory, "ten_rpt001_scope_b");

        await clientA.PostAsJsonAsync(
            "/api/v1/notification-rules",
            new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Push, 1, 1), ct);
        await clientA.PostAsync("/api/v1/reporting/primary-action", content: null, ct);

        var trailForB = await clientB.PostAsync("/api/v1/reporting/view-audit-trail", content: null, ct);

        var body = await trailForB.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.StartsWith("0 delivery attempt", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task primary_action_fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/reporting/primary-action", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task rule_status_change_with_a_stale_expected_version_returns_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_rpt001_conflict");

        var created = await client.PostAsJsonAsync(
            "/api/v1/notification-rules",
            new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Webhook, 1, 1), ct);
        var rule = await created.Content.ReadFromJsonAsync<NotificationRule>(ct);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/notification-rules/{rule!.RuleId}/status", new NotificationRuleStatusChangeRequest(false, ExpectedVersion: 999), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task a_disabled_rule_no_longer_receives_deliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_rpt001_disabled");

        var created = await client.PostAsJsonAsync(
            "/api/v1/notification-rules",
            new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Teams, 1, 1), ct);
        var rule = await created.Content.ReadFromJsonAsync<NotificationRule>(ct);
        await client.PatchAsJsonAsync($"/api/v1/notification-rules/{rule!.RuleId}/status", new NotificationRuleStatusChangeRequest(false, rule.Version), ct);

        var actionResponse = await client.PostAsync("/api/v1/reporting/primary-action", content: null, ct);

        var body = await actionResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("0 delivery attempt", body.GetProperty("message").GetString());
    }
}
