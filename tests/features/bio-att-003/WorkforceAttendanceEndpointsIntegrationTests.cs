using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.WorkforceAttendance.Api;
using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.WorkforceAttendance.Tests;

/// <summary>AC01-AC05: real HTTP pipeline tests for this story's own endpoints, self-hosted via <see cref="TestServer"/>.</summary>
public sealed class WorkforceAttendanceEndpointsIntegrationTests : IAsyncLifetime
{
    private const string ProvenanceSecret = "test-provenance-secret-32-bytes!"; // exactly 32 bytes - HmacActorProvenanceStore.MinimumSecretLength
    private const string ActorId = "per_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantId = "ten_018f2e5a-5678-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9999-7abc-89ab-0123456789ab";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;
    private MutableFakePermissionGateway _permissionGateway = null!;
    private FakeAttendanceEventGateway _eventGateway = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantId);
        _permissionGateway = new MutableFakePermissionGateway();
        _eventGateway = new FakeAttendanceEventGateway();

        var provenanceStore = new HmacActorProvenanceStore();
        provenanceStore.Configure(ProvenanceSecret);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<Tenancy.ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<IWorkforceShiftRepository, InMemoryWorkforceShiftRepository>();
                services.AddSingleton<IWorkforceAttendanceAdjustmentRepository, InMemoryWorkforceAttendanceAdjustmentRepository>();
                services.AddSingleton<IWorkforceAttendanceAuditSink, InMemoryWorkforceAttendanceAuditSink>();
                services.AddSingleton<WorkforceShiftLockRegistry>();
                services.AddSingleton<IAttendanceEventGateway>(_eventGateway);
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddSingleton<IPermissionGateway>(_permissionGateway);
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
                services.AddScoped<WorkforceShiftService>();
                services.AddScoped<WorkforceAttendanceService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(WorkforceAttendanceEndpoints.Map);
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(TestContext.Current.CancellationToken);
        _host.Dispose();
    }

    private static string ProvenanceProof(string actorId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private static HttpRequestMessage AdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ProvenanceProof(ActorId));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<string> CreateShiftAsync()
    {
        var response = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/workforce-attendance/shifts",
            new { name = "Day", start_time = "08:00", end_time = "17:00", grace_minutes = 0 }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        return body.GetProperty("shift_id").GetString()!;
    }

    // ---- AC01: shift CRUD ----

    [Fact]
    public async Task Creating_a_shift_without_a_provenance_header_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workforce-attendance/shifts")
        {
            Content = JsonContent.Create(new { name = "Day", start_time = "08:00", end_time = "17:00", grace_minutes = 0 }),
        };
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_shift_without_the_manage_permission_is_forbidden_403()
    {
        var ct = TestContext.Current.CancellationToken;
        _permissionGateway.Grant = false;

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Post, "/api/v1/workforce-attendance/shifts",
            new { name = "Day", start_time = "08:00", end_time = "17:00", grace_minutes = 0 }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Creating_then_getting_a_shift_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/workforce-attendance/shifts/{shiftId}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("Day", body.GetProperty("name").GetString());
        Assert.Equal(540, body.GetProperty("expected_duration_minutes").GetInt32());
    }

    [Fact]
    public async Task Listing_shifts_requires_no_actor_at_all()
    {
        var ct = TestContext.Current.CancellationToken;
        await CreateShiftAsync();

        var response = await _client.GetAsync("/api/v1/workforce-attendance/shifts", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(1, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Updating_a_shift_with_a_stale_version_returns_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Patch, $"/api/v1/workforce-attendance/shifts/{shiftId}",
            new { version = 0, name = "Night", start_time = "22:00", end_time = "06:00", grace_minutes = 5 }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- AC02/AC03: daily summary ----

    [Fact]
    public async Task Getting_a_daily_summary_without_a_shift_id_query_parameter_is_rejected_422()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/workforce-attendance/people/{PersonId}/daily-summary/2026-10-07"), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_complete_clock_pair_is_reflected_in_the_daily_summary_with_the_right_overtime()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();
        _eventGateway.Add(PersonId, "2026-10-07", new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:00:00.000Z" });
        _eventGateway.Add(PersonId, "2026-10-07", new ClockEventView { EventId = "evt_out", Mode = ClockMode.CheckOut, OccurredAt = "2026-10-07T18:00:00.000Z" });

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/workforce-attendance/people/{PersonId}/daily-summary/2026-10-07?shift_id={shiftId}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(1, body.GetProperty("pairs").GetArrayLength());
        Assert.Equal("complete", body.GetProperty("pairs")[0].GetProperty("status").GetString());
        Assert.Equal(60, body.GetProperty("computed_overtime_minutes").GetInt32());
        Assert.Equal("on_time", body.GetProperty("interpretation").GetProperty("status").GetString());
    }

    // ---- BIO-ATT-003-F01: lateness and absence interpretation ----

    [Fact]
    public async Task A_day_with_no_clock_events_reports_an_absent_interpretation_over_real_http()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/workforce-attendance/people/{PersonId}/daily-summary/2026-10-07?shift_id={shiftId}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("absent", body.GetProperty("interpretation").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_late_arrival_reports_a_late_interpretation_with_minutes_late_over_real_http()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();
        _eventGateway.Add(PersonId, "2026-10-07", new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:30:00.000Z" });

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/workforce-attendance/people/{PersonId}/daily-summary/2026-10-07?shift_id={shiftId}"), ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("late", body.GetProperty("interpretation").GetProperty("status").GetString());
        Assert.Equal(30, body.GetProperty("interpretation").GetProperty("minutes_late").GetInt32());
        Assert.Equal("evt_in", body.GetProperty("interpretation").GetProperty("first_check_in_event_id").GetString());
    }

    // ---- AC04: exceptions require audit reason ----

    [Fact]
    public async Task Creating_an_adjustment_without_a_reason_is_rejected_422_and_does_not_override_anything()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Post, $"/api/v1/workforce-attendance/people/{PersonId}/adjustments/2026-10-07",
            new { shift_id = shiftId, adjusted_overtime_minutes = 30 }), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_adjustment_with_a_reason_succeeds_and_is_reflected_in_the_next_summary_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var shiftId = await CreateShiftAsync();

        var createResponse = await _client.SendAsync(AdminRequest(HttpMethod.Post, $"/api/v1/workforce-attendance/people/{PersonId}/adjustments/2026-10-07",
            new { shift_id = shiftId, adjusted_overtime_minutes = 45, reason = "manual correction for a device outage" }), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var summaryResponse = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/workforce-attendance/people/{PersonId}/daily-summary/2026-10-07?shift_id={shiftId}"), ct);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        Assert.Equal(45, summary.GetProperty("effective_overtime_minutes").GetInt32());
        Assert.Equal("manual correction for a device outage", summary.GetProperty("adjustment").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/workforce-attendance/shifts", ct);

        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
    }

}
