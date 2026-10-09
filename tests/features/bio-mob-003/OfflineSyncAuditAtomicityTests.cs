// Developer tests for BIO-MOB-003. SYNTHETIC fixtures only.
//
// Regression coverage for BIO-MOB-003 round 4 (Seyi): the original IntakeAsync committed the
// aggregate version ledger's advance via versionLedger.Apply(...) BEFORE calling AuditAsync, so
// a confirmed audit failure left the ledger advanced with no way to undo it - a retry of the
// exact same (never actually accepted) mutation then received a false concurrency_conflict
// forever. Fixed by splitting the ledger into Peek (a pure read) and Commit (the write), with
// AuditAsync run in between while a per-aggregate lock is held - see
// OfflineMutationIntakeService.IntakeAsync's own comment. Mirrors
// tests/features/bio-crd-005/CardIssueAuditAtomicityTests.cs's exact shape for the identical
// ADR-0003 Decision 7 defect class (BIO-CRD-005-QA-01).
using Biolog.Features.OfflineSync.Api;
using Biolog.Features.OfflineSync.Audit;
using Biolog.Features.OfflineSync.Domain;
using Biolog.Features.OfflineSync.Identifiers;
using Xunit;

namespace Biolog.Features.OfflineSync.Tests;

public sealed class OfflineSyncAuditAtomicityTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string AggregateId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    private sealed class AlwaysFailingAuditSink : IOfflineSyncAuditSink
    {
        public Task WriteAsync(OfflineSyncAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("audit offline");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false); // confirms the write genuinely never persisted, not merely ambiguous
    }

    private static ResolvedDevice Device() => new(DeviceId, TenantId);

    private static AuditActor Operator() => new(ActorType.Device, DeviceId);

    private static OfflineMutationDeltaRequest Delta(string mutationId, int baseVersion, string idempotencyKey, string operation = "ADDITION") => new()
    {
        MutationId = mutationId,
        TenantId = TenantId,
        DeviceId = DeviceId,
        AggregateId = AggregateId,
        BaseVersion = baseVersion,
        IdempotencyKey = idempotencyKey,
        OccurredAt = "2026-09-23T00:00:00.000Z",
        Operation = operation,
        Payload = System.Text.Json.JsonDocument.Parse("""{"field":"SYNTHETIC"}""").RootElement,
    };

    private static string NewMutationId() => $"evt_{Guid.CreateVersion7():D}";

    [Fact]
    public async Task BIOMOB003_round4_a_confirmed_audit_failure_leaves_the_version_ledger_untouched()
    {
        var ledger = new InMemoryAggregateVersionLedger();
        var service = new OfflineMutationIntakeService(
            new InMemoryIdempotencyStore(), ledger, new AlwaysFailingAuditSink(), new OfflineMutationLockRegistry());

        var mutationId = NewMutationId();
        var error = await Assert.ThrowsAsync<OfflineSyncError>(() =>
            service.IntakeAsync(Device(), Delta(mutationId, 1, $"idem-{mutationId}"), $"idem-{mutationId}", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Null(ledger.Peek(TenantId, AggregateId)); // never committed - still as if nothing was ever attempted
    }

    [Fact]
    public async Task BIOMOB003_round4_retrying_the_identical_mutation_after_a_confirmed_audit_failure_succeeds_not_a_false_conflict()
    {
        // Same ledger + idempotency store shared across both service instances, mirroring how a
        // real outage strikes mid-request and then clears before the client's own retry - only
        // the audit sink differs between the failing attempt and the recovering one.
        var ledger = new InMemoryAggregateVersionLedger();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var lockRegistry = new OfflineMutationLockRegistry();

        var failingService = new OfflineMutationIntakeService(idempotencyStore, ledger, new AlwaysFailingAuditSink(), lockRegistry);
        var mutationId = NewMutationId();
        var idempotencyKey = $"idem-{mutationId}";
        await Assert.ThrowsAsync<OfflineSyncError>(() =>
            failingService.IntakeAsync(Device(), Delta(mutationId, 1, idempotencyKey), idempotencyKey, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        var recoveredService = new OfflineMutationIntakeService(idempotencyStore, ledger, new InMemoryOfflineSyncAuditSink(), lockRegistry);
        var (result, replayed) = await recoveredService.IntakeAsync(
            Device(), Delta(mutationId, 1, idempotencyKey), idempotencyKey, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.False(replayed); // a genuine retry, not a replay of a stored success (there was none)
        Assert.Equal(1, result.Version); // base_version 1 was still valid - not falsely bumped to a phantom 2
    }

    [Fact]
    public async Task A_successful_intake_still_commits_normally_when_the_audit_sink_works()
    {
        var ledger = new InMemoryAggregateVersionLedger();
        var service = new OfflineMutationIntakeService(
            new InMemoryIdempotencyStore(), ledger, new InMemoryOfflineSyncAuditSink(), new OfflineMutationLockRegistry());

        var mutationId = NewMutationId();
        var (result, replayed) = await service.IntakeAsync(
            Device(), Delta(mutationId, 1, $"idem-{mutationId}"), $"idem-{mutationId}", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.False(replayed);
        Assert.Equal(1, result.Version);
        Assert.Equal(1, ledger.Peek(TenantId, AggregateId));
    }
}
