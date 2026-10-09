using System.Collections.Concurrent;
using Biolog.Features.VisitorCredentials.Audit;
using Biolog.Features.VisitorCredentials.Domain;
using Biolog.Features.VisitorCredentials.Identifiers;
using Biolog.Features.VisitorCredentials.Tenancy;

namespace Biolog.Features.VisitorCredentials.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Defaults to "every actor holds every permission"; a test that specifically proves the authorization gate constructs this with <paramref name="grantedActorPermissions"/> set.</summary>
public sealed class FakePermissionGateway(IReadOnlySet<(string ActorId, string PermissionKey)>? grantedActorPermissions = null) : IPermissionGateway
{
    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(grantedActorPermissions is null || grantedActorPermissions.Contains((actorId, permissionKey)));
}

/// <summary>A hand-written fake standing in for BIO-VIS-001's own real <c>VisitorRecordService</c> - mirrors its own CheckOutAsync validation (version check, must be checked-in) closely enough to exercise this feature's orchestration without needing a real tenant-scoped host.</summary>
public sealed class FakeVisitorRecordGateway : IVisitorRecordGateway
{
    private readonly Dictionary<string, VisitorRecordView> _records = new(StringComparer.Ordinal);

    /// <summary>BIO-VIS-002-F02's own remediation: proves BIO-VIS-001's own one-way checkout was never called a second time, and proves it was never called at all when a revoke fails first.</summary>
    public int CheckOutCallCount { get; private set; }

    public void Seed(VisitorRecordView view) => _records[view.Id] = view;

    public Task<VisitorRecordView?> GetAsync(string visitorId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.TryGetValue(visitorId, out var v) ? v : null);

    public Task<VisitorRecordView> CheckOutAsync(string visitorId, int? expectedVersion, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
    {
        CheckOutCallCount++;
        if (!_records.TryGetValue(visitorId, out var current))
        {
            throw VisitorCredentialError.VisitorNotFound(visitorId);
        }

        if (expectedVersion != current.Version)
        {
            throw VisitorCredentialError.ConcurrencyConflict(visitorId, expectedVersion ?? -1, current.Version);
        }

        if (current.VisitStatusWire != "checked-in")
        {
            throw new VisitorCredentialError($"check-out blocked: visit is {current.VisitStatusWire}, not checked-in", "not_checked_in", 409);
        }

        var updated = current with { VisitStatusWire = "checked-out", CheckedOutAt = UtcInstant.Now(), Version = current.Version + 1 };
        _records[visitorId] = updated;
        return Task.FromResult(updated);
    }
}

/// <summary>A hand-written fake standing in for BIO-CRD-005's own real <c>CardIssueService</c> - mints a fresh sequence number per issuance and tracks Issued/Active/Deactivated transitions closely enough to exercise this feature's own grant/revoke orchestration.</summary>
public sealed class FakeCardIssuanceGateway : ICardIssuanceGateway
{
    private readonly Dictionary<(string PersonId, string CardClass, int SequenceNumber), CardIssueView> _cards = new();
    private int _nextSequenceNumber = 1;
    public int IssueCallCount { get; private set; }
    public int ActivateCallCount { get; private set; }
    public int DeactivateCallCount { get; private set; }

    /// <summary>BIO-VIS-002-F02's own remediation test hook: simulates the revoke step's own card-deactivation call failing, one-shot (cleared after it fires once).</summary>
    public bool ThrowOnNextDeactivate { get; set; }

    /// <summary>BIO-VIS-002-F04's own remediation test hook: unlike <see cref="ThrowOnNextDeactivate"/>, this is NOT one-shot - every <see cref="DeactivateAsync"/> call throws while set, simulating the compensating deactivate being persistently unavailable (not just failing once).</summary>
    public bool AlwaysThrowOnDeactivate { get; set; }

    /// <summary>
    /// BIO-VIS-002-F03's own remediation test hook (Seyi's round-2 FAIL): awaited once, the FIRST
    /// time <see cref="IssueAsync"/> is called across the whole fake's lifetime - lets a
    /// concurrency test force a genuine async suspension point mid-grant, deterministically
    /// proving the person-scoped lock actually blocks a second visitor's grant attempt rather
    /// than merely "usually" not racing.
    /// </summary>
    public Func<Task>? BeforeFirstIssueAsync { get; set; }
    private bool _beforeFirstIssueAsyncFired;

