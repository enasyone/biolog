using Biolog.Api.Tenancy;
using Biolog.Features.Attendance;
using Biolog.Features.Attendance.Api;
using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Biolog.Features.Attendance.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Attendance event core (BIO-ATT-001), registered the same way every sibling identity-wave
/// module wraps its feature project - written directly here, in <c>Biolog.Api.Modules</c>, from
/// the start (never inside <c>api/features/bio-att-001/**</c> first), so the dependency stays
/// one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PersonExistenceGatewayAdapter"/> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly (BIO-IDN-002-QA-01's lesson, applied from the start).
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration seam
/// for the one authorization decision it makes (<c>AttendancePolicyService.SetAsync</c>).
///
/// <b>This story's own scope boundary.</b> No adapter here reaches BIO-DEV-001 (device
/// registry/status) - not a declared dependency of this story (only BIO-IDN-001 and BIO-ARC-003
/// are); <c>device_id</c> is shape-validated only, never existence-gated (see
/// <c>Biolog.Features.Attendance.Identifiers.CanonicalId</c>'s own header).
///
/// <b>ADR-0006 Decision 3, applied only where Decision 2 actually requires it.</b>
/// <see cref="IActorProvenanceStore"/> is loaded from <c>IConfiguration</c> here, the same
/// out-of-band provisioning path every sibling module already establishes - but wired ONLY to
/// the policy-management endpoint (<see cref="ActorProvenanceGate"/>), not to capture/read, which
/// stay audit-attribution-only per Decision 2 - see
/// <c>docs/features/bio-att-001/README.md</c> "Why capture and read stay open, and policy does
/// not".
///
/// <b>AC05's own bridge moved to BIO-ATT-002.</b> This module used to also register a Singleton
/// <c>AttendanceDemoBridgeService</c> answering <c>/api/v1/attendance/primary-action</c> and
/// <c>/api/v1/attendance/flag-exception</c> - both removed (disclosed handoff, see
/// <c>Biolog.Features.AttendanceSessions.Demo.AttendanceSessionDemoBridgeService</c>'s own header
/// and <c>docs/features/bio-att-002/README.md</c> "Why this story edits BIO-ATT-001's own endpoint
/// mapping" for the full record).
/// </summary>
public sealed class BioAtt001Module : IBiologModule
{
    public string Name => "Attendance";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IAttendanceEventRepository, InMemoryAttendanceEventRepository>();
        services.AddSingleton<IAttendancePolicyRepository, InMemoryAttendancePolicyRepository>();
        services.AddSingleton<IAttendanceAuditSink, InMemoryAttendanceAuditSink>();
        services.AddSingleton<AttendanceEventLockRegistry>();
        services.AddSingleton<AttendancePolicyLockRegistry>();

        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddScoped<AttendanceEventService>();
        services.AddScoped<AttendanceStatusService>();
        services.AddScoped<AttendancePolicyService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AttendanceEndpoints.Map(endpoints);
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
}
