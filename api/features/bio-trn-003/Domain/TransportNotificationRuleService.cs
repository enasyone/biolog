namespace Biolog.Features.TransportNotifications;

/// <summary>
/// AC01's own tenant-scoped rule management, tied to the same actor+permission gate every
/// sibling story that makes a real authorization decision uses (ADR-0006 Decision 3 + BIO-IDN-006's
/// real RBAC grants, via <see cref="IPermissionGateway"/>) - the same shape BIO-INT-004's own
/// <c>WebhookSubscriptionService</c> already established.
/// </summary>
internal sealed class TransportNotificationRuleService(ITransportNotificationRuleRepository repository, IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "transport_notifications.manage";
    public const string ReadPermission = "transport_notifications.read";

    public async Task<TransportNotificationRule> CreateAsync(
        string tenantId, AuditActor actor, string name, IReadOnlySet<TransportNotificationEventType> eventTypes, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        var rule = TransportNotificationRule.Create(tenantId, name, eventTypes);
        await repository.AddAsync(rule, cancellationToken);
        return rule;
    }

    public async Task<IReadOnlyList<TransportNotificationRule>> ListAsync(string tenantId, AuditActor actor, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ReadPermission, cancellationToken);
        return await repository.ListForTenantAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// SY-TRN003-01 (Seyi, High): the version check below and the write that follows it are NOT
    /// one atomic step - two concurrent callers can both read the SAME stale <paramref name="expectedVersion"/>-
    /// matching record, both pass this check, and both compute their own "version + 1" update.
    /// <see cref="ITransportNotificationRuleRepository.TryReplaceAsync"/> is what actually closes
    /// that window: it only commits a caller's own write if the stored record is still exactly
    /// the one this method read, so the loser of a genuine race is rejected with a conflict citing
    /// the real current version, never silently overwritten.
    /// </summary>
    public async Task<TransportNotificationRule> SetEnabledAsync(
        string tenantId, AuditActor actor, string ruleId, bool enabled, int expectedVersion, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        var existing = await repository.GetAsync(tenantId, ruleId, cancellationToken) ?? throw new RuleNotFoundException(ruleId);
        if (existing.Version != expectedVersion)
        {
            throw new RuleConcurrencyConflictException(existing.Version);
        }

        var updated = existing.WithEnabled(enabled, existing.Version + 1);
        if (!await repository.TryReplaceAsync(existing, updated, cancellationToken))
        {
            var current = await repository.GetAsync(tenantId, ruleId, cancellationToken) ?? throw new RuleNotFoundException(ruleId);
            throw new RuleConcurrencyConflictException(current.Version);
        }

        return updated;
    }

    private async Task RequirePermissionAsync(string tenantId, AuditActor actor, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken))
        {
            throw new PermissionRequiredException(permissionKey);
        }
    }
}