    public Task<IReadOnlyList<CardIssueView>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CardIssueView> matches = [.. _cards.Values
            .Where(c => c.PersonId == personId && c.CardClass == cardClass)
            .OrderBy(c => c.SequenceNumber)];
        return Task.FromResult(matches);
    }

    public Task<CardIssueView?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cards.TryGetValue((personId, cardClass, sequenceNumber), out var v) ? v : null);

    public async Task<CardIssueView> IssueAsync(string personId, string cardClass, string templateRef, string printerRef, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
    {
        if (BeforeFirstIssueAsync is not null && !_beforeFirstIssueAsyncFired)
        {
            _beforeFirstIssueAsyncFired = true;
            await BeforeFirstIssueAsync();
        }

        IssueCallCount++;
        var sequenceNumber = _nextSequenceNumber++;
        var view = new CardIssueView { PersonId = personId, CardClass = cardClass, SequenceNumber = sequenceNumber, StatusWire = "issued", Version = 1 };
        _cards[(personId, cardClass, sequenceNumber)] = view;
        return view;
    }

    public Task<CardIssueView> ActivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, string idempotencyKey, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
    {
        ActivateCallCount++;
        var current = _cards[(personId, cardClass, sequenceNumber)];
        if (current.Version != expectedVersion)
        {
            throw new VisitorCredentialError("card concurrency conflict", "concurrency_conflict", 409);
        }

        var updated = current with { StatusWire = "active", Version = current.Version + 1 };
        _cards[(personId, cardClass, sequenceNumber)] = updated;
        return Task.FromResult(updated);
    }

    public Task<CardIssueView> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
    {
        DeactivateCallCount++;
        if (AlwaysThrowOnDeactivate)
        {
            throw new InvalidOperationException("injected: card deactivation is persistently unavailable");
        }

        if (ThrowOnNextDeactivate)
        {
            ThrowOnNextDeactivate = false;
            throw new InvalidOperationException("injected: card deactivation failed");
        }

        var current = _cards[(personId, cardClass, sequenceNumber)];
        if (current.Version != expectedVersion)
        {
            throw new VisitorCredentialError("card concurrency conflict", "concurrency_conflict", 409);
        }

        var updated = current with { StatusWire = "deactivated", Version = current.Version + 1 };
        _cards[(personId, cardClass, sequenceNumber)] = updated;
        return Task.FromResult(updated);
    }
}

/// <summary>
/// BIO-VIS-002-F01's own remediation test hook: wraps the real
/// <see cref="InMemoryVisitorCredentialRepository"/> and can inject a one-shot failure on the
/// specific Pending-&gt;Granted final-commit <see cref="ReplaceAsync"/> call - the exact
/// confirmed-final-commit-failure-after-the-card-is-active scenario the review found. Every other
/// call passes straight through to the real in-memory store, so this is otherwise
/// behaviourally identical to it.
/// </summary>
internal sealed class FaultInjectingVisitorCredentialRepository(IVisitorCredentialRepository inner) : IVisitorCredentialRepository
{
    public bool ThrowOnNextPendingToGrantedReplace { get; set; }

    /// <summary>
    /// BIO-VIS-002-F04's own remediation test hook: unlike <see cref="ThrowOnNextPendingToGrantedReplace"/>,
    /// this is NOT one-shot - simulates this row's own local writes being genuinely, persistently
    /// unavailable (not just failing once), on BOTH the paths <c>GrantAsync</c> uses to try to make
    /// a card's identity locally discoverable: the early Pending-&gt;Pending card-correlation attach
    /// (<c>VisitorCredentialService.AttachCardSequenceAsync</c>) AND the later
    /// Pending-&gt;Failed fallback. A test that forces BOTH of these AND the compensating
    /// deactivate (<see cref="FakeCardIssuanceGateway.AlwaysThrowOnDeactivate"/>) to fail is
    /// exercising the exact pathological scenario <see cref="IStrandedCardLedger"/> exists for.
    /// The write's own ambiguous-failure rereads still pass straight through to the real store
    /// unmodified - they correctly observe the write never landed, so there is no need to also
    /// fault-inject any read.
    /// </summary>
    public bool AlwaysThrowOnPendingCardAttach { get; set; }

    public Task<VisitorCredential> CreateAsync(VisitorCredential credential, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(credential, cancellationToken);

    public Task<VisitorCredential?> GetAsync(string tenantId, string credentialId, CancellationToken cancellationToken = default) =>
        inner.GetAsync(tenantId, credentialId, cancellationToken);

    public Task<VisitorCredential?> GetActiveForVisitorAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken = default) =>
        inner.GetActiveForVisitorAsync(tenantId, visitorRecordId, cancellationToken);

