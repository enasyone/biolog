using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Biolog.Features.TransportEvents.Identifiers;

namespace Biolog.Features.TransportEvents.Demo;

/// <summary>
/// AC05's own bridge: <c>web/features/transport/live.ts</c>'s <c>transportLivePort</c> already
/// POSTs a bare, headerless request to <c>/api/v1/transport/primary-action</c> - BIO-UX-005's own
/// scaffolding, previously answered by BIO-TRN-001's own bridge with a registry SUMMARY
/// (explicitly disclosed there as a placeholder, since boarding/alighting event capture did not
/// exist yet - see that story's own README "Deliberately out of scope"). This story now provides
/// that capability, so this bridge takes over the route (BIO-TRN-001's own handler and route
/// mapping removed - see docs/features/bio-trn-002/README.md "Why this story edits BIO-TRN-001's
/// own endpoint mapping"), answering with a REAL boarding/alighting event capture and the real,
/// freshly-computed <see cref="DerivedTransportPresence"/> for it - a genuine round trip through
/// this story's own domain logic, not a canned string.
///
/// <c>transportAssignRoutePort</c> ("Assign route") is untouched - still answered by BIO-TRN-001's
/// own bridge, which this bridge does not call or duplicate.
///
/// <b>Why a dedicated bridge instead of routing through <see cref="TransportEventService"/>.</b>
/// That service requires a real resolved <see cref="Tenancy.ICurrentTenantProvider.TenantId"/>
/// (Scoped) - a headerless demo POST resolves none. This bridge is registered Singleton and calls
/// the same domain statics the tenant-scoped service calls, against the fixed demo tenant/person
/// set BIO-ATT-001's/BIO-VIS-001's/BIO-TRN-001's own bridges already use, and a real, active demo
/// route/stop/vehicle found-or-created via <see cref="ITransportDemoSeedProvisioner.EnsureDemoSeedAsync"/>
/// (shared with BIO-TRN-001's own bridge by name, not duplicated - see that method's own header;
/// a SEPARATE, Singleton-safe interface from the Scoped <see cref="ITransportRegistryGateway"/>
/// this bridge deliberately does NOT hold, to avoid a captive-dependency bug).
/// </summary>
internal sealed class TransportEventDemoBridgeService(
    ITransportEventRepository eventRepository,
    ITransportEventAuditSink auditSink,
    ITransportDemoSeedProvisioner demoSeedProvisioner)
{
    public const string DemoTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    public const string DemoUiActorId = "svc_transport_events_ui_bridge";

    private static readonly string[] DemoPersonIds =
    [
        "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d",
        "per_0199e0c2-1111-7222-8333-444455556666",
        "per_0199e0c2-2222-7333-9444-555566667777",
    ];

    private int _callCount;

    /// <summary>"View boarding/alighting log" (route-manifest.ts's primary action). Deterministically cycles three demo people, capturing a genuine real Board event for whichever one is due next (idempotent within the same minute-bucket - a rapid repeated call answers with the SAME already-captured event, exactly like the real capture endpoint's own AC02 behaviour), then returns a message describing the REAL, freshly-computed <see cref="DerivedTransportPresence"/> for that capture.</summary>
    public async Task<string> ViewBoardingAlightingLogAsync(CancellationToken cancellationToken)
    {
        var seed = await demoSeedProvisioner.EnsureDemoSeedAsync(DemoTenantId, cancellationToken);

        var callIndex = Interlocked.Increment(ref _callCount);
        var personId = DemoPersonIds[(callIndex - 1) % DemoPersonIds.Length];
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var occurredAt = UtcInstant.Now();
        var idempotencyKey = $"demo-bridge-{personId}-{today}";

        var evidence = new List<TransportEvidenceEntry> { new() { Method = EvidenceMethod.Uhf, Verified = true, Confidence = 0.9 } };
        var fingerprint = TransportEvent.ComputeFingerprint(
            DemoTenantId, personId, seed.RouteId, seed.VehicleId, seed.StopId, null, TransportEventMode.Board, evidence, occurredAt, idempotencyKey);

        var existing = await eventRepository.GetByIdempotencyKeyAsync(DemoTenantId, idempotencyKey, cancellationToken);
        if (existing is null)
        {
            var captured = TransportEvent.Capture(
                DemoTenantId, personId, seed.RouteId, seed.VehicleId, seed.StopId, null, TransportEventMode.Board, evidence, occurredAt, occurredAt, idempotencyKey);
            await eventRepository.CreateAsync(captured, cancellationToken);
            await WriteAuditAsync("transport_event.captured", captured.EventId, cancellationToken);
        }
        else if (existing.ContentFingerprint != fingerprint)
        {
            idempotencyKey = $"demo-bridge-{personId}-{today}-{callIndex}"; // clock rolled over mid-run; mint a fresh key rather than fail the demo route.
            var captured = TransportEvent.Capture(
                DemoTenantId, personId, seed.RouteId, seed.VehicleId, seed.StopId, null, TransportEventMode.Board, evidence, occurredAt, occurredAt, idempotencyKey);
            await eventRepository.CreateAsync(captured, cancellationToken);
            await WriteAuditAsync("transport_event.captured", captured.EventId, cancellationToken);
        }

        var dayEvents = await eventRepository.ListForPersonOnDateAsync(DemoTenantId, personId, today, cancellationToken);
        var presence = TransportPresenceEvaluator.Evaluate(DemoTenantId, personId, today, dayEvents, UtcInstant.Now());

        return $"Transport: boarding/alighting log loaded — {personId} is {presence.Status.ToWireString()} for {today} (live).";
    }

    private async Task WriteAuditAsync(string action, string eventId, CancellationToken cancellationToken)
    {
        var auditEvent = TransportEventAuditEventBuilder.Build(
            DemoTenantId,
            new AuditActor(ActorType.ServiceAccount, DemoUiActorId),
            action,
            new AuditTarget("transport_event", eventId),
            AuditOutcome.Allowed,
            "transport_event_captured_via_ui_bridge",
            CanonicalId.NewEventId(),
            null,
            new Dictionary<string, (object?, string)>());

        await auditSink.WriteAsync(auditEvent, cancellationToken);
    }
}
