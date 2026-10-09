namespace Biolog.Features.TransportNotifications;

/// <summary>
/// AC01 (rule matching) + AC02 (minimizes before sending) + AC03 (visible retry/dead-letter) +
/// AC04 (read-only against bio-trn-002 - <see cref="ITransportEventFeedGateway"/> has no write
/// member at all, so this service can never mutate transport event truth).
///
/// <see cref="DispatchAndListAsync"/> is both the write path (runs any newly-matching rule
/// against the current feed) and the read path (returns the full durable trail) in one call -
/// the same "recompute/catch-up at read time" discipline bio-trn-002's own
/// <c>TransportEventService.GetPresenceStatusAsync</c> already establishes, chosen here so the web
/// panel's own "load" action needs no separate trigger/sync button: opening the panel is itself
/// what causes any new board/alight/missed-alight/exception fact to be evaluated against the
/// tenant's enabled rules.
///
/// One rule delivers AT MOST ONCE per underlying source event, even when that event independently
/// qualifies under more than one of the rule's own selected <see cref="TransportNotificationEventType"/>
/// values (e.g. a Board event with every evidence entry unverified is both Board and Exception) -
/// <see cref="ITransportNotificationDispatchLedger"/>'s claim key is
/// <c>(tenantId, ruleId, sourceEventId)</c>, not <c>(..., eventType)</c>, so the first classification
/// <see cref="ITransportEventFeedGateway"/> happens to list wins the claim and the second is a
/// true no-op - deliberately: a tenant configuring a rule for both categories should see one
/// notification about one physical happening, not two.
/// </summary>
internal sealed class TransportNotificationDispatchService(
    ITransportNotificationRuleRepository ruleRepository,
    ITransportEventFeedGateway feedGateway,
    ITransportNotificationDispatchLedger ledger,
    IInAppNotificationSender sender,
    ITransportNotificationDeliveryAuditSink auditSink,
    IPermissionGateway permissionGateway)
{
    /// <summary>Bounded retry, same MaxAttempts=3 vocabulary BIO-RPT-001's own <c>NotificationDispatchService</c> already established for this codebase.</summary>
    public const int MaxAttempts = 3;

    public async Task<IReadOnlyList<TransportNotificationDeliveryRecord>> DispatchAndListAsync(string tenantId, AuditActor actor, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, TransportNotificationRuleService.ReadPermission, cancellationToken);

        var rules = await ruleRepository.ListForTenantAsync(tenantId, cancellationToken);
        var enabledRules = rules.Where(r => r.Enabled).ToList();
        if (enabledRules.Count > 0)
        {
            var feed = await feedGateway.ListRecentAsync(tenantId, cancellationToken);
            foreach (var rule in enabledRules)
            {
                foreach (var item in feed.Where(i => rule.EventTypes.Contains(i.EventType)))
                {
                    await DispatchOneAsync(tenantId, rule, item, cancellationToken);
                }
            }
        }

        return await auditSink.ListForTenantAsync(tenantId, cancellationToken);
    }

    private async Task DispatchOneAsync(string tenantId, TransportNotificationRule rule, TransportFeedItem item, CancellationToken cancellationToken)
    {
        var claim = ledger.Claim(tenantId, rule.RuleId, item.SourceEventId);
        if (!claim.IsNewClaim)
        {
            return;
        }

        try
        {
            var payload = TransportNotificationPayloadRedactor.Minimize(tenantId, item);
            var attempt = 0;
            NotificationSendResult result;
            do
            {
                attempt++;
                result = await sender.SendAsync(payload, cancellationToken);
            }
            while (result.Outcome == NotificationSendOutcome.TransientFailure && attempt < MaxAttempts);

            var outcome = result.Outcome == NotificationSendOutcome.Delivered
                ? DeliveryAttemptOutcome.Delivered
                : DeliveryAttemptOutcome.DeadLettered;

            var record = new TransportNotificationDeliveryRecord(
                tenantId,
                $"trn-delivery_{Guid.NewGuid():N}",
                rule.RuleId,
                item.SourceEventId,
                payload.PersonId,
                payload.RouteId,
                payload.EventType,
                payload.OccurredAt,
                outcome,
                attempt,
                result.Detail,
                DateTimeOffset.UtcNow);

            await auditSink.TryRecordOnceAsync(record, cancellationToken);
            ledger.Complete(tenantId, rule.RuleId, item.SourceEventId);
        }
        catch
        {
            ledger.Release(tenantId, rule.RuleId, item.SourceEventId);
            throw;
        }
    }

    private async Task RequirePermissionAsync(string tenantId, AuditActor actor, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken))
        {
            throw new PermissionRequiredException(permissionKey);
        }
    }
}
