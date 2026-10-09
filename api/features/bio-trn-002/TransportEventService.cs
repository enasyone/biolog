using Biolog.Features.TransportEvents.Api;
using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Biolog.Features.TransportEvents.Identifiers;
using Biolog.Features.TransportEvents.Tenancy;

namespace Biolog.Features.TransportEvents;

/// <summary>
/// Application service for AC01 (capture), AC02 (idempotent capture, identical mechanism to
/// BIO-ATT-001's own AC03), AC03 (derived presence, read time only) and AC04 (notification
/// trigger, feature-local - see <see cref="TryTriggerNotificationAsync"/>). Implements ADR-0003
/// Decision 7's audit-atomicity pattern exactly as BIO-ATT-001's own <c>AttendanceEventService</c>.
///
/// <b>Capture and read stay open to any asserted actor (ADR-0006 Decision 2).</b> Board/alight
/// presence data is comparable sensitivity to BIO-ATT-001's own raw attendance events and
/// PERSON's own name/DOB, which stay open throughout this codebase - re-derived, not
/// pattern-matched, from this story's own data (per BIO-ACC-001-QA-01's lesson).
/// </summary>
internal sealed class TransportEventService(
    ICurrentTenantProvider tenantProvider,
    ITransportEventRepository repository,
    ITransportEventAuditSink auditSink,
    TransportEventLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway,
    ITransportRegistryGateway registryGateway,
    ITransportNotificationConfigRepository notificationConfigRepository,
    ITriggeredNotificationLog notificationLog)
{
    public async Task<TransportEvent> CaptureAsync(
        CaptureTransportEventRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!TransportEventModeExtensions.TryParse(request.Mode, out var mode))
        {
            throw TransportEventError.Validation([new ValidationErrorItem("mode", "invalid", "mode must be one of board, alight")]);
        }

        if (request.Evidence is null || request.Evidence.Count == 0)
        {
            throw TransportEventError.Validation([new ValidationErrorItem("evidence", "required", "at least one evidence entry is required")]);
        }

        var evidence = new List<TransportEvidenceEntry>();
        for (var i = 0; i < request.Evidence.Count; i++)
        {
            var wire = request.Evidence[i];
            if (!EvidenceMethodExtensions.TryParse(wire.Method, out var method))
            {
                throw TransportEventError.Validation([new ValidationErrorItem($"evidence[{i}].method", "invalid", "method must be one of uhf, nfc, face, manual")]);
            }

            evidence.Add(new TransportEvidenceEntry { Method = method, Verified = wire.Verified, Confidence = wire.Confidence });
        }

        // Idempotency lock FIRST (AC02), before any gateway/existence validation - the lock itself
        // must serialize on the caller-controlled key regardless of what downstream validation
        // later rejects, the same BIO-ATT-001-own reasoning: two concurrent identical-but-invalid
        // retries could both pass validation before either checks for the other.
        using var _ = await lockRegistry.AcquireAsync(tenantId, request.IdempotencyKey ?? string.Empty, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
        {
            throw TransportEventError.Validation([new ValidationErrorItem("idempotency_key", "invalid", "idempotency_key must be non-empty and at most 200 characters")]);
        }

        if (!await personGateway.ExistsAsync(request.PersonId, cancellationToken))
        {
            throw TransportEventError.PersonNotFound(request.PersonId);
        }

        var route = await registryGateway.GetRouteAsync(request.RouteId, cancellationToken) ?? throw TransportEventError.RouteNotFound(request.RouteId);
        if (!route.IsActive)
        {
            throw TransportEventError.RouteNotActive(request.RouteId);
        }

        if (!await registryGateway.VehicleIsActiveAsync(request.VehicleId, cancellationToken))
        {
            throw TransportEventError.VehicleNotActive(request.VehicleId);
        }

        if (request.StopId is not null)
        {
            if (!route.StopIds.Contains(request.StopId))
            {
                throw TransportEventError.StopNotOnRoute(request.StopId, request.RouteId);
            }

            if (!await registryGateway.StopIsActiveAsync(request.StopId, cancellationToken))
            {
                throw TransportEventError.StopNotActive(request.StopId);
            }
        }

        var occurredAt = request.OccurredAt ?? throw TransportEventError.Validation([new ValidationErrorItem("occurred_at", "required", "occurred_at is required")]);

        // occurred_at's own format is validated inside TransportEvent.Capture below (the same
        // place BIO-ATT-001's own AttendanceEvent.Capture validates it) - not duplicated here.
        var fingerprint = TransportEvent.ComputeFingerprint(
            tenantId, request.PersonId, request.RouteId, request.VehicleId, request.StopId, request.Location, mode, evidence, occurredAt, request.IdempotencyKey);

        var existing = await repository.GetByIdempotencyKeyAsync(tenantId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentFingerprint == fingerprint)
            {
                // AC02: identical redelivery - answered idempotently, no new event, no new audit entry.
                return existing;
            }

            throw TransportEventError.IdempotencyConflict(request.IdempotencyKey);
        }

        var now = UtcInstant.Now();
        var captured = TransportEvent.Capture(
            tenantId, request.PersonId, request.RouteId, request.VehicleId, request.StopId, request.Location, mode, evidence, occurredAt, now, request.IdempotencyKey);

        await AuditAsync(tenantId, actor, "transport_event.captured", captured.EventId, "transport_event_captured", correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["mode"] = (mode.ToWireString(), "internal"),
                ["route_id"] = (request.RouteId, "internal"),
            },
            cancellationToken);

        var result = await repository.CreateAsync(captured, CancellationToken.None);

        // AC04: a fresh capture may reveal (or newly settle) an exception for the day it belongs
        // to - checked and triggered right here, at the one genuine WRITE path this feature has,
        // never from the read-only status endpoint (GET must stay side-effect-free). Best-effort:
        // a notification-trigger failure must never fail the capture itself, the same "producer's
        // fan-out isolated from one consumer's failure" discipline BIO-RPT-001's own
        // IDomainEventBus applies, re-derived here for this feature-local mechanism.
        try
        {
            var date = captured.OccurredAt[..10];
            await MaybeTriggerNotificationAsync(tenantId, captured.PersonId, date, CancellationToken.None);
        }
        catch
        {
            // Best-effort - see comment above.
        }

        return result;
    }

    /// <summary>Read-only, no side effect - AC04's own trigger check runs only from <see cref="CaptureAsync"/>, the one genuine write path (see that method's own comment).</summary>
    public async Task<DerivedTransportPresence> GetPresenceStatusAsync(string personId, string date, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var dayEvents = await repository.ListForPersonOnDateAsync(tenantId, personId, date, cancellationToken);
        return TransportPresenceEvaluator.Evaluate(tenantId, personId, date, dayEvents, UtcInstant.Now());
    }

    public async Task<IReadOnlyList<TransportEvent>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListForPersonOnDateAsync(tenantId, personId, date, cancellationToken);
    }

    public async Task<IReadOnlyList<TransportEvent>> ListForTenantAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListForTenantAsync(tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<TriggeredNotification>> ListTriggeredNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await notificationLog.ListForTenantAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// AC04's own trigger point, called only from <see cref="CaptureAsync"/> (the one genuine
    /// write path): evaluates presence for (tenant, person, date) at the current time and, if it
    /// is <see cref="DerivedPresenceStatus.MissingAlightException"/> AND the tenant's
    /// <see cref="TransportNotificationConfig.NotifyOnMissingAlightException"/> is enabled,
    /// appends a <see cref="TriggeredNotification"/> - deduplicated (at most one per
    /// (tenant, person, event, rule) so re-capturing the same idempotency key never double-fires,
    /// checked against <see cref="ITriggeredNotificationLog"/> itself rather than a separate
    /// dedupe store). A caller proves this end to end by capturing a Board event with an
    /// <c>occurred_at</c> already outside <see cref="TransportPresenceEvaluator.MissingAlightExceptionWindowHours"/>
    /// (a legitimate late/backfilled capture, not a clock trick) - the exception is genuine at
    /// the moment of capture, so the trigger fires immediately, no second call needed.
    /// </summary>
    private async Task MaybeTriggerNotificationAsync(string tenantId, string personId, string date, CancellationToken cancellationToken)
    {
        var dayEvents = await repository.ListForPersonOnDateAsync(tenantId, personId, date, cancellationToken);
        var now = UtcInstant.Now();
        var presence = TransportPresenceEvaluator.Evaluate(tenantId, personId, date, dayEvents, now);

        if (presence.Status != DerivedPresenceStatus.MissingAlightException)
        {
            return;
        }

        var config = await notificationConfigRepository.GetAsync(tenantId, cancellationToken);
        if (!config.NotifyOnMissingAlightException)
        {
            return;
        }

        const string ruleId = "missing_alight_exception";
        var eventId = presence.BasedOnEventIds.Count > 0 ? presence.BasedOnEventIds[0] : personId;

        var alreadyTriggered = await notificationLog.ListForTenantAsync(tenantId, cancellationToken);
        if (alreadyTriggered.Any(n => n.PersonId == personId && n.EventId == eventId && n.RuleId == ruleId))
        {
            return;
        }

        var notification = TriggeredNotification.Create(
            tenantId, personId, eventId, ruleId, $"missing alight for {personId} on {date}", now);
        await notificationLog.AppendAsync(notification, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string eventId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = TransportEventAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("transport_event", eventId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail);

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
