using System.Collections.Concurrent;

namespace Biolog.Features.CardProduction.Domain;

/// <summary>
/// Tenant-scoped production-job storage. A single, process-lifetime, cross-tenant dictionary
/// standing in for the per-tenant database ADR-0002 Decision 4.1 describes - the same
/// simplification every in-memory repository in this codebase draws around itself (ADR-0005).
/// </summary>
public interface IProductionJobRepository
{
    /// <summary>The job sequence number a NEW job for this (tenant, person, card class, sequence) would receive - starts at 1. A pure read; see <see cref="Create"/>.</summary>
    int NextJobSequenceNumber(string tenantId, string personId, string cardClass, int sequenceNumber);

    /// <summary>Commits a fully-formed, already-audited job. Throws on a collision at an already-occupied key - unreachable in practice under the caller's own lock.</summary>
    ProductionJob Create(ProductionJob job);

    ProductionJob? Get(string tenantId, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber);

    /// <summary>Ordered by job sequence number ascending. Empty if this card issue has never been produced.</summary>
    IReadOnlyList<ProductionJob> List(string tenantId, string personId, string cardClass, int sequenceNumber);

    /// <summary>AC02: the single job whose Passed status is what a subsequent activation attempt is gated on - the most recent attempt for this card, whatever its status.</summary>
    ProductionJob? GetLatest(string tenantId, string personId, string cardClass, int sequenceNumber);

    /// <summary>Compare-and-set replace (ADR-0001 §3.2).</summary>
    ProductionJob Replace(ProductionJob expected, ProductionJob updated);

    /// <summary>
    /// BIO-RPT-002: every production job for one tenant, across every person/card class/sequence -
    /// not ordered by any particular key. Purely additive to this story's original AC01-AC05 scope
    /// (every existing method/behaviour above is unchanged) - the governed report builder's own
    /// whitelisted "card production by status" dimension needs a tenant-wide read that no prior
    /// caller of this repository ever required, since every other method here is keyed to one
    /// specific card issue.
    /// </summary>
    IReadOnlyList<ProductionJob> ListForTenant(string tenantId);
}

internal sealed class InMemoryProductionJobRepository : IProductionJobRepository
{
    private readonly ConcurrentDictionary<string, ProductionJob> _byKey = new(StringComparer.Ordinal);

    public int NextJobSequenceNumber(string tenantId, string personId, string cardClass, int sequenceNumber)
    {
        var existing = List(tenantId, personId, cardClass, sequenceNumber);
        return existing.Count == 0 ? 1 : existing[^1].JobSequenceNumber + 1;
    }

    public ProductionJob Create(ProductionJob job)
    {
        var key = Key(job.TenantId, job.PersonId, job.CardClass, job.SequenceNumber, job.JobSequenceNumber);
        if (!_byKey.TryAdd(key, job))
        {
            throw new ProductionError(
                $"production job collision at job sequence {job.JobSequenceNumber} for {job.PersonId}/{job.CardClass}/{job.SequenceNumber}",
                "production_job_collision", 500,
                new Dictionary<string, object?> { ["person_id"] = job.PersonId, ["card_class"] = job.CardClass, ["sequence_number"] = job.SequenceNumber, ["job_sequence_number"] = job.JobSequenceNumber });
        }

        return job;
    }

    public ProductionJob? Get(string tenantId, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) =>
        _byKey.TryGetValue(Key(tenantId, personId, cardClass, sequenceNumber, jobSequenceNumber), out var job) ? job : null;

    public IReadOnlyList<ProductionJob> List(string tenantId, string personId, string cardClass, int sequenceNumber) =>
        [.. _byKey.Values
            .Where(j => j.TenantId == tenantId && j.PersonId == personId && j.CardClass == cardClass && j.SequenceNumber == sequenceNumber)
            .OrderBy(j => j.JobSequenceNumber)];

    public ProductionJob? GetLatest(string tenantId, string personId, string cardClass, int sequenceNumber) =>
        List(tenantId, personId, cardClass, sequenceNumber) is { Count: > 0 } jobs ? jobs[^1] : null;

    public ProductionJob Replace(ProductionJob expected, ProductionJob updated)
    {
        var key = Key(expected.TenantId, expected.PersonId, expected.CardClass, expected.SequenceNumber, expected.JobSequenceNumber);

        if (!_byKey.TryGetValue(key, out var current))
        {
            throw ProductionError.JobNotFound(expected.PersonId, expected.CardClass, expected.SequenceNumber, expected.JobSequenceNumber);
        }

        if (!_byKey.TryUpdate(key, updated, expected))
        {
            var latest = _byKey.TryGetValue(key, out var p) ? p : current;
            throw ProductionError.ConcurrencyConflict(expected.Version, latest.Version);
        }

        return updated;
    }

    public IReadOnlyList<ProductionJob> ListForTenant(string tenantId) =>
        [.. _byKey.Values.Where(j => j.TenantId == tenantId)];

    private static string Key(string tenantId, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) =>
        $"{tenantId}\u0000{personId}\u0000{cardClass}\u0000{sequenceNumber}\u0000{jobSequenceNumber}";
}
