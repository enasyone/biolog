using Biolog.Api.Tenancy;
using Biolog.Features.AttendanceSessions;
using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Demo;
using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Education attendance policies (BIO-ATT-002): session/roster association, per-(tenant, session
/// type) configurable present/late/absent thresholds, and audited manual adjustments - registered
/// the same way every sibling identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PersonExistenceGatewayAdapter"/> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly. <see cref="PermissionGatewayAdapter"/> wraps
/// BIO-IDN-006's own already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly -
/// this story's RBAC integration seam for its two authorization decisions
/// (<c>SessionAttendancePolicyService.SetAsync</c>, <c>AttendanceAdjustmentService.CreateAdjustmentAsync</c>).
///
/// <see cref="AttendanceEventQueryGatewayAdapter"/> wraps BIO-ATT-001's own already-registered
/// (internal, <c>InternalsVisibleTo Biolog.Api</c>), Scoped <c>AttendanceEventService.ListForPersonOnDateAsync</c>
/// directly - reachable here only because that project's own csproj grants this assembly internals
/// visibility, the same cross-story gateway pattern BIO-TRN-002 established for its own dependency
/// on BIO-TRN-001. <see cref="AttendanceDemoSeedGatewayAdapter"/> is a SEPARATE, Singleton-safe
/// adapter wrapping BIO-ATT-001's own Singleton <c>IAttendanceEventRepository</c> directly (calling
/// its own internal <c>AttendanceEvent.Capture</c> factory) - used ONLY by
/// <see cref="AttendanceSessionDemoBridgeService"/>, never by the tenant-scoped Scoped gateway, the
/// same captive-dependency-safe split BIO-TRN-002's own <c>ITransportDemoSeedProvisioner</c> split
/// already established (a Singleton must never depend on a Scoped service, directly or
/// transitively).
///
/// <b>AC05 - this module's endpoint mapping takes over <c>/api/v1/attendance/primary-action</c>
/// and <c>/api/v1/attendance/flag-exception</c> from BIO-ATT-001's own (now-removed) bridge
/// mapping</b> - see <see cref="AttendanceSessionDemoBridgeService"/>'s own header and
/// <c>docs/features/bio-att-002/README.md</c> "Why this story edits BIO-ATT-001's own endpoint
/// mapping" for the full disclosed-handoff record.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn001Module"/> (registered as
/// <c>"PersonRegistry"</c>), <see cref="BioIdn006Module"/> (registered as <c>"Rbac"</c>) and
/// <see cref="BioAtt001Module"/> (registered as <c>"Attendance"</c>).
/// </summary>
public sealed class BioAtt002Module : IBiologModule
{
    public string Name => "AttendanceSessions";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Rbac", "Attendance"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
        services.AddSingleton<ISessionAttendancePolicyRepository, InMemorySessionAttendancePolicyRepository>();
        services.AddSingleton<IAttendanceAdjustmentRepository, InMemoryAttendanceAdjustmentRepository>();
        services.AddSingleton<ISessionAuditSink, InMemorySessionAuditSink>();
        services.AddSingleton<SessionLockRegistry>();
        services.AddSingleton<SessionAttendancePolicyLockRegistry>();

        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<IAttendanceEventQueryGateway>(
            sp => new AttendanceEventQueryGatewayAdapter(sp.GetRequiredService<Biolog.Features.Attendance.AttendanceEventService>()));
        services.AddSingleton<IAttendanceDemoSeedGateway>(
            sp => new AttendanceDemoSeedGatewayAdapter(sp.GetRequiredService<Biolog.Features.Attendance.Domain.IAttendanceEventRepository>()));

        services.AddScoped<SessionService>();
        services.AddScoped<SessionAttendancePolicyService>();
        services.AddScoped<SessionAttendanceQueryService>();
        services.AddScoped<AttendanceAdjustmentService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddSingleton<AttendanceSessionDemoBridgeService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        SessionEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    /// <summary>
    /// AC01/AC02/AC03's cross-story read seam onto BIO-ATT-001. Registered Scoped: delegates to
    /// BIO-ATT-001's own Scoped <c>AttendanceEventService</c>, which resolves the ambient tenant
    /// itself via its own <c>ICurrentTenantProvider</c> registration (a separate DI entry from this
    /// module's own <see cref="CurrentTenantProviderAdapter"/>, but both read the same underlying
    /// <see cref="ITenantContextAccessor"/>, so they always agree).
    /// </summary>
    private sealed class AttendanceEventQueryGatewayAdapter(Biolog.Features.Attendance.AttendanceEventService attendanceEventService) : IAttendanceEventQueryGateway
    {
        public async Task<IReadOnlyList<AttendanceEvidenceRecord>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default)
        {
            var events = await attendanceEventService.ListForPersonOnDateAsync(personId, date, cancellationToken);
            return [.. events.Select(MapToEvidenceRecord)];
        }
    }

    /// <summary>
    /// AC05's demo-seed write, registered Singleton (see <see cref="IAttendanceDemoSeedGateway"/>'s
    /// own header for why this is a SEPARATE class/interface from
    /// <see cref="AttendanceEventQueryGatewayAdapter"/>). Depends only on BIO-ATT-001's own
    /// Singleton <c>IAttendanceEventRepository</c> - no <c>ICurrentTenantProvider</c> dependency at
    /// all, so no captive-dependency risk. Mints a real <c>AttendanceEvent</c> through BIO-ATT-001's
    /// own aggregate factory (<c>AttendanceEvent.Capture</c>, <c>internal static</c>, reachable
    /// only because of this assembly's own <c>InternalsVisibleTo</c> grant) the first time the demo
    /// bridge needs one for a given (person, day); idempotent thereafter, mirroring BIO-ATT-001's
    /// own (now-removed) bridge's exact idempotency-key convention.
    /// </summary>
    private sealed class AttendanceDemoSeedGatewayAdapter(Biolog.Features.Attendance.Domain.IAttendanceEventRepository eventRepository) : IAttendanceDemoSeedGateway
    {
        public async Task<AttendanceEvidenceRecord> CaptureDemoCheckInAsync(string tenantId, string personId, string deviceId, string location, string date, CancellationToken cancellationToken = default)
        {
            var idempotencyKey = $"demo-bridge-{personId}-{date}";
            var occurredAt = Biolog.Features.Attendance.Identifiers.UtcInstant.Now();
            var evidence = new Biolog.Features.Attendance.Domain.VerificationEvidence
            {
                Method = Biolog.Features.Attendance.Domain.VerificationMethod.Credential,
                Verified = true,
                Confidence = 0.95,
            };

            var fingerprint = Biolog.Features.Attendance.Domain.AttendanceEvent.ComputeFingerprint(
                tenantId, personId, Biolog.Features.Attendance.Domain.AttendanceSource.Device, deviceId, location,
                Biolog.Features.Attendance.Domain.AttendanceMode.CheckIn, evidence, occurredAt, idempotencyKey);

            var existing = await eventRepository.GetByIdempotencyKeyAsync(tenantId, idempotencyKey, cancellationToken);
            Biolog.Features.Attendance.Domain.AttendanceEvent captured;
            if (existing is not null)
            {
                // Identical redelivery within the same day, or the clock rolled over mid-run -
                // either way, answer with the day's already-captured fact rather than fail the
                // demo route, the same tolerance BIO-ATT-001's own (now-removed) bridge applied.
                captured = existing;
            }
            else
            {
                captured = Biolog.Features.Attendance.Domain.AttendanceEvent.Capture(
                    tenantId, personId, Biolog.Features.Attendance.Domain.AttendanceSource.Device, deviceId, location,
                    Biolog.Features.Attendance.Domain.AttendanceMode.CheckIn, evidence, occurredAt, occurredAt, idempotencyKey);
                await eventRepository.CreateAsync(captured, cancellationToken);
            }

            return MapToEvidenceRecord(captured);
        }

        public async Task<IReadOnlyList<AttendanceEvidenceRecord>> ListForPersonOnDateAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default)
        {
            var events = await eventRepository.ListForPersonOnDateAsync(tenantId, personId, date, cancellationToken);
            return [.. events.Select(MapToEvidenceRecord)];
        }
    }

    private static AttendanceEvidenceRecord MapToEvidenceRecord(Biolog.Features.Attendance.Domain.AttendanceEvent e) => new()
    {
        EventId = e.EventId,
        OccurredAt = e.OccurredAt,
        Source = Biolog.Features.Attendance.Domain.AttendanceSourceExtensions.ToWireString(e.Source),
        Mode = Biolog.Features.Attendance.Domain.AttendanceModeExtensions.ToWireString(e.Mode),
        EvidenceMethod = Biolog.Features.Attendance.Domain.VerificationMethodExtensions.ToWireString(e.Evidence.Method),
        EvidenceVerified = e.Evidence.Verified,
        EvidenceConfidence = e.Evidence.Confidence,
    };
}