    public Task<IReadOnlyList<VisitorCredential>> ListForVisitorAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken = default) =>
        inner.ListForVisitorAsync(tenantId, visitorRecordId, cancellationToken);

    public Task<VisitorCredential?> FindByCardAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default) =>
        inner.FindByCardAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

    public Task<VisitorCredential> ReplaceAsync(VisitorCredential expectedCurrent, VisitorCredential updated, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextPendingToGrantedReplace
            && expectedCurrent.Status == VisitorCredentialStatus.Pending
            && updated.Status == VisitorCredentialStatus.Granted)
        {
            ThrowOnNextPendingToGrantedReplace = false;
            throw new InvalidOperationException("injected: local credential commit failed after the card was activated");
        }

        if (AlwaysThrowOnPendingCardAttach
            && expectedCurrent.Status == VisitorCredentialStatus.Pending
            && (updated.Status == VisitorCredentialStatus.Pending || updated.Status == VisitorCredentialStatus.Failed))
        {
            throw new InvalidOperationException("injected: this pending credential row's own local writes are persistently unavailable");
        }

        return inner.ReplaceAsync(expectedCurrent, updated, cancellationToken);
    }
}

/// <summary>BIO-VIS-002-F07's own remediation test hook (Seyi round-7 FAIL / Zainab round-5 FAIL, both independently): simulates the reconciliation audit write being genuinely unavailable, one-shot (cleared after it fires once) - <see cref="InMemoryVisitorCredentialAuditSink.ExistsAsync"/> genuinely returns false while armed, so this is a real failure, never an ambiguous one.</summary>
internal sealed class FaultInjectingVisitorCredentialAuditSink(InMemoryVisitorCredentialAuditSink inner) : IVisitorCredentialAuditSink
{
    public bool ThrowOnNextWrite { get; set; }

    public Task WriteAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextWrite)
        {
            ThrowOnNextWrite = false;
            throw new InvalidOperationException("injected: reconciliation audit write failed");
        }

        return inner.WriteAsync(auditEvent, cancellationToken);
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        inner.ExistsAsync(eventId, cancellationToken);

    /// <summary>BIO-VIS-002-F08's own remediation: reconciliation's stable-id audit write now goes
    /// through <see cref="CreateIfAbsentAsync"/>, not <see cref="WriteAsync"/> - the same
    /// <see cref="ThrowOnNextWrite"/> flag must gate this path too, so the round-7 failure-
    /// injection test keeps exercising a genuine failure instead of silently no-op'ing.</summary>
    public Task<VisitorCredentialAuditEvent> CreateIfAbsentAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextWrite)
        {
            ThrowOnNextWrite = false;
            throw new InvalidOperationException("injected: reconciliation audit write failed");
        }

        return inner.CreateIfAbsentAsync(auditEvent, cancellationToken);
    }
}

/// <summary>
/// BIO-VIS-002-F09's own remediation test hook (Seyi round-9 FAIL): a test built on a single
/// start-of-task signal (round 8's original shape) lets the scheduler run caller A fully through
/// <see cref="CreateIfAbsentAsync"/> before caller B even starts - meaning the OLD defective
/// <c>ExistsAsync</c>-then-<c>WriteAsync</c> sequence could pass that test by accident whenever the
/// two tasks happened to run close enough to sequentially. This decorator forces both racing
/// callers to reach the real contested call back-to-back by blocking each one, right at the create
/// boundary, until BOTH have arrived - a <see cref="Barrier"/> with exactly 2 participants, not a
/// single shared gate. The old racy sequence could still lose this race here (both observe absent,
/// both write, one silently clobbers the other); the real atomic
/// <see cref="InMemoryVisitorCredentialAuditSink.CreateIfAbsentAsync"/> cannot.
/// </summary>
internal sealed class BarrierGatedVisitorCredentialAuditSink(IVisitorCredentialAuditSink inner, Barrier barrier) : IVisitorCredentialAuditSink
{
    public Task WriteAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(auditEvent, cancellationToken);

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        inner.ExistsAsync(eventId, cancellationToken);

