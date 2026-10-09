using System.Collections.Concurrent;

namespace Biolog.Features.CardIssuance.Domain;

/// <summary>
/// Tenant-scoped card-issue storage (BIO-CRD-005-AC01/AC02/AC04). A single, process-lifetime,
/// cross-tenant dictionary standing in for the per-tenant database ADR-0002 Decision 4.1
/// describes - real per-tenant routing is a later story's concern, the same simplification
/// every in-memory repository in this codebase already draws around itself. Not the tenant/
/// control-plane MySQL store the master specification targets (ADR-0005: real schema
/// migrations go through a single Kemi-held lock this story's own conflict group has no
/// standing to start) - scoped so a real, migration-backed implementation is a drop-in
/// replacement behind this interface.
/// </summary>
public interface ICardIssueRepository
{
    /// <summary>
    /// BIO-CRD-005-AC01: the sequence number a NEW card issue for this (tenant, person, card
    /// class) would receive - starts at 1, never reused (AC02). A pure read with no side
    /// effect: <see cref="CardIssueService.IssueAsync"/> calls this, builds the candidate
    /// <see cref="CardIssue"/>, audits it, and only THEN calls <see cref="Create"/> - the same
    /// lock → validate → audit → commit order every other mutation in this feature already
    /// follows (BIO-CRD-005-QA-01, Chioma's acceptance review round 1: the original design
    /// combined sequence assignment with commit in one step, which is exactly the ADR-0003
    /// Decision 7 "commit before audit" violation the caller's own per-(tenant, person, card
    /// class) lock exists to prevent). Safe to call without its own concurrency control only
    /// because every caller holds that lock across the whole read-this-then-<see cref="Create"/>
    /// sequence - a caller that reads this value without holding the lock has a genuine race.
    /// </summary>
    int NextSequenceNumber(string tenantId, string personId, string cardClass);

    /// <summary>
    /// Commits a fully-formed, already-audited <see cref="CardIssue"/> (BIO-CRD-005-QA-01: the
    /// commit half of lock → validate → audit → commit, called only after
    /// <see cref="CardIssueService.AuditAsync"/> has confirmed the issuance event is durable).
    /// Throws if a row already exists at this exact key - unreachable in practice under the
    /// caller's own lock (the same "collision must fail loudly, never silently overwrite"
    /// posture <c>InMemoryPersonRepository.CreateAsync</c> already established for an identical
    /// defensive reason).
    /// </summary>
    CardIssue Create(CardIssue issue);

    CardIssue? Get(string tenantId, string personId, string cardClass, int sequenceNumber);

    /// <summary>Ordered by sequence number ascending. Empty if this (person, card class) has no card issues for this tenant.</summary>
    IReadOnlyList<CardIssue> List(string tenantId, string personId, string cardClass);

    /// <summary>BIO-CRD-005-AC04: the sibling currently holding <see cref="CardIssueStatus.Active"/> for this (person, card class), if any. At most one may exist at a time - this is the read the "prevents two active physical cards" invariant is checked against.</summary>
    CardIssue? GetActive(string tenantId, string personId, string cardClass);

    /// <summary>Compare-and-set replace (ADR-0001 §3.2): succeeds only if the stored row still structurally equals <paramref name="expected"/>. Throws <see cref="CardIssueError.ConcurrencyConflict"/> on a stale write, <see cref="CardIssueError.CardIssueNotFound"/> if the row no longer exists.</summary>
    CardIssue Replace(CardIssue expected, CardIssue updated);
}

internal sealed class InMemoryCardIssueRepository : ICardIssueRepository
{
    private readonly ConcurrentDictionary<string, CardIssue> _byKey = new(StringComparer.Ordinal);

    public int NextSequenceNumber(string tenantId, string personId, string cardClass)
    {
        var existing = List(tenantId, personId, cardClass);
        return existing.Count == 0 ? 1 : existing[^1].SequenceNumber + 1;
    }

    public CardIssue Create(CardIssue issue)
    {
        var key = Key(issue.TenantId, issue.PersonId, issue.CardClass, issue.SequenceNumber);
        if (!_byKey.TryAdd(key, issue))
        {
            // Unreachable in practice - every caller holds this (tenant, person, card class)'s
            // own lock across NextSequenceNumber and Create - but a collision on a computed key
            // must fail loudly, never silently overwrite another card issue.
            throw new CardIssueError(
                $"card issue collision at sequence {issue.SequenceNumber} for {issue.PersonId}/{issue.CardClass}",
                "card_issue_collision", 500,
                new Dictionary<string, object?> { ["person_id"] = issue.PersonId, ["card_class"] = issue.CardClass, ["sequence_number"] = issue.SequenceNumber });
        }

        return issue;
    }

    public CardIssue? Get(string tenantId, string personId, string cardClass, int sequenceNumber) =>
        _byKey.TryGetValue(Key(tenantId, personId, cardClass, sequenceNumber), out var issue) ? issue : null;

    public IReadOnlyList<CardIssue> List(string tenantId, string personId, string cardClass) =>
        [.. _byKey.Values
            .Where(i => i.TenantId == tenantId && i.PersonId == personId && i.CardClass == cardClass)
            .OrderBy(i => i.SequenceNumber)];

    public CardIssue? GetActive(string tenantId, string personId, string cardClass) =>
        _byKey.Values.FirstOrDefault(i =>
            i.TenantId == tenantId && i.PersonId == personId && i.CardClass == cardClass && i.Status == CardIssueStatus.Active);

    public CardIssue Replace(CardIssue expected, CardIssue updated)
    {
        var key = Key(expected.TenantId, expected.PersonId, expected.CardClass, expected.SequenceNumber);

        if (!_byKey.TryGetValue(key, out var current))
        {
            throw CardIssueError.CardIssueNotFound(expected.PersonId, expected.CardClass, expected.SequenceNumber);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_byKey.TryUpdate(key, updated, expected))
        {
            var latest = _byKey.TryGetValue(key, out var p) ? p : current;
            throw CardIssueError.ConcurrencyConflict(expected.Version, latest.Version);
        }

        return updated;
    }

    private static string Key(string tenantId, string personId, string cardClass, int sequenceNumber) =>
        $"{tenantId}\u0000{personId}\u0000{cardClass}\u0000{sequenceNumber}";
}
