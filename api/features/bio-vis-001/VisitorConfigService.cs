using Biolog.Features.Visitors.Api;
using Biolog.Features.Visitors.Audit;
using Biolog.Features.Visitors.Domain;
using Biolog.Features.Visitors.Tenancy;

namespace Biolog.Features.Visitors;

/// <summary>
/// Application service for AC03: reading and setting the tenant-wide visitor feature config.
/// Implements the same audit-atomicity pattern as <see cref="VisitorRecordService"/>.
///
/// <b>This is this feature's other authorization decision (ADR-0006 Decision 1/2), alongside
/// approve/deny.</b> Setting the config changes what EVERY future preregistration in the tenant
/// requires before check-in - a capability, gated by identity, not merely an audit-attribution
/// record of who changed it. <see cref="ManagePermission"/> is required via
/// <see cref="IPermissionGateway"/>, and the endpoint additionally requires a verified ADR-0006
/// Decision 3 provenance proof. <see cref="GetAsync"/> stays open - reading the CURRENT config
/// discloses no more than what <see cref="VisitorRecordService.CheckInAsync"/> already applies
/// to every check-in attempt.
/// </summary>
internal sealed class VisitorConfigService(
    ICurrentTenantProvider tenantProvider,
    IVisitorConfigRepository repository,
    IVisitorAuditSink auditSink,
    VisitorConfigLockRegistry lockRegistry,
    IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "visitor.config_manage";

    public async Task<VisitorTenantConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, cancellationToken);
    }

    public async Task<VisitorTenantConfig> SetAsync(SetVisitorConfigRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw VisitorError.PermissionRequired(ManagePermission);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, cancellationToken);

        var current = await repository.GetAsync(tenantId, cancellationToken);
        var updated = current.Version == 0
            ? VisitorTenantConfig.Create(
                tenantId, request.RequireVehiclePlate, request.RequireNda, request.RequireSafetyBriefing, request.RequireWatchlistScreening,
                request.AllowTemporaryCredential, request.DefaultVisitDurationHours, request.MaxVisitDurationHours, actor.Id, Identifiers.UtcInstant.Now())
            : current.Update(
                request.RequireVehiclePlate, request.RequireNda, request.RequireSafetyBriefing, request.RequireWatchlistScreening,
                request.AllowTemporaryCredential, request.DefaultVisitDurationHours, request.MaxVisitDurationHours, actor.Id, Identifiers.UtcInstant.Now());

        await AuditAsync(tenantId, actor, "visitor.config_updated", correlationId,
            new Dictionary<string, (object?, string)> { ["version"] = (updated.Version, "internal") },
            cancellationToken);

        if (!await repository.TrySaveAsync(updated, current.Version, CancellationToken.None))
        {
            throw new VisitorError(
                "the visitor config was modified since the version this request expected", "concurrency_conflict", 409,
                new Dictionary<string, object?> { ["expected_version"] = current.Version });
        }

        return updated;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = VisitorAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("visitor_config", tenantId), AuditOutcome.Allowed, "visitor_config_updated", correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw VisitorError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw VisitorError.TenantContextMissing();
}
