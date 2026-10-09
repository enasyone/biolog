using Biolog.Api.Tenancy;
using Biolog.Features.Attendance.Domain;
using Biolog.Features.WorkforceAttendance;
using Biolog.Features.WorkforceAttendance.Api;
using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Biolog.Features.WorkforceAttendance.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Workforce attendance policies (BIO-ATT-003), registered the same way every sibling
/// identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="AttendanceEventGatewayAdapter"/> wraps BIO-ATT-001's own already-registered,
/// Scoped <c>AttendanceEventService.ListForPersonOnDateAsync</c> directly - reachable here only
/// because that project's own csproj grants this assembly internals visibility. Maps BIO-ATT-001's
/// own <c>AttendanceEvent</c>/<c>AttendanceMode</c> onto this feature's own
/// <see cref="ClockEventView"/>/<see cref="ClockMode"/> - a read-only view, never a shared type,
/// since this project has no <c>ProjectReference</c> to BIO-ATT-001's assembly.
/// <see cref="PersonExistenceGatewayAdapter"/> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly (BIO-IDN-002-QA-01's lesson, applied from the start).
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration
/// seam for its two authorization decisions (shift management, adjustment creation).
///
/// Depends on <see cref="BioAtt001Module"/> (registered as <c>"Attendance"</c>),
/// <see cref="BioIdn001Module"/> (registered as <c>"PersonRegistry"</c>) and
/// <see cref="BioIdn006Module"/> (registered as <c>"Rbac"</c>).
/// </summary>
public sealed class BioAtt003Module : IBiologModule
{
    public string Name => "WorkforceAttendance";
    public IReadOnlyCollection<string> DependsOn => ["Attendance", "PersonRegistry", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IWorkforceShiftRepository, InMemoryWorkforceShiftRepository>();
        services.AddSingleton<IWorkforceAttendanceAdjustmentRepository, InMemoryWorkforceAttendanceAdjustmentRepository>();
        services.AddSingleton<IWorkforceAttendanceAuditSink, InMemoryWorkforceAttendanceAuditSink>();
        services.AddSingleton<WorkforceShiftLockRegistry>();

        services.AddScoped<IAttendanceEventGateway>(
            sp => new AttendanceEventGatewayAdapter(sp.GetRequiredService<Biolog.Features.Attendance.AttendanceEventService>()));
        services.AddScoped<Biolog.Features.WorkforceAttendance.Domain.IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<Biolog.Features.WorkforceAttendance.Domain.IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddScoped<WorkforceShiftService>();
        services.AddScoped<WorkforceAttendanceService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    /// <summary>
    /// TD-WEB-PROVENANCE-BRIDGE-REVERIFY (Tunde, 2026-10-07) - BIO-ATT-003-F02's own share of
    /// the obligation (same pattern as BIO-TEN-003's own
    /// <c>evidence/TD-WEB-PROVENANCE-BRIDGE/codex/REVERIFY-Seyi-BIO-RPT-004-BIO-TEN-003.md</c>
    /// closure): a real positive LIVE workflow proof needs a real seeded
    /// <see cref="Biolog.Features.Rbac.Domain.RolePermissionGrant"/> for this story's own two
    /// permission keys, scoped to the one hardcoded <c>WebSessionBridge:SyntheticTenantId</c>
    /// synthetic tenant round 5 of that TD already established.
    ///
    /// <b>Simpler than BIO-TEN-003's own equivalent fix</b> - no tenant-registry seeding needed
    /// here. This story resolves tenant AMBIENTLY throughout (<see cref="CurrentTenantProviderAdapter"/>,
    /// never an explicit route-supplied tenant id), so round 5's <c>DemoSessionTenantResolver</c>
    /// confining ambient resolution to the one synthetic tenant is sufficient on its own - this
    /// story has no separate tenant-existence check that would also need a real
    /// <c>ITenantRegistry</c> row (unlike BIO-TEN-003's own admin-console-shaped endpoint).
    ///
    /// Fail-closed/inert unless <c>WebSessionBridge:SyntheticTenantId</c> is configured,
    /// idempotent, writes directly to the repository (seed data, never through
    /// <see cref="Biolog.Features.Rbac.PermissionService.SetGrantAsync"/>, which expects a real
    /// administrative actor and audit trail a fixed startup fixture has neither of) - the same
    /// "seed data, not provisioning logic" shape <c>DemoSessionIdentitySeed</c> established.
    /// </summary>
    public void Configure(IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var tenantId = configuration["WebSessionBridge:SyntheticTenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var grants = app.ApplicationServices.GetRequiredService<Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository>();
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, WorkforceShiftService.ManagePermission);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, WorkforceAttendanceService.AdjustmentManagePermission);
    }

    private static void SeedGrant(Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository grants, string tenantId, Biolog.Features.Rbac.Domain.RoleType role, string permissionKey)
    {
        if (grants.GetAsync(tenantId, role, permissionKey).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        var now = Biolog.Features.Rbac.Identifiers.UtcInstant.Now();
        grants.UpsertAsync(new Biolog.Features.Rbac.Domain.RolePermissionGrant(tenantId, role, permissionKey)
        {
            Granted = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        }).GetAwaiter().GetResult();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        WorkforceAttendanceEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class AttendanceEventGatewayAdapter(Biolog.Features.Attendance.AttendanceEventService eventService) : IAttendanceEventGateway
    {
        public async Task<IReadOnlyList<ClockEventView>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default)
        {
            var events = await eventService.ListForPersonOnDateAsync(personId, date, cancellationToken);
            return [.. events.Select(MapToView)];
        }

        private static ClockEventView MapToView(Biolog.Features.Attendance.Domain.AttendanceEvent e)
        {
            if (!ClockModeExtensions.TryParse(e.Mode.ToWireString(), out var mode))
            {
                // Unreachable: BIO-ATT-001's own AttendanceMode wire vocabulary and this
                // feature's own ClockMode are kept in lockstep by construction (see
                // ClockMode.cs's own header) - a mismatch here would be a defect in THIS
                // adapter, never a caller input to recover from.
                throw new InvalidOperationException($"unmapped AttendanceMode wire value: {e.Mode.ToWireString()}");
            }

            return new ClockEventView { EventId = e.EventId, Mode = mode, OccurredAt = e.OccurredAt };
        }
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : Biolog.Features.WorkforceAttendance.Domain.IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : Biolog.Features.WorkforceAttendance.Domain.IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}