    public async Task<VisitorCredentialAuditEvent> CreateIfAbsentAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        // Both callers block here until BOTH have arrived, then are released together, right
        // before the real contested call - no slack for the scheduler to serialize them.
        barrier.SignalAndWait(cancellationToken);
        return await inner.CreateIfAbsentAsync(auditEvent, cancellationToken);
    }
}

/// <summary>
/// BIO-VIS-002-F09's own mutation/differential-test reference, round 2 (Seyi round-10 FAIL on top
/// of round-9's <see cref="BarrierGatedVisitorCredentialAuditSink"/>): that decorator's barrier
/// only guarantees both callers reach the wrapper before EITHER enters the inner create call - it
/// does not control what happens afterward. The scheduler can still run caller A's entire inner
/// create to completion before caller B even starts its own, and the OLD defective
/// <c>ExistsAsync</c>-then-<c>WriteAsync</c> sequence would ALSO pass under that exact serialized
/// interleaving. This pair of reference sinks (this one and <see cref="RacyCheckThenWriteSink"/>)
/// exists purely to give the test real discriminating power, per god's exact direction: move the
/// barrier INTO the factory delegate passed to <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/>
/// itself. .NET's own documented contract for that overload is that under genuine two-thread
/// contention on the same key, the factory MAY be invoked on BOTH threads before one result is
/// kept and the other discarded - it does NOT serialize factory invocation the way a lock would.
/// Placing the barrier inside the factory therefore forces exactly the sequence Seyi asked for:
/// neither thread's factory can return - and so neither candidate can be stored OR discarded -
/// until BOTH have been invoked (i.e. both have "observed absent" by virtue of being asked to
/// produce a value at all). This is the real atomic primitive the production
/// <see cref="InMemoryVisitorCredentialAuditSink.CreateIfAbsentAsync"/> is built on
/// (<c>ConcurrentDictionary.GetOrAdd</c>), driven through its documented worst case instead of its
/// common case.
/// </summary>
internal sealed class AtomicFactoryRaceSink(Barrier factoryEntryBarrier)
{
    private readonly ConcurrentDictionary<string, VisitorCredentialAuditEvent> _store = new();

    public VisitorCredentialAuditEvent CreateIfAbsent(string eventId, VisitorCredentialAuditEvent candidate) =>
        _store.GetOrAdd(eventId, _ =>
        {
            factoryEntryBarrier.SignalAndWait();
            return candidate;
        });

    public VisitorCredentialAuditEvent? Get(string eventId) => _store.TryGetValue(eventId, out var value) ? value : null;
}

/// <summary>
/// BIO-VIS-002-F09's own mutation/differential-test reference: deliberately REIMPLEMENTS the
/// historical defective algorithm this entire F07/F08/F09 chain fixed - an explicit absence check,
/// then a plain, unconditional overwrite - so the SAME harness and assertions that must pass
/// against <see cref="AtomicFactoryRaceSink"/> can be proven to FAIL against this one. That is the
/// whole point: a test that can't tell the two apart has no discriminating power, no matter how
/// correct the real production code happens to be. <see cref="CreateIfAbsent"/> blocks on
/// <paramref name="checkBarrier"/> only AFTER completing its own absence check - mirroring Seyi's
/// own words exactly ("both callers must complete the absence observation before either write is
/// permitted; then release both writes") - then an optional pair of semaphores lets the caller
/// deterministically force which write happens second, proving THAT write silently discards the
/// first even though both callers legitimately observed "absent".
/// </summary>
internal sealed class RacyCheckThenWriteSink(Barrier checkBarrier)
{
    private readonly Dictionary<string, VisitorCredentialAuditEvent> _store = new();
    private readonly Lock _lock = new();

    public VisitorCredentialAuditEvent CreateIfAbsent(
        string eventId, VisitorCredentialAuditEvent candidate, SemaphoreSlim? waitBeforeWrite = null, SemaphoreSlim? signalAfterWrite = null)
    {
        lock (_lock)
        {
            _ = _store.ContainsKey(eventId); // the historical "absence observation" - both callers do this before either may write.
        }

        checkBarrier.SignalAndWait();
        waitBeforeWrite?.Wait();

        lock (_lock)
        {
            _store[eventId] = candidate; // the historical bug: unconditional overwrite, never a durable create-once.
        }

        signalAfterWrite?.Release();
        return candidate;
    }

    public VisitorCredentialAuditEvent? Get(string eventId)
    {
        lock (_lock)
        {
            return _store.TryGetValue(eventId, out var value) ? value : null;
        }
    }
}
