using System.Collections.Concurrent;

namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// Tenant-scoped card-lifecycle-record storage. A single, process-lifetime, cross-tenant
/// dictionary standing in for the per-tenant database ADR-0002 Decision 4.1 describes - the
/// same simplification every in-memory repository in this codebase already draws around
/// itself (ADR-0005: real schema migrations go through a single Kemi-held lock this story's
/// own conflict group has no standing to start).
/// </summary>
public interface ICardLifecycleRepository
{
    CardLifecycleRecord? Get(string tenantId, string personId, string cardClass, int sequenceNumber);

    /// <summary>Throws if a row already exists at this exact key - a first-ever report only.</summary>
    CardLifecycleRecord Create(CardLifecycleRecord record);

    /// <summary>Compare-and-set replace (ADR-0001 §3.2): succeeds only if the stored row still structurally equals <paramref name="expected"/>.</summary>
    CardLifecycleRecord Replace(CardLifecycleRecord expected, CardLifecycleRecord updated);

    /// <summary>
    /// AC05's deny-list feed: every record currently <see cref="CardLockState.Locked"/>,
    /// <see cref="CardLockState.RevocationPending"/> (BIO-CRD-006-QA-03: deny conservatively
    /// while a sweep's own activation has not yet been confirmed) or
    /// <see cref="CardLockState.PermanentlyRevoked"/> for this tenant - all three states must
    /// deny presentation. Optionally filtered to records touched at or after <paramref name="since"/>
    /// (RFC 3339), so an offline reader can pull only the delta since its last sync rather than
    /// the whole tenant's history every time.
    /// </summary>
    IReadOnlyList<CardLifecycleRecord> ListDenyList(string tenantId, string? since);
}

internal sealed class InMemoryCardLifecycleRepository : ICardLifecycleRepository
{
    private readonly ConcurrentDictionary<string, CardLifecycleRecord> _byKey = new(StringComparer.Ordinal);

    public CardLifecycleRecord? Get(string tenantId, string personId, string cardClass, int sequenceNumber) =>
        _byKey.TryGetValue(Key(tenantId, personId, cardClass, sequenceNumber), out var record) ? record : null;

    public CardLifecycleRecord Create(CardLifecycleRecord record)
    {
        var key = Key(record.TenantId, record.PersonId, record.CardClass, record.SequenceNumber);
        if (!_byKey.TryAdd(key, record))
        {
            throw new CardLifecycleError(
                $"card lifecycle record collision for {record.PersonId}/{record.CardClass}/{record.SequenceNumber}",
                "card_lifecycle_record_collision", 500,
                new Dictionary<string, object?> { ["person_id"] = record.PersonId, ["card_class"] = record.CardClass, ["sequence_number"] = record.SequenceNumber });
        }

        return record;
    }

    public CardLifecycleRecord Replace(CardLifecycleRecord expected, CardLifecycleRecord updated)
    {
        var key = Key(expected.TenantId, expected.PersonId, expected.CardClass, expected.SequenceNumber);

        if (!_byKey.TryGetValue(key, out var current))
        {
            throw CardLifecycleError.RecordNotFound(expected.PersonId, expected.CardClass, expected.SequenceNumber);
        }

        if (!_byKey.TryUpdate(key, updated, expected))
        {
            var latest = _byKey.TryGetValue(key, out var p) ? p : current;
            throw CardLifecycleError.ConcurrencyConflict(expected.Version, latest.Version);
        }

        return updated;
    }

    public IReadOnlyList<CardLifecycleRecord> ListDenyList(string tenantId, string? since) =>
        [.. _byKey.Values
            .Where(r => r.TenantId == tenantId && r.LockState is CardLockState.Locked or CardLockState.RevocationPending or CardLockState.PermanentlyRevoked)
            .Where(r => since is null || string.CompareOrdinal(r.UpdatedAt, since) >= 0)
            .OrderBy(r => r.UpdatedAt, StringComparer.Ordinal)];

    private static string Key(string tenantId, string personId, string cardClass, int sequenceNumber) =>
        $"{tenantId}\u0000{personId}\u0000{cardClass}\u0000{sequenceNumber}";
}
