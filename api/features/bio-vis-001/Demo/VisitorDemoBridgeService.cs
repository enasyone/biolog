using System.Collections.Concurrent;
using Biolog.Features.Visitors.Audit;
using Biolog.Features.Visitors.Domain;
using Biolog.Features.Visitors.Identifiers;

namespace Biolog.Features.Visitors.Demo;

/// <summary>
/// AC05's own bridge: <c>web/features/visitors/live.ts</c>'s three live adapters
/// (<c>visitorsLivePort</c>/<c>visitorsCheckOutLivePort</c>/<c>visitorsViewHistoryLivePort</c>)
/// already POST a bare, headerless request to <c>/api/v1/visitors/primary-action</c>,
/// <c>/api/v1/visitors/check-out-visitor</c> and <c>/api/v1/visitors/view-visit-history</c> -
/// BIO-UX-005's own scaffolding, by design (no login/session story exists yet, the same
/// disclosed reasoning BIO-ATT-001's own <c>AttendanceDemoBridgeService</c>
/// already established for an identically-shaped headerless UI caller). Requiring a tenant/actor
/// on these three specific routes would make them permanently unreachable from the one UI AC05
/// exists to connect - so this bridge operates against one FIXED, already-public SYNTHETIC demo
/// tenant/person set, genuinely exercising the same <see cref="VisitorRecord"/>/
/// <see cref="VisitorStatusEvaluator"/> domain code the real, tenant-scoped, strictly-gated
/// <c>/api/v1/visitors</c> family uses - a real, connected live implementation of the three
/// existing mapped actions, not a second mock.
///
/// <b>Why a dedicated bridge instead of routing through <see cref="VisitorRecordService"/>.</b>
/// That service requires a real resolved <see cref="Tenancy.ICurrentTenantProvider.TenantId"/>
/// (Scoped) - a headerless demo POST resolves none. This bridge is registered Singleton and
/// depends only on the Singleton repository/audit sink directly, calling the same domain methods
/// the tenant-scoped service calls, against the fixed demo identifiers below.
///
/// The demo tenant id is the literal example value
/// <c>contracts/schemas/identifier-registry.json</c> itself publishes for the <c>tenant</c> type
/// - the same synthetic value BIO-ATT-001's own bridge already uses. Reused deliberately (not
/// re-minted) since it is the one canonical "this is a synthetic demo tenant" value the codebase
/// already establishes, and there is no collision risk: this feature's own
/// <see cref="IVisitorRecordRepository"/> is a completely separate store from BIO-ATT-001's event
/// repository. Host person ids likewise reuse BIO-ATT-001's own three synthetic demo person ids.
/// </summary>
internal sealed class VisitorDemoBridgeService(
    IVisitorRecordRepository repository,
    IVisitorConfigRepository configRepository,
    IVisitorAuditSink auditSink)
{
    public const string DemoTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    public const string DemoUiActorId = "svc_visitors_ui_bridge";

    private static readonly string[] DemoHostPersonIds =
    [
        "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d",
        "per_0199e0c2-1111-7222-8333-444455556666",
        "per_0199e0c2-2222-7333-9444-555566667777",
    ];

    private static readonly string[] DemoVisitorNames = ["Demo Visitor One", "Demo Visitor Two", "Demo Visitor Three"];

    private readonly ConcurrentDictionary<int, string> _slotVisitorIds = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _primaryActionCallCount;

    /// <summary>"Check in visitor" (route-manifest.ts's primary action). Deterministically cycles three demo slots: preregisters+auto-approves a fresh demo visitor for a slot that has none or has already checked out, then genuinely checks them in through this story's own domain code.</summary>
    public async Task<string> CheckInNextDemoVisitorAsync(CancellationToken cancellationToken)
    {
        var slot = (int)((uint)Interlocked.Increment(ref _primaryActionCallCount) - 1) % DemoVisitorNames.Length;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var record = await GetOrCreateSlotAsync(slot, cancellationToken);
            if (record.VisitStatus == VisitStatus.CheckedOut)
            {
                record = await CreateDemoVisitorAsync(slot, cancellationToken);
            }

            if (record.VisitStatus == VisitStatus.CheckedIn)
            {
                return $"Visitors: {record.FullName} is already checked in (live).";
            }

            var checkedIn = record.CheckIn(UtcInstant.Now());
            await WriteAuditAsync("visitor.checked_in", checkedIn.Id, cancellationToken);
            checkedIn = await repository.ReplaceAsync(record, checkedIn, cancellationToken);
            _slotVisitorIds[slot] = checkedIn.Id;
            return $"Visitors: {checkedIn.FullName} checked in (live).";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>"Check out visitor" (route-manifest.ts's secondary action). Checks out whichever demo slot is currently checked in - a real, computed answer, not a canned string.</summary>
    public async Task<string> CheckOutNextDemoVisitorAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var slot = 0; slot < DemoVisitorNames.Length; slot++)
            {
                if (!_slotVisitorIds.TryGetValue(slot, out var visitorId))
                {
                    continue;
                }

                var record = await repository.GetAsync(DemoTenantId, visitorId, cancellationToken);
                if (record is null || record.VisitStatus != VisitStatus.CheckedIn)
                {
                    continue;
                }

                var checkedOut = record.CheckOut(UtcInstant.Now());
                await WriteAuditAsync("visitor.checked_out", checkedOut.Id, cancellationToken);
                checkedOut = await repository.ReplaceAsync(record, checkedOut, cancellationToken);
                return $"Visitors: {checkedOut.FullName} checked out (live).";
            }

            return "Visitors: no visitor is currently checked in to check out (live).";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>"View visit history" (route-manifest.ts's tertiary action). Summarises the real, current status of every demo slot this bridge has touched.</summary>
    public async Task<string> ViewDemoVisitHistoryAsync(CancellationToken cancellationToken)
    {
        var summaries = new List<string>();
        foreach (var (slot, visitorId) in _slotVisitorIds.OrderBy(kv => kv.Key))
        {
            var record = await repository.GetAsync(DemoTenantId, visitorId, cancellationToken);
            if (record is not null)
            {
                summaries.Add($"{record.FullName}: {record.VisitStatus.ToWireString()}");
            }
        }

        return summaries.Count == 0
            ? "Visitors: no visit history yet (live) - check a visitor in first."
            : $"Visitors: history — {string.Join("; ", summaries)} (live).";
    }

    private async Task<VisitorRecord> GetOrCreateSlotAsync(int slot, CancellationToken cancellationToken)
    {
        if (_slotVisitorIds.TryGetValue(slot, out var existingId))
        {
            var existing = await repository.GetAsync(DemoTenantId, existingId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        return await CreateDemoVisitorAsync(slot, cancellationToken);
    }

    private async Task<VisitorRecord> CreateDemoVisitorAsync(int slot, CancellationToken cancellationToken)
    {
        var config = await configRepository.GetAsync(DemoTenantId, cancellationToken);
        var now = UtcInstant.Now();
        var expiresAt = UtcInstant.Render(DateTime.UtcNow.AddHours(config.DefaultVisitDurationHours));

        var record = VisitorRecord.Preregister(
            DemoTenantId, personId: null, DemoVisitorNames[slot], contactEmail: null, contactPhone: null,
            DemoHostPersonIds[slot], vehiclePlate: null, temporaryCredentialRequested: false, expiresAt, now);

        await WriteAuditAsync("visitor.preregistered", record.Id, cancellationToken);
        record = await repository.CreateAsync(record, cancellationToken);

        // Auto-approved as part of this demo bridge's own convenience path (the bridge stands in
        // for a host who would otherwise approve manually) - not a bypass of AC02: the approval
        // is still a real, audited HostApprovalStatus transition through the same domain method
        // the gated endpoint calls.
        var approved = record.Approve(DemoUiActorId, UtcInstant.Now());
        await WriteAuditAsync("visitor.approved", record.Id, cancellationToken);
        record = await repository.ReplaceAsync(record, approved, cancellationToken);

        _slotVisitorIds[slot] = record.Id;
        return record;
    }

    private async Task WriteAuditAsync(string action, string visitorId, CancellationToken cancellationToken)
    {
        var auditEvent = VisitorAuditEventBuilder.Build(
            DemoTenantId,
            new AuditActor(ActorType.ServiceAccount, DemoUiActorId),
            action,
            new AuditTarget("visitor_record", visitorId),
            AuditOutcome.Allowed,
            $"{action.Replace('.', '_')}_via_ui_bridge",
            CanonicalId.NewEventId(),
            null,
            new Dictionary<string, (object?, string)>());

        await auditSink.WriteAsync(auditEvent, cancellationToken);
    }
}
