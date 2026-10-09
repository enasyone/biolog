using Biolog.Features.TransportEvents.Api;
using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Biolog.Features.TransportEvents.Tenancy;

namespace Biolog.Features.TransportEvents;

/// <summary>
/// Application service for AC04's tenant notification-trigger config. Implements the same
/// audit-atomicity pattern as <see cref="TransportEventService"/>.
///
/// <b>Deliberately NOT gated by a permission/provenance check</b>, unlike
/// <c>AttendancePolicyService.SetAsync</c>/<c>VisitorConfigService.SetAsync</c> - see
/// <see cref="TransportNotificationConfig"/>'s own header for the re-derivation.
/// </summary>
internal sealed class TransportNotificationConfigService(
    ICurrentTenantProvider tenantProvider,
    ITransportNotificationConfigRepository repository,
    ITransportEventAuditSink auditSink,
    TransportNotificationConfigLockRegistry lockRegistry)
{
    public async Task<TransportNotificationConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, cancellationToken);
    }

    public async Task<TransportNotificationConfig> SetAsync(SetTransportNotificationConfigRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, cancellationToken);

        var current = await repository.GetAsync(tenantId, cancellationToken);
        var now = Identifiers.UtcInstant.Now();
        var updated = current.Version == 0
            ? TransportNotificationConfig.Create(tenantId, request.NotifyOnMissingAlightException, actor.Id, now)
            : current.Update(request.NotifyOnMissingAlightException, actor.Id, now);

        await AuditAsync(tenantId, actor, "transport_notification_config.updated", correlationId,
            new Dictionary<string, (object?, string)> { ["version"] = (updated.Version, "internal") },
            cancellationToken);

        if (!await repository.TrySaveAsync(updated, current.Version, CancellationToken.None))
        {
            throw TransportEventError.NotificationConfigConcurrencyConflict(current.Version);
        }

        return updated;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = TransportEventAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("transport_notification_config", tenantId), AuditOutcome.Allowed, "transport_notification_config_updated", correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw TransportEventError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw TransportEventError.TenantContextMissing();
}
