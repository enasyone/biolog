namespace Biolog.Features.OfflineSync.Domain;

/// <summary>
/// ADR-0001 §3.2 (compare-and-set) / §3.5 (offline sync conflicts), applied generically: this
/// queue is infrastructure for whatever domain aggregate a future story attaches to it (no
/// story defines one yet - see BIO-MOB-002's own "Known gaps"), so this ledger tracks each
/// aggregate's version purely by <c>(tenant_id, aggregate_id)</c>, with no domain-specific
/// meaning attached to what an "aggregate" here actually represents.
///
/// <b>Deliberately read/write split, not a single atomic "apply".</b> BIO-MOB-003-round-4 (Seyi):
/// an earlier version of this interface exposed one <c>Apply</c> method that checked AND
/// committed the new version in the same call, called before the audit write. When the audit
/// then failed, there was no way to undo the commit - the ledger stayed advanced, and a
/// legitimate retry of the identical (never-actually-accepted) mutation received a false
/// <c>concurrency_conflict</c> forever, permanently losing it. <see cref="Peek"/> is read-only;
/// <see cref="Commit"/> is a separate, unconditional write. <see cref="OfflineMutationIntakeService"/>
/// holds a per-(tenant, aggregate) lock (<c>OfflineMutationLockRegistry</c>) across the whole
/// peek -> audit -> commit sequence, the same discipline <c>CardIssueService.IssueAsync</c>
/// already established in this codebase (BIO-CRD-005-QA-01: compute in memory, audit, and only
/// commit once the audit write is confirmed durable) - nothing about this ledger's state ever
/// changes until the audit succeeds, so there is no commit to roll back and no false-conflict
/// window to reason about.
///
/// <b>The first-mutation rule is this story's own explicit, flagged choice</b> (the same
/// treatment as BIO-MOB-002's retry-policy constants and BIO-MOB-003's own endpoint route): no
/// ADR or contract defines how a brand-new, never-before-seen aggregate enters this generic
/// ledger, and <c>offline-mutation.schema.json#/properties/base_version</c>'s own
/// <c>minimum: 1</c> means a client can never legitimately claim "version 0" to signal
/// creation. Absent a real domain owner to say otherwise, the first mutation this ledger ever
/// sees for a given aggregate (<see cref="Peek"/> returns <see langword="null"/>) is accepted
/// unconditionally (there is nothing for it to conflict with yet), and the caller commits the
/// client's own claimed <c>base_version</c> **as-is** - not <c>base_version + 1</c> - as the
/// resulting version (a creating client conventionally claims <c>base_version: 1</c>, matching
/// ADR-0001 §3.1's "starting at 1 on creation"). Every later mutation for that same aggregate is
/// then a normal compare-and-set: its own <c>base_version</c> must equal whatever
/// <see cref="Peek"/> currently returns to be accepted, and only then does the caller commit
/// exactly one version higher. Concretely: an addition claims <c>base_version: 1</c> (ledger
/// becomes 1); the very next mutation for that aggregate must also claim <c>base_version: 1</c>
/// to match (ledger becomes 2), not 2. A future story that attaches real domain semantics to a
/// specific aggregate type may need to replace this generic rule with one grounded in that
/// domain's own creation event.
/// </summary>
public interface IAggregateVersionLedger
{
    /// <summary>The ledger's current version for <c>(tenantId, aggregateId)</c>, or <see langword="null"/> if this aggregate has never had a mutation committed. Read-only - never mutates state.</summary>
    int? Peek(string tenantId, string aggregateId);

    /// <summary>Unconditionally sets the ledger's version for <c>(tenantId, aggregateId)</c> to <paramref name="newVersion"/>. Callers must only call this while holding that aggregate's lock, and only after independently confirming (via a prior <see cref="Peek"/> under the same lock, plus a successful audit write) that this commit is safe to make durable.</summary>
    void Commit(string tenantId, string aggregateId, int newVersion);
}

/// <summary>In-memory, process-lifetime version ledger. <see cref="ConcurrentDictionary{TKey,TValue}"/> guards the dictionary itself against concurrent access from different aggregates running in parallel; the caller's per-aggregate lock (not this class) is what makes a <see cref="Peek"/>-then-<see cref="Commit"/> pair for the SAME aggregate race-free.</summary>
internal sealed class InMemoryAggregateVersionLedger : IAggregateVersionLedger
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string TenantId, string AggregateId), int> _versions = new();

    public int? Peek(string tenantId, string aggregateId) =>
        _versions.TryGetValue((tenantId, aggregateId), out var current) ? current : null;

    public void Commit(string tenantId, string aggregateId, int newVersion) =>
        _versions[(tenantId, aggregateId)] = newVersion;
}
