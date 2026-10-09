using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>In-process, in-memory <see cref="IFingerprintDuplicateReviewRepository"/> for local
/// development and this story's own tests - explicitly NOT a real persistence layer. Internal,
/// same seam as the interface. Re-validates the optimistic-concurrency version independently of
/// <see cref="Service.FingerprintSearchService"/> at the persistence boundary (ADR-0001 Decision
/// 3), the same dual-boundary discipline every prior BIO-BIO/BIO-IDN repository applies.</summary>
internal sealed class InMemoryFingerprintDuplicateReviewRepository : IFingerprintDuplicateReviewRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonIdLow, string PersonIdHigh), FingerprintDuplicateReviewEntry> _entries = new();

    public Task<FingerprintDuplicateReviewEntry> CreateAsync(FingerprintDuplicateReviewEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Version != 1)
        {
            throw new InvalidOperationException(
                $"invalid_initial_version: entry '{entry.TenantId}/{entry.PersonIdLow}/{entry.PersonIdHigh}' must start at version 1.");
        }
        var key = (entry.TenantId, entry.PersonIdLow, entry.PersonIdHigh);
        if (!_entries.TryAdd(key, entry))
        {
            throw new InvalidOperationException($"fingerprint_duplicate_review_entry_already_exists: '{entry.PersonIdLow}/{entry.PersonIdHigh}'.");
        }
        return Task.FromResult(entry);
    }

    public Task<FingerprintDuplicateReviewEntry?> GetAsync(
        string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue((tenantId, personIdLow, personIdHigh), out var entry) || entry.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null, not an exception - ADR-0001 §1.5, no
            // enumeration oracle distinguishing "does not exist" from "belongs to another tenant".
            return Task.FromResult<FingerprintDuplicateReviewEntry?>(null);
        }
        return Task.FromResult<FingerprintDuplicateReviewEntry?>(entry);
    }

    public Task<FingerprintDuplicateReviewEntry> ReplaceAsync(
        FingerprintDuplicateReviewEntry expected, FingerprintDuplicateReviewEntry updated, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);
        if (updated.TenantId != expected.TenantId || updated.PersonIdLow != expected.PersonIdLow || updated.PersonIdHigh != expected.PersonIdHigh)
        {
            throw new InvalidOperationException("identity_or_tenant_immutable: a replace cannot change the tenant or the pair.");
        }

        var key = (expected.TenantId, expected.PersonIdLow, expected.PersonIdHigh);
        if (!_entries.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw new InvalidOperationException($"fingerprint_duplicate_review_entry_not_found: '{expected.PersonIdLow}/{expected.PersonIdHigh}'.");
        }
        if (current.Version != expected.Version)
        {
            throw new InvalidOperationException(
                $"concurrency_conflict: entry '{expected.PersonIdLow}/{expected.PersonIdHigh}' expected version {expected.Version} but found {current.Version}.");
        }

        _entries[key] = updated;
        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<FingerprintDuplicateReviewEntry>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FingerprintDuplicateReviewEntry> matches = _entries.Values
            .Where(e => e.TenantId == tenantId)
            .ToList();
        return Task.FromResult(matches);
    }
}
